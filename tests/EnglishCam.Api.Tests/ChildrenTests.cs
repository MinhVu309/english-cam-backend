using System.Net;
using System.Net.Http.Json;
using EnglishCam.Api.Dtos;
using Xunit;

namespace EnglishCam.Api.Tests;

public class ChildrenTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Parent_cannot_see_or_change_another_parents_child()
    {
        var a = await f.RegisterParentAsync();
        var b = await f.RegisterParentAsync();
        var childId = await ApiFactory.CreateChildAsync(a, "Bi");
        var fakeSession = Guid.NewGuid();

        var attempts = new Dictionary<string, HttpResponseMessage>
        {
            ["GET"] = await b.Client.GetAsync($"/api/children/{childId}"),
            ["PUT"] = await b.Client.PutAsJsonAsync($"/api/children/{childId}", new { nickname = "Hacked", isActive = false }),
            ["settings"] = await b.Client.PutAsJsonAsync($"/api/children/{childId}/settings", new { dailyLimitMinutes = 5, dailyWordGoal = 1 }),
            ["DELETE"] = await b.Client.DeleteAsync($"/api/children/{childId}"),
            ["session"] = await b.Client.PostAsJsonAsync($"/api/children/{childId}/session", new { deviceId = "evil" }),
            ["link-code"] = await b.Client.PostAsync($"/api/children/{childId}/link-code", null),
            ["sessions"] = await b.Client.GetAsync($"/api/children/{childId}/sessions"),
            ["revoke one"] = await b.Client.DeleteAsync($"/api/children/{childId}/sessions/{fakeSession}"),
            ["revoke all"] = await b.Client.PostAsync($"/api/children/{childId}/revoke-sessions", null),
        };
        foreach (var (name, resp) in attempts)
            Assert.True(resp.StatusCode == HttpStatusCode.NotFound, $"{name} trả {(int)resp.StatusCode}, mong đợi 404");

        // Dữ liệu của A còn nguyên và B không thấy bé nào.
        var mine = await a.Client.GetFromJsonAsync<ChildDto>($"/api/children/{childId}");
        Assert.Equal("Bi", mine!.Nickname);
        Assert.True(mine.IsActive);
        Assert.Empty((await b.Client.GetFromJsonAsync<List<ChildDto>>("/api/children"))!);
    }

    [Fact]
    public async Task Admin_can_read_and_edit_any_child()
    {
        var p = await f.RegisterParentAsync();
        var childId = await ApiFactory.CreateChildAsync(p, "Na");
        var admin = await f.LoginAdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync($"/api/children/{childId}")).StatusCode);
        var put = await admin.Client.PutAsJsonAsync($"/api/children/{childId}", new { nickname = "Na2", isActive = true });
        Assert.Equal("Na2", (await put.Content.ReadFromJsonAsync<ChildDto>())!.Nickname);
    }

    [Fact]
    public async Task Create_validates_input_and_caps_children_per_parent()
    {
        var p = await f.RegisterParentAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await p.Client.PostAsJsonAsync("/api/children", new { nickname = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await p.Client.PostAsJsonAsync("/api/children", new { nickname = "A", birthYear = 1850 })).StatusCode);
        for (var i = 0; i < 10; i++) await ApiFactory.CreateChildAsync(p, $"Be{i}");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await p.Client.PostAsJsonAsync("/api/children", new { nickname = "Thu 11" })).StatusCode);
    }

    [Fact]
    public async Task Settings_are_validated_and_visible_to_the_child()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await p.Client.PutAsJsonAsync($"/api/children/{id}/settings", new { dailyLimitMinutes = 1, dailyWordGoal = 5 })).StatusCode);
        (await p.Client.PutAsJsonAsync($"/api/children/{id}/settings", new { dailyLimitMinutes = 45, dailyWordGoal = 8 })).EnsureSuccessStatusCode();

        var kid = await f.LinkDeviceAsync(p, id, "may-be");
        var me = await kid.Client.GetFromJsonAsync<ChildDto>("/api/child/me");
        Assert.Equal(45, me!.DailyLimitMinutes);
        Assert.Equal(8, me.DailyWordGoal);
    }

    [Fact]
    public async Task A_child_token_cannot_reach_parent_or_admin_endpoints()
    {
        var p = await f.RegisterParentAsync();
        var kid = await f.LinkDeviceAsync(p, await ApiFactory.CreateChildAsync(p), "may-be");
        Assert.Equal("Child", kid.Auth.Role);
        foreach (var url in new[] { "/api/children", "/api/admin/users" })
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await kid.Client.PutAsJsonAsync("/api/auth/me", new { fullName = "Doi Ten" })).StatusCode);
        // Phụ huynh thì không vào được API riêng của bé.
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Client.GetAsync("/api/child/me")).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_child_kills_the_token_immediately_and_enabling_restores_it()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var kid = await f.LinkDeviceAsync(p, id, "may-be");
        await p.Client.PutAsJsonAsync($"/api/children/{id}", new { nickname = "Bi", isActive = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await kid.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await p.Client.PostAsync($"/api/children/{id}/link-code", null)).StatusCode);
        await p.Client.PutAsJsonAsync($"/api/children/{id}", new { nickname = "Bi", isActive = true });
        Assert.Equal(HttpStatusCode.OK, (await kid.Client.GetAsync("/api/child/me")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_child_kills_its_tokens()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var kid = await f.LinkDeviceAsync(p, id, "may-be");
        Assert.Equal(HttpStatusCode.NoContent, (await p.Client.DeleteAsync($"/api/children/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kid.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await p.Client.GetAsync($"/api/children/{id}")).StatusCode);
    }

    [Fact]
    public async Task Child_accounts_cannot_use_the_email_flows()
    {
        var p = await f.RegisterParentAsync();
        var kid = await f.LinkDeviceAsync(p, await ApiFactory.CreateChildAsync(p), "may-be");
        var anon = f.Anonymous();
        var userName = (await kid.Client.GetFromJsonAsync<ProfileDto>("/api/auth/me"))!;
        Assert.Equal("", userName.Email); // email giả không lộ ra ngoài

        // Email giả của bé có dạng cố định; thử đăng ký trùng miền đó.
        var fake = await anon.PostAsJsonAsync("/api/auth/register",
            new { email = "child-x@child.englishcam.local", password = ApiFactory.Password, fullName = "Gia Mao" });
        Assert.Equal(HttpStatusCode.BadRequest, fake.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_requests_get_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync("/api/children")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync("/api/admin/users")).StatusCode);
    }
}
