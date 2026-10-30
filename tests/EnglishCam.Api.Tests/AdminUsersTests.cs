using System.Net;
using System.Net.Http.Json;
using EnglishCam.Api.Dtos;
using Xunit;

namespace EnglishCam.Api.Tests;

public class AdminUsersTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Only_admins_can_use_the_user_endpoints()
    {
        var p = await f.RegisterParentAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Client.DeleteAsync($"/api/admin/users/{p.Auth.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Client.PostAsJsonAsync("/api/admin/users",
            new { email = ApiFactory.NewEmail(), password = ApiFactory.Password, fullName = "X Y", role = "Admin" })).StatusCode);
    }

    [Fact]
    public async Task List_is_paged_searchable_and_hides_child_accounts_by_default()
    {
        var admin = await f.LoginAdminAsync();
        var p = await f.RegisterParentAsync(fullName: "Tim Thay Toi");
        await ApiFactory.CreateChildAsync(p, "BeNho");

        var found = await admin.Client.GetFromJsonAsync<PagedResult<AdminUserDto>>("/api/admin/users?search=Tim%20Thay%20Toi");
        var row = Assert.Single(found!.Items);
        Assert.Equal("Parent", row.Role);
        Assert.Equal(1, row.ChildCount);

        var all = await admin.Client.GetFromJsonAsync<PagedResult<AdminUserDto>>("/api/admin/users?pageSize=100");
        Assert.DoesNotContain(all!.Items, u => u.Role == "Child");

        var kids = await admin.Client.GetFromJsonAsync<PagedResult<AdminUserDto>>("/api/admin/users?role=Child&pageSize=100");
        Assert.All(kids!.Items, u => Assert.Equal("Child", u.Role));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.GetAsync("/api/admin/users?role=Hacker")).StatusCode);
    }

    [Fact]
    public async Task Create_only_allows_Admin_or_Parent()
    {
        var admin = await f.LoginAdminAsync();
        var bad = await admin.Client.PostAsJsonAsync("/api/admin/users",
            new { email = ApiFactory.NewEmail(), password = ApiFactory.Password, fullName = "Be Con", role = "Child" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var email = ApiFactory.NewEmail();
        var ok = await admin.Client.PostAsJsonAsync("/api/admin/users",
            new { email, password = ApiFactory.Password, fullName = "Phu Huynh Moi", role = "Parent" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal("Parent", (await f.LoginAsync(email)).Auth.Role); // đã xác minh email sẵn, đăng nhập được
    }

    [Fact]
    public async Task Changing_a_role_forces_that_user_to_sign_in_again()
    {
        var admin = await f.LoginAdminAsync();
        var p = await f.RegisterParentAsync();
        var put = await admin.Client.PutAsJsonAsync($"/api/admin/users/{p.Auth.UserId}",
            new { fullName = "Da Len Admin", role = "Admin", isActive = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal("Admin", (await f.LoginAsync(p.Email)).Auth.Role);
    }

    [Fact]
    public async Task Locking_a_user_takes_effect_immediately_and_blocks_login()
    {
        var admin = await f.LoginAdminAsync();
        var p = await f.RegisterParentAsync();
        await admin.Client.PutAsJsonAsync($"/api/admin/users/{p.Auth.UserId}", new { fullName = "Bi Khoa", role = "Parent", isActive = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Client.GetAsync("/api/auth/me")).StatusCode);
        var login = await f.Anonymous().PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = p.Email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_demote_lock_or_delete_themself()
    {
        var admin = await f.LoginAdminAsync();
        var self = admin.Auth.UserId;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PutAsJsonAsync($"/api/admin/users/{self}",
            new { fullName = "Quan tri vien", role = "Parent", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PutAsJsonAsync($"/api/admin/users/{self}",
            new { fullName = "Quan tri vien", role = "Admin", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.DeleteAsync($"/api/admin/users/{self}")).StatusCode);
    }

    [Fact]
    public async Task Child_accounts_cannot_be_edited_or_deleted_here()
    {
        var admin = await f.LoginAdminAsync();
        var p = await f.RegisterParentAsync();
        var kid = await f.LinkDeviceAsync(p, await ApiFactory.CreateChildAsync(p), "may-be");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PutAsJsonAsync($"/api/admin/users/{kid.Auth.UserId}",
            new { fullName = "Doi Ten", role = "Admin", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.DeleteAsync($"/api/admin/users/{kid.Auth.UserId}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_parent_also_deletes_their_children_and_ends_their_devices()
    {
        var admin = await f.LoginAdminAsync();
        var p = await f.RegisterParentAsync();
        var id = await ApiFactory.CreateChildAsync(p);
        var kid = await f.LinkDeviceAsync(p, id, "may-be");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/admin/users/{p.Auth.UserId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await kid.Client.GetAsync("/api/child/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.GetAsync($"/api/children/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.GetAsync($"/api/admin/users/{p.Auth.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.GetAsync($"/api/admin/users/{kid.Auth.UserId}")).StatusCode);
    }
}
