using System.Net;
using System.Net.Http.Json;
using EnglishCam.Api.Dtos;
using Xunit;

namespace EnglishCam.Api.Tests;

/// <summary>Sửa danh sách huy hiệu toàn cục nên dùng DB riêng (không chung với các test game).</summary>
public class BadgeAdminTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Only_admins_manage_badges_and_the_seed_has_ten()
    {
        var parent = await f.RegisterParentAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.Client.GetAsync("/api/admin/badges")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync("/api/admin/badges")).StatusCode);
        var admin = await f.LoginAdminAsync();
        var all = (await admin.Client.GetFromJsonAsync<List<BadgeDto>>("/api/admin/badges"))!;
        Assert.Equal(10, all.Count);
        Assert.Contains(all, b => b.Id == "streak_7" && b.Metric == "streak" && b.Threshold == 7);
    }

    [Fact]
    public async Task Badge_crud_with_validation()
    {
        var admin = await f.LoginAdminAsync();
        var c = admin.Client;
        var body = new { id = "tuyet_voi", name = "Tuyệt vời", description = "Chơi 2 ván", emoji = "🥇", metric = "rounds", threshold = 2 };
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/admin/badges", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/admin/badges", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/admin/badges", new { id = "x1", name = "X", description = "d", emoji = "e", metric = "magic", threshold = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/admin/badges", new { id = "x2", name = "X", description = "d", emoji = "e", metric = "stars", threshold = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/admin/badges", new { id = "Bad Id", name = "X", description = "d", emoji = "e", metric = "stars", threshold = 1 })).StatusCode);

        var upd = await c.PutAsJsonAsync("/api/admin/badges/tuyet_voi", new { name = "Tuyệt vời!", description = "Chơi 3 ván", emoji = "🥇", metric = "rounds", threshold = 3 });
        Assert.Equal(3, (await upd.Content.ReadFromJsonAsync<BadgeDto>())!.Threshold);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/admin/badges/tuyet_voi", new { id = "khac", name = "N", description = "d", emoji = "e", metric = "stars", threshold = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/admin/badges/khong_co", new { name = "N", description = "d", emoji = "e", metric = "stars", threshold = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/admin/badges/tuyet_voi")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/admin/badges/tuyet_voi")).StatusCode);
    }

    [Fact]
    public async Task A_new_badge_is_awarded_to_children_who_reach_it_and_deleting_it_hides_it()
    {
        var admin = await f.LoginAdminAsync();
        await admin.Client.PostAsJsonAsync("/api/admin/badges", new { id = "hai_van", name = "Hai ván", description = "Chơi 2 ván", emoji = "✌️", metric = "rounds", threshold = 2 });
        var kid = await f.NewKidAsync();
        var one = await G.PlayAsync(kid.Device.Client, G.Match(3));
        Assert.DoesNotContain("hai_van", one.Progress.BadgeIds);
        var two = await G.PlayAsync(kid.Device.Client, G.Match(3));
        Assert.Contains("hai_van", two.NewBadges.Select(b => b.Id));

        var list = (await kid.Device.Client.GetFromJsonAsync<List<ChildBadgeDto>>("/api/games/badges"))!;
        Assert.True(list.Single(b => b.Id == "hai_van").Unlocked);

        await admin.Client.DeleteAsync("/api/admin/badges/hai_van");
        var after = (await kid.Device.Client.GetFromJsonAsync<List<ChildBadgeDto>>("/api/games/badges"))!;
        Assert.DoesNotContain(after, b => b.Id == "hai_van");
    }
}
