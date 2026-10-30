using System.Net;
using System.Net.Http.Json;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishCam.Api.Tests;

public class DeviceTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static async Task<List<ChildSessionDto>> SessionsOf(Session parent, Guid id) =>
        (await parent.Client.GetFromJsonAsync<List<ChildSessionDto>>($"/api/children/{id}/sessions"))!;

    [Fact]
    public async Task One_child_can_be_signed_in_on_several_devices_and_each_can_be_revoked_alone()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var phoneA = await f.LinkDeviceAsync(p, id, "dien-thoai-bo");
        var phoneB = await f.LinkDeviceAsync(p, id, "dien-thoai-me");
        var tablet = await f.LinkDeviceAsync(p, id, "may-tinh-bang");
        foreach (var d in new[] { phoneA, phoneB, tablet })
            Assert.Equal(HttpStatusCode.OK, (await d.Client.GetAsync("/api/child/me")).StatusCode);

        var sessions = await SessionsOf(p, id);
        Assert.Equal(3, sessions.Count);
        Assert.Equal(3, (await p.Client.GetFromJsonAsync<ChildDto>($"/api/children/{id}"))!.ActiveDevices);

        var phoneBSession = sessions.Single(s => s.DeviceId == "dien-thoai-me");
        Assert.Equal(HttpStatusCode.NoContent,
            (await p.Client.DeleteAsync($"/api/children/{id}/sessions/{phoneBSession.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await phoneB.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phoneA.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tablet.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(2, (await SessionsOf(p, id)).Count);
    }

    [Fact]
    public async Task Revoke_all_signs_out_every_device()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var a = await f.LinkDeviceAsync(p, id, "a");
        var b = await f.LinkDeviceAsync(p, id, "b");
        await p.Client.PostAsync($"/api/children/{id}/revoke-sessions", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Empty(await SessionsOf(p, id));
    }

    [Fact]
    public async Task A_link_code_works_once()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var code = (await (await p.Client.PostAsync($"/api/children/{id}/link-code", null)).Content.ReadFromJsonAsync<LinkCodeDto>())!;
        Assert.Equal(8, code.Code.Length);
        var first = await f.Anonymous().PostAsJsonAsync("/api/auth/child-link", new { code = code.Code, deviceId = "may-1" });
        var again = await f.Anonymous().PostAsJsonAsync("/api/auth/child-link", new { code = code.Code, deviceId = "may-2" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task Wrong_malformed_and_expired_codes_are_rejected()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        foreach (var bad in new[] { "00000000", "123", "abcdefgh", "123456789012" })
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await f.Anonymous().PostAsJsonAsync("/api/auth/child-link", new { code = bad, deviceId = "x" })).StatusCode);

        var code = (await (await p.Client.PostAsync($"/api/children/{id}/link-code", null)).Content.ReadFromJsonAsync<LinkCodeDto>())!.Code;
        await f.WithDbAsync(async db => await db.ChildLinkCodes.Where(l => l.ChildId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await f.Anonymous().PostAsJsonAsync("/api/auth/child-link", new { code, deviceId = "x" })).StatusCode);
    }

    [Fact]
    public async Task Creating_a_new_code_cancels_the_previous_unused_one()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var old = (await (await p.Client.PostAsync($"/api/children/{id}/link-code", null)).Content.ReadFromJsonAsync<LinkCodeDto>())!.Code;
        await p.Client.PostAsync($"/api/children/{id}/link-code", null);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await f.Anonymous().PostAsJsonAsync("/api/auth/child-link", new { code = old, deviceId = "x" })).StatusCode);
    }

    [Fact]
    public async Task Signing_in_again_on_the_same_device_replaces_its_session()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var before = await f.LinkDeviceAsync(p, id, "may-be");
        var after = await f.LinkDeviceAsync(p, id, "may-be");
        Assert.Equal(HttpStatusCode.Unauthorized, (await before.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await after.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Single(await SessionsOf(p, id));
    }

    [Fact]
    public async Task Child_logout_only_ends_that_device()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var a = await f.LinkDeviceAsync(p, id, "a");
        var b = await f.LinkDeviceAsync(p, id, "b");
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Single(await SessionsOf(p, id));
    }

    [Fact]
    public async Task Parent_can_start_a_session_on_their_own_phone()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var resp = await p.Client.PostAsJsonAsync($"/api/children/{id}/session", new { deviceId = "may-phu-huynh", deviceName = "Máy mẹ" });
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResultDto>())!;
        Assert.Equal("Child", auth.Role);
        Assert.Equal(HttpStatusCode.OK, (await f.WithToken(auth.Token).GetAsync("/api/child/me")).StatusCode);
        Assert.Equal("Máy mẹ", (await SessionsOf(p, id)).Single().DeviceName);
    }

    [Fact]
    public async Task A_child_can_have_at_most_ten_devices()
    {
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        for (var i = 0; i < ChildServiceLimit; i++) await f.LinkDeviceAsync(p, id, $"may-{i}");
        var code = (await (await p.Client.PostAsync($"/api/children/{id}/link-code", null)).Content.ReadFromJsonAsync<LinkCodeDto>())!.Code;
        var eleventh = await f.Anonymous().PostAsJsonAsync("/api/auth/child-link", new { code, deviceId = "may-11" });
        Assert.Equal(HttpStatusCode.Unauthorized, eleventh.StatusCode); // mã đã dùng nhưng không cấp phiên
        Assert.Equal(ChildServiceLimit, (await SessionsOf(p, id)).Count);
    }

    private const int ChildServiceLimit = EnglishCam.Api.Services.ChildService.MaxDevicesPerChild;
}

public class ChildLinkRateLimitTests(RateLimitedApiFactory f) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Guessing_codes_is_throttled_per_client()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await f.Anonymous().PostAsJsonAsync("/api/auth/child-link", new { code = "00000000", deviceId = "x" })).StatusCode);
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
