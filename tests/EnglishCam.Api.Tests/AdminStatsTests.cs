using System.Net;
using System.Net.Http.Json;
using EnglishCam.Api.Dtos;
using Xunit;

namespace EnglishCam.Api.Tests;

public class AdminStatsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static Task<AdminStatsDto> Stats(Session admin) => admin.Client.GetFromJsonAsync<AdminStatsDto>("/api/admin/stats")!;

    [Fact]
    public async Task Only_admin_can_read_system_stats()
    {
        var admin = await f.LoginAdminAsync();
        var kid = await f.NewKidAsync();
        const string url = "/api/admin/stats";
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kid.Parent.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kid.Device.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Counts_follow_what_happens_in_the_system()
    {
        var admin = await f.LoginAdminAsync();
        var before = await Stats(admin);

        var kid = await f.NewKidAsync("Na");
        await f.AddDeviceAsync(kid); // máy thứ hai của cùng một bé
        await G.PlayAsync(kid.Device.Client, G.Match(firstTry: 4));
        await G.SendAsync(kid.Device.Client, G.Ev("screenTime", seconds: 600));

        var after = await Stats(admin);
        Assert.Equal(before.Users.Parents + 1, after.Users.Parents);
        Assert.Equal(before.Users.Children + 1, after.Users.Children);
        Assert.Equal(before.Users.Admins, after.Users.Admins);
        Assert.Equal(before.Users.NewParentsLast7Days + 1, after.Users.NewParentsLast7Days);
        Assert.Equal(before.Children.Total + 1, after.Children.Total);
        Assert.Equal(before.Children.Active + 1, after.Children.Active);
        Assert.Equal(before.Children.ActiveToday + 1, after.Children.ActiveToday);
        Assert.Equal(before.Children.ActiveLast7Days + 1, after.Children.ActiveLast7Days);
        Assert.Equal(before.Children.LinkedDevices + 2, after.Children.LinkedDevices);
        Assert.Equal(before.Activity.SessionsTotal + 1, after.Activity.SessionsTotal);
        Assert.Equal(before.Activity.SessionsLast7Days + 1, after.Activity.SessionsLast7Days);
        Assert.Equal(before.Activity.MinutesLast7Days + 10, after.Activity.MinutesLast7Days);
        Assert.Equal(before.Activity.StarsTotal + 4, after.Activity.StarsTotal);
        Assert.Equal(1, after.Vocab.PublishedVersion);
        Assert.Equal(60, after.Vocab.Words);
        Assert.Equal(11, after.Vocab.Categories);
    }

    [Fact]
    public async Task Locked_accounts_are_counted_and_revoked_devices_are_not()
    {
        var admin = await f.LoginAdminAsync();
        var before = await Stats(admin);

        var kid = await f.NewKidAsync();
        var parentId = kid.Parent.Auth.UserId;
        var put = await admin.Client.PutAsJsonAsync($"/api/admin/users/{parentId}",
            new { fullName = "Phu Huynh", role = "Parent", isActive = false });
        put.EnsureSuccessStatusCode();
        Assert.Equal(before.Users.Locked + 1, (await Stats(admin)).Users.Locked);

        var other = await f.NewKidAsync();
        var devicesBefore = (await Stats(admin)).Children.LinkedDevices;
        (await other.Parent.Client.PostAsync($"/api/children/{other.ChildId}/revoke-sessions", null)).EnsureSuccessStatusCode();
        Assert.Equal(devicesBefore - 1, (await Stats(admin)).Children.LinkedDevices);
    }
}

/// <summary>Bảng từ khó cần cơ sở dữ liệu riêng: nó cộng dồn mọi bé nên không so theo mức tăng được.</summary>
public class AdminStatsHardWordsTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    /// <summary>Một ván có đúng các đáp án này: mỗi mục là (từ, số lần chọn ở câu đó). Attempts = 1 là đúng ngay lần đầu.</summary>
    private static object Round(params (string Word, int Attempts)[] a) => new
    {
        id = Guid.NewGuid(), gameType = "match", startedAt = DateTime.UtcNow, durationSec = 30,
        answers = a.Select(x => G.Answer(x.Word, true, x.Attempts)).ToArray(),
    };

    [Fact]
    public async Task Hardest_words_rank_by_wrong_rate_across_all_children_and_ignore_rare_words()
    {
        var admin = await f.LoginAdminAsync();
        var a = await f.NewKidAsync("A");
        var b = await f.NewKidAsync("B");

        // cup: bé A 2 lần sai + 1 đúng, bé B 1 sai + 1 đúng → 3 sai / 5 = 0,6 (2 bé)
        await G.PlayAsync(a.Device.Client, Round(("cup", 2), ("cup", 3), ("cup", 1)));
        await G.PlayAsync(b.Device.Client, Round(("cup", 2), ("cup", 1)));
        // book: 1 sai / 5 = 0,2
        await G.PlayAsync(a.Device.Client, Round(("book", 2), ("book", 1), ("book", 1), ("book", 1), ("book", 1)));
        // pen: 2 sai / 2 = 1,0 nhưng chỉ 2 lượt → chưa đủ để xét
        await G.PlayAsync(b.Device.Client, Round(("pen", 2), ("pen", 2)));
        // chair: toàn đúng → không có sai thì không vào bảng
        await G.PlayAsync(b.Device.Client, Round(("chair", 1), ("chair", 1), ("chair", 1), ("chair", 1), ("chair", 1)));

        var hard = (await admin.Client.GetFromJsonAsync<AdminStatsDto>("/api/admin/stats"))!.HardestWords;
        Assert.Equal(["cup", "book"], hard.Select(h => h.WordId));
        var cup = hard[0];
        Assert.Equal(("cup", "cup", 5, 3, 0.6, 2), (cup.WordId, cup.En, cup.Answers, cup.Wrong, cup.WrongRate, cup.Children));
        Assert.False(string.IsNullOrEmpty(cup.Vi));
        Assert.Equal((5, 1, 0.2, 1), (hard[1].Answers, hard[1].Wrong, hard[1].WrongRate, hard[1].Children));
    }

    [Fact]
    public async Task Hardest_words_list_is_empty_when_nobody_has_played()
    {
        var other = new ApiFactory();
        await using var _ = other as IAsyncDisposable;
        var admin = await other.LoginAdminAsync();
        Assert.Empty((await admin.Client.GetFromJsonAsync<AdminStatsDto>("/api/admin/stats"))!.HardestWords);
    }
}
