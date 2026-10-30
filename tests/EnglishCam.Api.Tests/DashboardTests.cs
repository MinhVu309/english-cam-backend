using System.Net;
using System.Net.Http.Json;
using System.Text;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Services;
using Xunit;

namespace EnglishCam.Api.Tests;

public class DashboardTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private static Task<ChildDashboardDto> Dash(HttpClient parent, Guid child, int days = 7) =>
        parent.GetFromJsonAsync<ChildDashboardDto>($"/api/parent/children/{child}/dashboard?days={days}")!;

    /// <summary>Từ → chủ đề, lấy từ chính /api/vocab mà app nhận.</summary>
    private async Task<Dictionary<string, string>> CategoryOfAsync()
    {
        var vocab = (await f.Anonymous().GetFromJsonAsync<System.Text.Json.Nodes.JsonNode>("/api/vocab"))!;
        return vocab["words"]!.AsArray().ToDictionary(w => (string)w!["id"]!, w => (string)w!["category"]!);
    }

    [Fact]
    public async Task Only_the_childs_parent_or_an_admin_can_read_the_dashboard_and_exports()
    {
        var kid = await f.NewKidAsync();
        var stranger = await f.RegisterParentAsync();
        var admin = await f.LoginAdminAsync();
        var id = kid.ChildId;
        foreach (var url in new[] { $"/api/parent/children/{id}/dashboard", $"/api/parent/children/{id}/export.csv", $"/api/parent/children/{id}/export.csv?kind=words" })
        {
            Assert.Equal(HttpStatusCode.OK, (await kid.Parent.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.Device.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync(url)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await kid.Parent.Client.GetAsync($"/api/parent/children/{Guid.NewGuid()}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kid.Device.Client.GetAsync("/api/parent/overview")).StatusCode);
    }

    [Fact]
    public async Task A_new_child_has_a_full_but_empty_dashboard()
    {
        var kid = await f.NewKidAsync("Na");
        var d = await Dash(kid.Parent.Client, kid.ChildId);
        Assert.Equal(("Na", 7, 60), (d.Nickname, d.Days, d.Totals.TotalWords));
        Assert.Equal(7, d.DaysData.Count);
        Assert.Equal(Today, d.DaysData[^1].Date);
        Assert.Equal(Today.AddDays(-6), d.DaysData[0].Date);
        Assert.Equal(d.DaysData.Select(x => x.Date).Order(), d.DaysData.Select(x => x.Date));
        Assert.All(d.DaysData, x => Assert.Equal((0, 0, 0, 0), (x.Seconds, x.WordsLearned, x.Rounds, x.Scans)));
        Assert.Equal((0, 0, 0, 0, 0, 0), (d.Totals.LearnedWords, d.Totals.Stars, d.Totals.Rounds, d.Totals.CurrentStreak, d.Totals.BestStreak, d.Totals.TotalMinutes));
        Assert.Null(d.Totals.AccuracyPercent);
        Assert.Equal(11, d.Categories.Count);
        Assert.All(d.Categories, c => Assert.Equal(0, c.Learned));
        Assert.Equal(60, d.Categories.Sum(c => c.Total));
        Assert.Empty(d.WeakWords);
        Assert.Empty(d.Recent);
        Assert.Empty(d.RecentBadges);
        Assert.Equal((0, 10), (d.BadgesUnlocked, d.BadgesTotal));
        Assert.False(d.Today.GoalMet);
        Assert.Equal(5, d.DailyWordGoal);
        Assert.Null(d.DailyLimitMinutes);
    }

    [Fact]
    public async Task The_days_parameter_is_clamped_to_1_through_90()
    {
        var kid = await f.NewKidAsync();
        var c = kid.Parent.Client;
        Assert.Equal(7, (await c.GetFromJsonAsync<ChildDashboardDto>($"/api/parent/children/{kid.ChildId}/dashboard"))!.DaysData.Count);
        Assert.Equal(30, (await Dash(c, kid.ChildId, 30)).DaysData.Count);
        Assert.Equal(1, (await Dash(c, kid.ChildId, 0)).DaysData.Count);
        Assert.Equal(1, (await Dash(c, kid.ChildId, -5)).DaysData.Count);
        Assert.Equal(90, (await Dash(c, kid.ChildId, 1000)).DaysData.Count);
        Assert.Equal(Today, (await Dash(c, kid.ChildId, 1)).DaysData.Single().Date);
    }

    [Fact]
    public async Task Activity_is_placed_on_the_right_day_and_totals_add_up()
    {
        var kid = await f.NewKidAsync();
        var k = kid.Device.Client;
        // Cách đây 2 ngày: 10 phút dùng app, học 2 từ, nhận ra 1 vật.
        await G.SendAsync(k, G.Ev("screenTime", seconds: 600, at: G.At(2)), G.Ev("learned", "cup", true, at: G.At(2)),
            G.Ev("learned", "bowl", true, at: G.At(2)), G.Ev("scan", "pen", at: G.At(2)));
        // Hôm qua: một ván hoàn hảo (8 sao).
        await G.PlayAsync(k, G.Match(8, startedAt: G.At(1)));
        // Hôm nay: 90 giây, 1 từ mới.
        await G.SendAsync(k, G.Ev("screenTime", seconds: 90), G.Ev("learned", "pen", true));

        var d = await Dash(kid.Parent.Client, kid.ChildId, 7);
        var byDate = d.DaysData.ToDictionary(x => x.Date);
        var two = byDate[Today.AddDays(-2)];
        Assert.Equal((600, 2, 0, 1), (two.Seconds, two.WordsLearned, two.Rounds, two.Scans));
        var one = byDate[Today.AddDays(-1)];
        Assert.Equal((0, 0, 1), (one.Seconds, one.WordsLearned, one.Rounds));
        var now = byDate[Today];
        Assert.Equal((90, 1, 0), (now.Seconds, now.WordsLearned, now.Rounds));
        Assert.Equal((90, 1), (d.Today.Seconds, d.Today.WordsLearned));
        Assert.Equal(3, d.Totals.LearnedWords);
        Assert.Equal((8, 1, 1), (d.Totals.Stars, d.Totals.Rounds, d.Totals.PerfectRounds));
        Assert.Equal(12, d.Totals.TotalMinutes); // (600 + 90) giây = 11,5 phút, làm tròn 12
        Assert.Equal(3, d.Totals.CurrentStreak);
        Assert.Equal(3, d.Totals.BestStreak);
        Assert.Equal(100, d.Totals.AccuracyPercent);
        // Cửa sổ 1 ngày chỉ thấy hôm nay.
        var only = await Dash(kid.Parent.Client, kid.ChildId, 1);
        Assert.Equal(90, only.DaysData.Single().Seconds);
    }

    [Fact]
    public async Task Accuracy_counts_first_try_answers_in_the_window_and_weak_words_rank_by_error_rate()
    {
        var kid = await f.NewKidAsync();
        var k = kid.Device.Client;
        Task Round(params object[] answers) => G.PlayAsync(k, new { id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 30, answers }).ContinueWith(t => t.Result);
        // pen: sai 2 lần (0% đúng); cup: sai 1, đúng 1 (50%); bag: chỉ sai 1 lần (chưa đủ 2 lần gặp → chưa tính); book: luôn đúng.
        await Round(G.Answer("pen", true, 2), G.Answer("cup", true, 3), G.Answer("bag", true, 2), G.Answer("book"));
        await Round(G.Answer("pen", true, 2), G.Answer("cup"), G.Answer("book"));

        var d = await Dash(kid.Parent.Client, kid.ChildId, 7);
        Assert.Equal(["pen", "cup"], d.WeakWords.Select(w => w.WordId));
        var pen = d.WeakWords[0];
        Assert.Equal((2, 2, 0), (pen.Wrong, pen.Total, pen.AccuracyPercent));
        var cup = d.WeakWords[1];
        Assert.Equal((1, 2, 50), (cup.Wrong, cup.Total, cup.AccuracyPercent));
        Assert.False(string.IsNullOrEmpty(pen.En) || string.IsNullOrEmpty(pen.Vi));
        // 7 câu, đúng ngay lần đầu: cup(1) + book(2) = 3 → 43%
        Assert.Equal(43, d.Totals.AccuracyPercent);
    }

    [Fact]
    public async Task At_most_five_weak_words_are_listed()
    {
        var kid = await f.NewKidAsync();
        var answers = G.Words.Take(7).Select(w => G.Answer(w, true, 2)).ToArray();
        await G.PlayAsync(kid.Device.Client, new { id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 30, answers });
        await G.PlayAsync(kid.Device.Client, new { id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 30, answers });
        Assert.Equal(5, (await Dash(kid.Parent.Client, kid.ChildId)).WeakWords.Count);
    }

    [Fact]
    public async Task Category_progress_matches_the_vocabulary()
    {
        var kid = await f.NewKidAsync();
        var cat = await CategoryOfAsync();
        var learned = new[] { "cup", "bowl", "pen", "book", "bag" };
        await G.SendAsync(kid.Device.Client, learned.Select(w => G.Ev("learned", w, true)).ToArray());
        var d = await Dash(kid.Parent.Client, kid.ChildId);
        foreach (var c in d.Categories)
            Assert.Equal(learned.Count(w => cat[w] == c.Id), c.Learned);
        Assert.Equal(5, d.Categories.Sum(c => c.Learned));
        var school = d.Categories.Single(c => c.Id == "school");
        Assert.Equal(5, school.Total);
        Assert.False(string.IsNullOrEmpty(school.Vi));
        Assert.StartsWith("#", school.Color);
    }

    [Fact]
    public async Task Goal_limit_and_minutes_are_reported_for_the_parent_to_judge()
    {
        var kid = await f.NewKidAsync();
        (await kid.Parent.Client.PutAsJsonAsync($"/api/children/{kid.ChildId}/settings", new { dailyLimitMinutes = 30, dailyWordGoal = 2 })).EnsureSuccessStatusCode();
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "cup", true), G.Ev("screenTime", seconds: 45 * 60));
        var one = await Dash(kid.Parent.Client, kid.ChildId);
        Assert.Equal((30, 2), (one.DailyLimitMinutes, one.DailyWordGoal));
        Assert.False(one.Today.GoalMet); // mới học 1/2 từ
        Assert.Equal(45 * 60, one.Today.Seconds); // vượt giới hạn 30 phút: phụ huynh tự thấy qua số liệu
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "bowl", true));
        Assert.True((await Dash(kid.Parent.Client, kid.ChildId)).Today.GoalMet);
    }

    [Fact]
    public async Task Recent_activity_is_newest_first_and_names_the_hunted_word()
    {
        var kid = await f.NewKidAsync();
        var k = kid.Device.Client;
        await G.PlayAsync(k, G.Match(4, startedAt: DateTime.UtcNow.AddMinutes(-30)));
        await G.PlayAsync(k, G.Hunt("cup", "model", startedAt: DateTime.UtcNow.AddMinutes(-10)));
        await G.PlayAsync(k, G.Match(2, 2, startedAt: DateTime.UtcNow.AddMinutes(-1)));
        var d = await Dash(kid.Parent.Client, kid.ChildId);
        Assert.Equal(["match", "hunt", "match"], d.Recent.Select(r => r.Kind));
        Assert.Equal((2, 4, 4), (d.Recent[0].Stars, d.Recent[0].Correct, d.Recent[0].Questions));
        Assert.Equal(("cup", 2), (d.Recent[1].WordId, d.Recent[1].Stars));
        Assert.False(string.IsNullOrEmpty(d.Recent[1].WordEn));
        Assert.Null(d.Recent[0].WordId);
        for (var i = 1; i < d.Recent.Count; i++) Assert.True(d.Recent[i - 1].At >= d.Recent[i].At);
    }

    [Fact]
    public async Task At_most_ten_recent_activities_are_returned()
    {
        var kid = await f.NewKidAsync();
        for (var i = 0; i < 12; i++) await G.PlayAsync(kid.Device.Client, G.Match(1, startedAt: DateTime.UtcNow.AddMinutes(-i)));
        Assert.Equal(10, (await Dash(kid.Parent.Client, kid.ChildId)).Recent.Count);
    }

    [Fact]
    public async Task Badges_show_the_latest_five_and_the_counts()
    {
        var kid = await f.NewKidAsync();
        await G.PlayAsync(kid.Device.Client, G.Match(8)); // first_round + perfect
        await G.PlayAsync(kid.Device.Client, G.Hunt("cup")); // finder_1, stars_10 (8 + 2)
        var d = await Dash(kid.Parent.Client, kid.ChildId);
        Assert.Equal(4, d.BadgesUnlocked);
        Assert.Equal(10, d.BadgesTotal);
        Assert.Equal(["finder_1", "first_round", "perfect", "stars_10"], d.RecentBadges.Select(b => b.Id).Order());
        Assert.All(d.RecentBadges, b => Assert.False(string.IsNullOrEmpty(b.Emoji)));
    }

    [Fact]
    public async Task Overview_lists_only_my_children_with_the_headline_numbers()
    {
        var a = await f.NewKidAsync("Bi");
        var second = await ApiFactory.CreateChildAsync(a.Parent, "Na");
        var b = await f.NewKidAsync("Khac");
        await G.PlayAsync(a.Device.Client, G.Match(5));
        await G.SendAsync(a.Device.Client, G.Ev("learned", "cup", true), G.Ev("screenTime", seconds: 90));
        await G.PlayAsync(b.Device.Client, G.Match(8));

        var mine = (await a.Parent.Client.GetFromJsonAsync<List<OverviewChildDto>>("/api/parent/overview"))!;
        Assert.Equal(["Bi", "Na"], mine.Select(c => c.Nickname));
        var bi = mine[0];
        Assert.Equal((5, 1, 1, 60), (bi.Stars, bi.LearnedWords, bi.CurrentStreak, bi.TotalWords));
        Assert.Equal(2, bi.MinutesToday); // 90 giây = 1,5 phút → 2
        Assert.Equal((1, 1), (bi.WordsLearnedToday, bi.ActiveDevices));
        Assert.Equal(Today, bi.LastActiveDate);
        var na = mine[1];
        Assert.Equal(second, na.Id);
        Assert.Equal((0, 0, 0, 0), (na.Stars, na.LearnedWords, na.CurrentStreak, na.ActiveDevices));
        Assert.Null(na.LastActiveDate);
        Assert.Empty((await (await f.RegisterParentAsync()).Client.GetFromJsonAsync<List<OverviewChildDto>>("/api/parent/overview"))!);
    }

    [Fact]
    public async Task A_streak_that_lapsed_shows_zero_in_the_overview_and_dashboard()
    {
        var kid = await f.NewKidAsync();
        await G.PlayAsync(kid.Device.Client, G.Match(2, startedAt: G.At(5)));
        await G.PlayAsync(kid.Device.Client, G.Match(2, startedAt: G.At(4)));
        Assert.Equal(0, (await kid.Parent.Client.GetFromJsonAsync<List<OverviewChildDto>>("/api/parent/overview"))!.Single(c => c.Id == kid.ChildId).CurrentStreak);
        var d = await Dash(kid.Parent.Client, kid.ChildId);
        Assert.Equal((0, 2), (d.Totals.CurrentStreak, d.Totals.BestStreak));
    }

    [Fact]
    public async Task Daily_csv_has_one_row_per_day_with_a_bom_and_download_headers()
    {
        var kid = await f.NewKidAsync();
        await G.SendAsync(kid.Device.Client, G.Ev("screenTime", seconds: 90), G.Ev("scan", "cup"));
        var r = await kid.Parent.Client.GetAsync($"/api/parent/children/{kid.ChildId}/export.csv?days=7");
        Assert.Equal("text/csv", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", r.Content.Headers.ContentType.CharSet);
        Assert.StartsWith("english-cam-daily-", r.Content.Headers.ContentDisposition!.FileName);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).TrimEnd().Split("\r\n");
        Assert.Equal("date,minutes,seconds,words_learned,rounds,scans", lines[0]);
        Assert.Equal(8, lines.Length);
        Assert.Equal($"{Today:yyyy-MM-dd},1.5,90,0,0,1", lines[^1]);
        Assert.StartsWith($"{Today.AddDays(-6):yyyy-MM-dd},0,0,", lines[1]);
    }

    [Fact]
    public async Task Words_csv_lists_every_published_word_with_the_childs_status()
    {
        var kid = await f.NewKidAsync();
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "cup", true));
        await G.PlayAsync(kid.Device.Client, new { id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 5,
            answers = new[] { G.Answer("cup", true, 2) } });
        var text = Encoding.UTF8.GetString((await kid.Parent.Client.GetByteArrayAsync($"/api/parent/children/{kid.ChildId}/export.csv?kind=words"))[3..]);
        var lines = text.TrimEnd().Split("\r\n");
        Assert.Equal("word_id,english,vietnamese,category,learned,times_seen,correct,wrong,box,last_seen_utc", lines[0]);
        Assert.Equal(61, lines.Length);
        var cup = lines.Single(l => l.StartsWith("cup,"));
        Assert.Contains(",yes,1,0,1,1,", cup); // học rồi, gặp 1 lần, sai 1 lần, hộp 1
        Assert.Contains(",no,0,0,0,1,", lines.Single(l => l.StartsWith("bag,")));
        Assert.Equal(HttpStatusCode.BadRequest, (await kid.Parent.Client.GetAsync($"/api/parent/children/{kid.ChildId}/export.csv?kind=secrets")).StatusCode);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("bình thường", "bình thường")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("nói \"xin chào\"", "\"nói \"\"xin chào\"\"\"")]
    [InlineData("hai\ndòng", "\"hai\ndòng\"")]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void Csv_cells_are_quoted_and_cannot_become_spreadsheet_formulas(string? input, string expected) =>
        Assert.Equal(expected, DashboardService.Cell(input));
}

