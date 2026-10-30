using System.Net;
using System.Net.Http.Json;
using EnglishCam.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishCam.Api.Tests;

public class GameTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static HashSet<string> Ids(IEnumerable<BadgeDto> b) => b.Select(x => x.Id).ToHashSet();

    [Fact]
    public async Task Match_stars_are_computed_by_the_server_and_a_client_claim_is_ignored()
    {
        var kid = await f.NewKidAsync();
        // 5 câu đúng ngay lần đầu + 3 câu đúng sau khi chọn sai → 5 sao. Cố tình khai gian "starsEarned": 999.
        var session = new { id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 60, starsEarned = 999, stars = 999,
            answers = ((dynamic)G.Match(5, 3)).answers };
        var res = await G.PlayAsync(kid.Device.Client, session);
        Assert.Equal(5, res.StarsEarned);
        Assert.Equal(5, res.Progress.Stars);
        Assert.Equal(1, res.Progress.Rounds);
        Assert.Equal(0, res.Progress.PerfectRounds);
        Assert.Equal(["first_round"], Ids(res.NewBadges));
    }

    [Fact]
    public async Task A_perfect_round_gives_a_star_per_question_and_the_perfect_badge()
    {
        var kid = await f.NewKidAsync();
        var res = await G.PlayAsync(kid.Device.Client, G.Match(8));
        Assert.Equal(8, res.StarsEarned);
        Assert.Equal(1, res.Progress.PerfectRounds);
        Assert.Equal(["first_round", "perfect"], Ids(res.NewBadges).Order());
        Assert.Equal(["first_round", "perfect"], res.Progress.BadgeIds.Order());
    }

    [Fact]
    public async Task Perfect_needs_at_least_five_answers()
    {
        var kid = await f.NewKidAsync();
        var res = await G.PlayAsync(kid.Device.Client, G.Match(3));
        Assert.Equal(3, res.StarsEarned);
        Assert.Equal(0, res.Progress.PerfectRounds);
        Assert.DoesNotContain("perfect", res.Progress.BadgeIds);
    }

    [Fact]
    public async Task Hunt_gives_two_stars_when_the_model_confirms_and_one_when_a_parent_does()
    {
        var kid = await f.NewKidAsync();
        var a = await G.PlayAsync(kid.Device.Client, G.Hunt("cup", "model"));
        var b = await G.PlayAsync(kid.Device.Client, G.Hunt("bowl", "parent"));
        Assert.Equal(2, a.StarsEarned);
        Assert.Equal(1, b.StarsEarned);
        Assert.Equal(3, b.Progress.Stars);
        Assert.Equal(2, b.Progress.Found);
        Assert.Equal(0, b.Progress.Rounds); // Tìm vật không tính là "ván"
        Assert.Contains("finder_1", a.NewBadges.Select(x => x.Id));
    }

    [Fact]
    public async Task Resending_the_same_session_never_double_counts()
    {
        var kid = await f.NewKidAsync();
        var session = G.Match(8);
        var first = await G.PlayAsync(kid.Device.Client, session);
        var second = await G.PlayAsync(kid.Device.Client, session);
        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(first.StarsEarned, second.StarsEarned);
        Assert.Empty(second.NewBadges);
        Assert.Equal(8, (await G.MeAsync(kid.Device.Client)).Stars);
    }

    [Fact]
    public async Task The_same_session_sent_at_the_same_time_from_two_devices_counts_once()
    {
        var kid = await f.NewKidAsync();
        var other = await f.AddDeviceAsync(kid);
        var session = G.Match(6);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            G.PlayAsync(i % 2 == 0 ? kid.Device.Client : other.Client, session)));
        Assert.Equal(1, results.Count(r => !r.Duplicate));
        var me = await G.MeAsync(kid.Device.Client);
        Assert.Equal(6, me.Stars);
        Assert.Equal(1, me.Rounds);
    }

    [Fact]
    public async Task Another_childs_session_id_is_a_conflict()
    {
        var a = await f.NewKidAsync();
        var b = await f.NewKidAsync();
        var session = G.Match(4);
        await G.PlayAsync(a.Device.Client, session);
        var r = await b.Device.Client.PostAsJsonAsync("/api/games/sessions", session);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0, (await G.MeAsync(b.Device.Client)).Stars);
    }

    [Fact]
    public async Task Invalid_sessions_are_rejected_without_changing_anything()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        async Task<HttpStatusCode> Post(object body) => (await c.PostAsJsonAsync("/api/games/sessions", body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { id = Guid.NewGuid(), gameType = "poker", durationSec = 1, answers = new[] { G.Answer("cup") } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { id = Guid.NewGuid(), gameType = "match", durationSec = 1, answers = Array.Empty<object>() }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { id = Guid.NewGuid(), gameType = "match", durationSec = 1, answers = Enumerable.Range(0, 21).Select(_ => G.Answer("cup")).ToArray() }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { id = Guid.NewGuid(), gameType = "match", durationSec = 1, answers = new[] { G.Answer("khong_co_tu_nay") } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { id = Guid.NewGuid(), gameType = "match", durationSec = 1, answers = new[] { G.Answer("cup", true, 0) } })); // attempts < 1
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { id = Guid.NewGuid(), gameType = "hunt", durationSec = 1, answers = new[] { G.Answer("cup", false) } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { id = Guid.NewGuid(), gameType = "match", durationSec = -5, answers = new[] { G.Answer("cup") } }));
        var me = await G.MeAsync(c);
        Assert.Equal((0, 0), (me.Stars, me.Rounds));
    }

    [Fact]
    public async Task Unknown_words_inside_an_otherwise_valid_session_are_dropped()
    {
        var kid = await f.NewKidAsync();
        var res = await G.PlayAsync(kid.Device.Client, new
        {
            id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 30,
            answers = new[] { G.Answer("cup"), G.Answer("tu_ma_khong_co"), G.Answer("bowl") }
        });
        Assert.Equal(2, res.StarsEarned);
    }

    [Fact]
    public async Task Wrong_answers_demote_a_word_and_right_ones_move_it_up_the_boxes()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        // cup: sai (phải chọn 2 lần) → hộp 1; bowl: đúng ngay → hộp 2
        await G.PlayAsync(c, new { id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 20,
            answers = new[] { G.Answer("cup", true, 3), G.Answer("bowl") } });
        await f.WithDbAsync(async db =>
        {
            var rows = await db.WordProgress.Where(p => p.ChildId == kid.ChildId).ToDictionaryAsync(p => p.WordId);
            Assert.Equal((1, 1, 0), (rows["cup"].Box, rows["cup"].WrongCount, rows["cup"].CorrectCount));
            Assert.Equal((2, 0, 1), (rows["bowl"].Box, rows["bowl"].WrongCount, rows["bowl"].CorrectCount));
            Assert.True(rows["cup"].NextDueAt <= DateTime.UtcNow.AddSeconds(5), "từ sai phải được hỏi lại ngay");
            Assert.True(rows["bowl"].NextDueAt > DateTime.UtcNow, "từ đúng được hẹn ôn lại sau");
        });
        // bowl đúng thêm hai lần nữa → lên hộp 4; cup đúng ngay → hộp 2
        await G.PlayAsync(c, new { id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 20,
            answers = new[] { G.Answer("bowl"), G.Answer("cup") } });
        await G.PlayAsync(c, new { id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 20, answers = new[] { G.Answer("bowl") } });
        await f.WithDbAsync(async db =>
        {
            var rows = await db.WordProgress.Where(p => p.ChildId == kid.ChildId).ToDictionaryAsync(p => p.WordId);
            Assert.Equal(4, rows["bowl"].Box);
            Assert.Equal(2, rows["cup"].Box);
        });
    }

    [Fact]
    public async Task Playing_on_consecutive_days_builds_a_streak_and_unlocks_the_streak_badge()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        await G.PlayAsync(c, G.Match(2, startedAt: G.At(2)));
        await G.PlayAsync(c, G.Match(2, startedAt: G.At(1)));
        var r3 = await G.PlayAsync(c, G.Match(2, startedAt: G.At(0)));
        Assert.Equal(3, r3.Progress.CurrentStreak);
        Assert.Equal(3, r3.Progress.BestStreak);
        Assert.Contains("streak_3", r3.Progress.BadgeIds);
        Assert.DoesNotContain("streak_7", r3.Progress.BadgeIds);
    }

    [Fact]
    public async Task Missing_a_day_resets_the_current_streak_but_keeps_the_best()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        await G.PlayAsync(c, G.Match(2, startedAt: G.At(6)));
        await G.PlayAsync(c, G.Match(2, startedAt: G.At(5)));
        await G.PlayAsync(c, G.Match(2, startedAt: G.At(4)));
        var gap = await G.PlayAsync(c, G.Match(2, startedAt: G.At(1))); // nghỉ 2 ngày
        Assert.Equal(3, gap.Progress.BestStreak);
        Assert.Equal(1, gap.Progress.CurrentStreak);
        // Đã lâu không học: chuỗi hiện tại hiển thị 0 nhưng kỷ lục vẫn còn.
        var old = await f.NewKidAsync();
        await G.PlayAsync(old.Device.Client, G.Match(2, startedAt: G.At(5)));
        await G.PlayAsync(old.Device.Client, G.Match(2, startedAt: G.At(4)));
        var me = await G.MeAsync(old.Device.Client);
        Assert.Equal(0, me.CurrentStreak);
        Assert.Equal(2, me.BestStreak);
    }

    [Fact]
    public async Task Sessions_arriving_late_and_out_of_order_do_not_break_the_streak()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        await G.PlayAsync(c, G.Match(2, startedAt: G.At(0)));
        var late = await G.PlayAsync(c, G.Match(2, startedAt: G.At(1))); // ván hôm qua đồng bộ muộn
        Assert.Equal(1, late.Progress.CurrentStreak);
        Assert.Equal(4, late.Progress.Stars);
    }

    [Fact]
    public async Task Too_many_sessions_in_one_hour_get_429()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Device.Client;
        var now = DateTime.UtcNow;
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/games/sessions", G.Match(1, startedAt: now.AddSeconds(-i)))).StatusCode);
        var r = await c.PostAsJsonAsync("/api/games/sessions", G.Match(1, startedAt: now));
        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        Assert.Equal(60, (await G.MeAsync(c)).Stars);
    }

    [Fact]
    public async Task Two_devices_of_one_child_add_up()
    {
        var kid = await f.NewKidAsync();
        var tablet = await f.AddDeviceAsync(kid);
        await G.PlayAsync(kid.Device.Client, G.Match(3));
        await G.PlayAsync(tablet.Client, G.Hunt("cup"));
        var a = await G.MeAsync(kid.Device.Client);
        var b = await G.MeAsync(tablet.Client);
        Assert.Equal((5, 1, 1), (a.Stars, a.Rounds, a.Found));
        Assert.Equal((a.Stars, a.Rounds, a.Found, a.CurrentStreak), (b.Stars, b.Rounds, b.Found, b.CurrentStreak));
        Assert.Equal(a.BadgeIds, b.BadgeIds);
        Assert.Equal(a.LearnedWordIds, b.LearnedWordIds);
    }

    [Fact]
    public async Task Two_children_never_see_each_others_progress()
    {
        var a = await f.NewKidAsync();
        var b = await f.NewKidAsync();
        await G.PlayAsync(a.Device.Client, G.Match(8));
        var other = await G.MeAsync(b.Device.Client);
        Assert.Equal((0, 0), (other.Stars, other.Rounds));
        Assert.Empty(other.BadgeIds);
    }

    [Fact]
    public async Task Only_a_child_token_can_use_game_and_progress_endpoints()
    {
        var kid = await f.NewKidAsync();
        var admin = await f.LoginAdminAsync();
        foreach (var client in new[] { kid.Parent.Client, admin.Client })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/games/match/questions")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/games/badges")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/progress/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/games/sessions", G.Match(2))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync("/api/progress/me")).StatusCode);
    }

    [Fact]
    public async Task Match_questions_are_well_formed_and_only_use_published_words()
    {
        var kid = await f.NewKidAsync();
        var admin = await f.LoginAdminAsync();
        var set = (await kid.Device.Client.GetFromJsonAsync<QuestionSetDto>("/api/games/match/questions?count=8"))!;
        Assert.Equal(8, set.Questions.Count);
        Assert.True(set.VocabVersion >= 1);
        foreach (var q in set.Questions)
        {
            Assert.Equal(4, q.OptionIds.Distinct().Count());
            Assert.Single(q.OptionIds, id => id == q.WordId);
        }
        Assert.Equal(1, (await kid.Device.Client.GetFromJsonAsync<QuestionSetDto>("/api/games/match/questions?count=0"))!.Questions.Count);
        Assert.Equal(20, (await kid.Device.Client.GetFromJsonAsync<QuestionSetDto>("/api/games/match/questions?count=999"))!.Questions.Count);

        // Từ mới ở bản nháp chưa được hỏi; sau khi xuất bản thì có.
        var id = $"w_{Guid.NewGuid():N}"[..14];
        (await admin.Client.PostAsJsonAsync("/api/admin/words", new
        {
            id, en = id, vi = "từ thử", phonetic = "/t/", exampleEn = "A.", exampleVi = "B.", category = "animals",
            audio = "assets/audio/words/book.mp3", emoji = "🦒"
        })).EnsureSuccessStatusCode();
        async Task<bool> Appears()
        {
            for (var i = 0; i < 25; i++)
            {
                var s = (await kid.Device.Client.GetFromJsonAsync<QuestionSetDto>("/api/games/match/questions?count=20"))!;
                if (s.Questions.Any(q => q.OptionIds.Contains(id))) return true;
            }
            return false;
        }
        Assert.False(await Appears(), "từ chưa xuất bản không được xuất hiện");
        (await admin.Client.PostAsJsonAsync("/api/admin/vocab/publish", new { notes = "thêm từ thử" })).EnsureSuccessStatusCode();
        Assert.True(await Appears(), "từ đã xuất bản phải xuất hiện");
    }

    [Fact]
    public async Task Hunt_targets_only_offer_words_the_model_can_recognise_and_the_game_allows()
    {
        var kid = await f.NewKidAsync();
        var ids = new HashSet<string>();
        for (var i = 0; i < 30; i++)
            ids.UnionWith((await kid.Device.Client.GetFromJsonAsync<HuntTargetsDto>("/api/games/hunt/targets?count=10"))!.WordIds);
        Assert.DoesNotContain("apple", ids);  // model nhận apple kém → hunt=false
        Assert.DoesNotContain("table", ids);  // table hay bị đoán nhầm → hunt=false
        Assert.DoesNotContain("duck", ids);   // từ chỉ để học, không có nhãn model
        Assert.Contains("cup", ids);
        var one = (await kid.Device.Client.GetFromJsonAsync<HuntTargetsDto>("/api/games/hunt/targets?count=3"))!;
        Assert.Equal(3, one.WordIds.Distinct().Count());
    }

    [Fact]
    public async Task Badge_list_shows_which_ones_a_child_has_unlocked()
    {
        var kid = await f.NewKidAsync();
        await G.PlayAsync(kid.Device.Client, G.Match(8));
        var list = (await kid.Device.Client.GetFromJsonAsync<List<ChildBadgeDto>>("/api/games/badges"))!;
        Assert.Equal(10, list.Count);
        Assert.Equal(["first_round", "perfect"], list.Where(b => b.Unlocked).Select(b => b.Id).Order());
        Assert.All(list.Where(b => b.Unlocked), b => Assert.NotNull(b.UnlockedAt));
        Assert.All(list.Where(b => !b.Unlocked), b => Assert.Null(b.UnlockedAt));
    }

    [Fact]
    public async Task Deleting_a_child_removes_all_of_their_progress()
    {
        var kid = await f.NewKidAsync();
        await G.PlayAsync(kid.Device.Client, G.Match(8));
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "cup", true), G.Ev("screenTime", seconds: 30));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await kid.Parent.Client.DeleteAsync($"/api/children/{kid.ChildId}")).StatusCode);
        await f.WithDbAsync(async db =>
        {
            Assert.False(await db.GameSessions.AnyAsync(s => s.ChildId == kid.ChildId));
            Assert.False(await db.GameAnswers.AnyAsync(a => db.GameSessions.All(s => s.Id != a.SessionId)));
            Assert.False(await db.ChildStats.AnyAsync(s => s.ChildId == kid.ChildId));
            Assert.False(await db.WordProgress.AnyAsync(p => p.ChildId == kid.ChildId));
            Assert.False(await db.ChildBadges.AnyAsync(b => b.ChildId == kid.ChildId));
            Assert.False(await db.DailyActivities.AnyAsync(d => d.ChildId == kid.ChildId));
            Assert.False(await db.ProcessedEvents.AnyAsync(e => e.ChildId == kid.ChildId));
        });
    }
}
