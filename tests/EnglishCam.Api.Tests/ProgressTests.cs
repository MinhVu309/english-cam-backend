using System.Net;
using System.Net.Http.Json;
using EnglishCam.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishCam.Api.Tests;

public class ProgressTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Marking_words_learned_and_unlearned_is_reflected_in_the_snapshot()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        var r = await G.SendAsync(c, G.Ev("learned", "cup", true, at: DateTime.UtcNow.AddSeconds(-3)), G.Ev("learned", "bowl", true, at: DateTime.UtcNow.AddSeconds(-2)));
        Assert.Equal((2, 0, 0), (r.Applied, r.Duplicates, r.Ignored));
        Assert.Equal(["bowl", "cup"], r.Progress.LearnedWordIds);
        Assert.Equal(2, r.Progress.Today.WordsLearned);

        var r2 = await G.SendAsync(c, G.Ev("learned", "cup", false));
        Assert.Equal(["bowl"], r2.Progress.LearnedWordIds);
        Assert.Equal(2, r2.Progress.Today.WordsLearned); // lịch sử trong ngày không bị trừ
    }

    [Fact]
    public async Task Learned_sets_from_two_devices_are_merged()
    {
        var kid = await f.NewKidAsync();
        var tablet = await f.AddDeviceAsync(kid);
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "cup", true));
        await G.SendAsync(tablet.Client, G.Ev("learned", "bowl", true));
        Assert.Equal(["bowl", "cup"], (await G.MeAsync(kid.Device.Client)).LearnedWordIds);
        Assert.Equal(["bowl", "cup"], (await G.MeAsync(tablet.Client)).LearnedWordIds);
    }

    [Fact]
    public async Task The_newer_toggle_wins_no_matter_which_device_syncs_first()
    {
        var kid = await f.NewKidAsync();
        var tablet = await f.AddDeviceAsync(kid);
        var t1 = DateTime.UtcNow.AddMinutes(-10); // máy A bật "đã học" lúc t1
        var t2 = DateTime.UtcNow.AddMinutes(-5);  // máy B tắt lúc t2 (mới hơn)
        // B đồng bộ trước, A (cũ hơn) đồng bộ sau → vẫn là "chưa học"
        await G.SendAsync(tablet.Client, G.Ev("learned", "cup", false, at: t2));
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "cup", true, at: t1));
        Assert.DoesNotContain("cup", (await G.MeAsync(kid.Device.Client)).LearnedWordIds);

        // Ngược lại: bản mới hơn (bật) đến sau bản cũ (tắt) → "đã học"
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "bowl", false, at: t1));
        await G.SendAsync(tablet.Client, G.Ev("learned", "bowl", true, at: t2));
        Assert.Contains("bowl", (await G.MeAsync(kid.Device.Client)).LearnedWordIds);
    }

    [Fact]
    public async Task Events_are_applied_once_even_when_resent()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        var ev = G.Ev("scan", "cup");
        var first = await G.SendAsync(c, ev);
        var again = await G.SendAsync(c, ev);
        Assert.Equal(1, first.Applied);
        Assert.Equal((0, 1), (again.Applied, again.Duplicates));
        Assert.Equal(1, (await G.MeAsync(c)).Today.Scans);

        // Cùng một id hai lần trong một lô cũng chỉ áp dụng một lần.
        var dup = G.Ev("scan", "bowl");
        var batch = await G.SendAsync(c, dup, dup);
        Assert.Equal((1, 1), (batch.Applied, batch.Duplicates));
        Assert.Equal(2, (await G.MeAsync(c)).Today.Scans);
    }

    [Fact]
    public async Task Concurrent_resends_of_the_same_batch_apply_once()
    {
        var kid = await f.NewKidAsync();
        var other = await f.AddDeviceAsync(kid);
        var ev = G.Ev("screenTime", seconds: 100);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => G.SendAsync(i % 2 == 0 ? kid.Device.Client : other.Client, ev)));
        Assert.Equal(1, results.Sum(r => r.Applied));
        Assert.Equal(100, (await G.MeAsync(kid.Device.Client)).Today.Seconds);
    }

    [Fact]
    public async Task Bad_events_are_ignored_not_fatal()
    {
        var kid = await f.NewKidAsync();
        var r = await G.SendAsync(kid.Device.Client,
            G.Ev("learned", "tu_ma_khong_co", true),     // từ lạ
            G.Ev("learned", "cup"),                       // thiếu learned
            G.Ev("scan"),                                 // thiếu wordId
            G.Ev("dance", "cup"),                         // loại lạ
            G.Ev("screenTime", seconds: 0),               // 0 giây
            G.Ev("scan", "cup"));                         // hợp lệ
        Assert.Equal((1, 0, 5), (r.Applied, r.Duplicates, r.Ignored));
    }

    [Fact]
    public async Task Event_batches_are_limited_to_200()
    {
        var kid = await f.NewKidAsync();
        var many = Enumerable.Range(0, 201).Select(_ => G.Ev("scan", "cup")).ToArray();
        var r = await kid.Device.Client.PostAsJsonAsync("/api/progress/events", new { events = many });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var max = Enumerable.Range(0, 200).Select(_ => G.Ev("scan", "cup")).ToArray();
        Assert.Equal(200, (await G.SendAsync(kid.Device.Client, max)).Applied);
    }

    [Fact]
    public async Task Screen_time_is_clamped_per_event_and_per_day()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        await G.SendAsync(c, G.Ev("screenTime", seconds: 99999)); // tối đa 1 giờ mỗi sự kiện
        Assert.Equal(3600, (await G.MeAsync(c)).Today.Seconds);
        await G.SendAsync(c, Enumerable.Range(0, 30).Select(_ => G.Ev("screenTime", seconds: 3600)).ToArray()); // vượt 24h/ngày
        Assert.Equal(86400, (await G.MeAsync(c)).Today.Seconds);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.PostAsJsonAsync("/api/progress/events", new { events = new[] { G.Ev("screenTime", seconds: -5) } })).StatusCode);
    }

    [Fact]
    public async Task Scans_count_toward_the_day_and_the_streak_but_screen_time_alone_does_not()
    {
        var idle = await f.NewKidAsync();
        var r0 = await G.SendAsync(idle.Device.Client, G.Ev("screenTime", seconds: 600));
        Assert.Equal(0, r0.Progress.CurrentStreak); // chỉ mở app không tính là học

        var kid = await f.NewKidAsync();
        var r = await G.SendAsync(kid.Device.Client, G.Ev("scan", "cup"));
        Assert.Equal(1, r.Progress.CurrentStreak);
        Assert.Equal(1, r.Progress.Today.Scans);
    }

    [Fact]
    public async Task Timestamps_far_in_the_past_or_future_fall_back_to_server_time()
    {
        var kid = await f.NewKidAsync();
        var r = await G.SendAsync(kid.Device.Client,
            G.Ev("scan", "cup", at: DateTime.UtcNow.AddYears(-2)),
            G.Ev("scan", "bowl", at: DateTime.UtcNow.AddYears(2)));
        Assert.Equal(2, r.Progress.Today.Scans); // cả hai bị coi là "bây giờ", không tạo ngày giả
        Assert.Equal(1, r.Progress.CurrentStreak);
    }

    [Fact]
    public async Task Offline_backlog_from_earlier_days_lands_on_the_right_days()
    {
        var kid = await f.NewKidAsync();
        await G.SendAsync(kid.Device.Client, G.Ev("scan", "cup", at: G.At(3)), G.Ev("scan", "bowl", at: G.At(2)), G.Ev("scan", "pen", at: G.At(1)), G.Ev("scan", "bag", at: G.At(0)));
        var me = await G.MeAsync(kid.Device.Client);
        Assert.Equal(4, me.CurrentStreak);
        Assert.Equal(1, me.Today.Scans);
    }

    [Fact]
    public async Task Learning_ten_words_unlocks_the_learner_badge()
    {
        var kid = await f.NewKidAsync();
        var first = await G.SendAsync(kid.Device.Client, G.Words.Take(9).Select(w => G.Ev("learned", w, true)).ToArray());
        Assert.DoesNotContain("learner_10", first.Progress.BadgeIds);
        var tenth = await G.SendAsync(kid.Device.Client, G.Ev("learned", G.Words[9], true));
        Assert.Contains("learner_10", tenth.NewBadges.Select(b => b.Id));
    }

    [Fact]
    public async Task Parent_settings_reach_the_child_snapshot()
    {
        var kid = await f.NewKidAsync();
        (await kid.Parent.Client.PutAsJsonAsync($"/api/children/{kid.ChildId}/settings", new { dailyLimitMinutes = 45, dailyWordGoal = 9 })).EnsureSuccessStatusCode();
        var me = await G.MeAsync(kid.Device.Client);
        Assert.Equal((45, 9), (me.DailyLimitMinutes, me.DailyWordGoal));
        Assert.Equal(DateTime.UtcNow, me.ServerTime, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_revoked_device_can_no_longer_send_progress()
    {
        var kid = await f.NewKidAsync();
        await kid.Parent.Client.PostAsync($"/api/children/{kid.ChildId}/revoke-sessions", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kid.Device.Client.PostAsJsonAsync("/api/progress/events", new { events = new[] { G.Ev("scan", "cup") } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kid.Device.Client.PostAsJsonAsync("/api/games/sessions", G.Match(3))).StatusCode);
    }
}

public class ProgressResetTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_parent_can_wipe_a_childs_progress_but_not_the_profile_or_devices()
    {
        var kid = await f.NewKidAsync();
        var other = await f.NewKidAsync();
        await G.PlayAsync(kid.Device.Client, G.Match(8));
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "cup", true), G.Ev("screenTime", seconds: 30));
        await G.PlayAsync(other.Device.Client, G.Match(4));
        Assert.True((await G.MeAsync(kid.Device.Client)).Stars > 0);

        Assert.Equal(HttpStatusCode.NoContent, (await kid.Parent.Client.DeleteAsync($"/api/children/{kid.ChildId}/progress")).StatusCode);

        var me = await G.MeAsync(kid.Device.Client); // thiết bị vẫn đăng nhập
        Assert.Equal((0, 0, 0, 0), (me.Stars, me.Rounds, me.LearnedWordIds.Count, me.BadgeIds.Count));
        Assert.Equal((0, 0), (me.Today.Seconds, me.CurrentStreak));
        Assert.Equal(4, (await G.MeAsync(other.Device.Client)).Stars); // bé khác không bị ảnh hưởng
        Assert.Equal(HttpStatusCode.OK, (await kid.Parent.Client.GetAsync($"/api/children/{kid.ChildId}")).StatusCode);

        // Sau khi xoá, chơi lại tính từ đầu và huy hiệu mở lại được.
        var again = await G.PlayAsync(kid.Device.Client, G.Match(8));
        Assert.Contains("first_round", again.NewBadges.Select(b => b.Id));
        Assert.Equal(8, again.Progress.Stars);
    }

    [Fact]
    public async Task Only_the_childs_own_parent_or_an_admin_can_wipe_progress()
    {
        var kid = await f.NewKidAsync();
        var stranger = await f.RegisterParentAsync();
        await G.PlayAsync(kid.Device.Client, G.Match(5));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.DeleteAsync($"/api/children/{kid.ChildId}/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kid.Device.Client.DeleteAsync($"/api/children/{kid.ChildId}/progress")).StatusCode);
        Assert.Equal(5, (await G.MeAsync(kid.Device.Client)).Stars);
        var admin = await f.LoginAdminAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/children/{kid.ChildId}/progress")).StatusCode);
    }
}