/// <summary>Sửa từ vựng toàn cục (xoá/đổi tên rồi xuất bản) nên dùng DB riêng.</summary>
public class DashboardVocabTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Progress_is_counted_against_the_published_words_only()
    {
        var admin = await f.LoginAdminAsync();
        var kid = await f.NewKidAsync();
        await G.SendAsync(kid.Device.Client, G.Ev("learned", "cup", true), G.Ev("learned", "bowl", true));
        var before = (await kid.Parent.Client.GetFromJsonAsync<ChildDashboardDto>($"/api/parent/children/{kid.ChildId}/dashboard"))!;
        Assert.Equal((2, 60), (before.Totals.LearnedWords, before.Totals.TotalWords));

        // Xoá "cup" khỏi từ vựng: chưa xuất bản thì vẫn tính như cũ (app của bé vẫn có từ đó).
        await admin.Client.DeleteAsync("/api/admin/words/cup");
        var draft = (await kid.Parent.Client.GetFromJsonAsync<ChildDashboardDto>($"/api/parent/children/{kid.ChildId}/dashboard"))!;
        Assert.Equal((2, 60), (draft.Totals.LearnedWords, draft.Totals.TotalWords));

        (await admin.Client.PostAsJsonAsync("/api/admin/vocab/publish", new { notes = "bỏ cup" })).EnsureSuccessStatusCode();
        var after = (await kid.Parent.Client.GetFromJsonAsync<ChildDashboardDto>($"/api/parent/children/{kid.ChildId}/dashboard"))!;
        Assert.Equal((1, 59), (after.Totals.LearnedWords, after.Totals.TotalWords)); // tử số không bao giờ vượt mẫu số
        Assert.True(after.VocabVersion > before.VocabVersion);
        Assert.Equal(59, after.Categories.Sum(c => c.Total));
    }

    [Fact]
    public async Task Words_csv_neutralises_formulas_typed_by_an_admin()
    {
        var admin = await f.LoginAdminAsync();
        var kid = await f.NewKidAsync();
        var body = new
        {
            en = "=HYPERLINK(\"http://evil\",\"x\")", vi = "một, hai", phonetic = "/b/", exampleEn = "A.", exampleVi = "B.", category = "school",
            audio = "assets/audio/words/book.mp3", emoji = "📖", label = (string?)null
        };
        (await admin.Client.PutAsJsonAsync("/api/admin/words/book", body)).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/admin/vocab/publish", new { })).EnsureSuccessStatusCode();
        var text = Encoding.UTF8.GetString((await kid.Parent.Client.GetByteArrayAsync($"/api/parent/children/{kid.ChildId}/export.csv?kind=words"))[3..]);
        var row = text.Split("\r\n").Single(l => l.StartsWith("book,"));
        Assert.StartsWith("book,\"'=HYPERLINK(", row);
        Assert.Contains("\"một, hai\"", row);
    }
}
