using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishCam.Api.Tests;

public class AuthTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Register_always_creates_a_Parent_and_ignores_a_requested_role()
    {
        var email = ApiFactory.NewEmail();
        var anon = f.Anonymous();
        (await anon.PostAsJsonAsync("/api/auth/register",
            new { email, password = ApiFactory.Password, fullName = "Ke Gian", role = "Admin" })).EnsureSuccessStatusCode();
        var s = await f.LoginAsync(email);
        Assert.Equal("Parent", s.Auth.Role);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Wrong_otp_is_rejected_and_unknown_user_gets_the_same_answer()
    {
        var email = ApiFactory.NewEmail();
        await f.Anonymous().PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.Password, fullName = "A B" });
        var wrong = await f.Anonymous().PostAsJsonAsync("/api/auth/verify-otp", new { userNameOrEmail = email, code = "000000" });
        var nobody = await f.Anonymous().PostAsJsonAsync("/api/auth/verify-otp", new { userNameOrEmail = "nobody@test.com", code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nobody.StatusCode);
    }

    [Fact]
    public async Task Logout_kills_only_that_token()
    {
        var p = await f.RegisterParentAsync();
        var second = await f.LoginAsync(p.Email); // máy thứ hai của cùng phụ huynh
        Assert.Equal(HttpStatusCode.NoContent, (await p.Client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.Client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_invalidates_every_old_token()
    {
        var p = await f.RegisterParentAsync();
        await f.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email = p.Email });
        var code = f.Mail.LastCode(p.Email);
        var reset = await f.Anonymous().PostAsJsonAsync("/api/auth/reset-password",
            new { email = p.Email, code, newPassword = "Moimatkhau9" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_a_while()
    {
        var p = await f.RegisterParentAsync();
        var anon = f.Anonymous();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anon.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = p.Email, password = "sai" })).StatusCode);
        Assert.Equal((HttpStatusCode)423,
            (await anon.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = p.Email, password = ApiFactory.Password })).StatusCode);
    }

    [Fact]
    public async Task Google_login_is_unavailable_until_a_client_id_is_configured()
    {
        var r = await f.Anonymous().PostAsJsonAsync("/api/auth/google-login", new { idToken = "abc" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
    }

    [Fact]
    public async Task Seeded_admin_can_sign_in()
    {
        var admin = await f.LoginAdminAsync();
        Assert.Equal("Admin", admin.Auth.Role);
    }

    [Fact]
    public async Task Database_used_by_tests_is_the_throwaway_one()
    {
        await f.WithDbAsync(db =>
        {
            Assert.StartsWith("EnglishCamTest_", db.Database.GetDbConnection().Database);
            return Task.CompletedTask;
        });
    }
}
