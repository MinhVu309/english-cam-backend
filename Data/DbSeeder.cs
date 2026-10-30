using EnglishCam.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace EnglishCam.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, IConfiguration cfg)
    {
        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        foreach (var r in Roles.All)
            if (!await roleMgr.RoleExistsAsync(r)) await roleMgr.CreateAsync(new IdentityRole(r));

        var email = cfg["Seed:AdminEmail"] ?? "admin@englishcam.local";
        var password = cfg["Seed:AdminPassword"] ?? "Admin@123456";
        var admin = await userMgr.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Quản trị viên" };
            var res = await userMgr.CreateAsync(admin, password);
            if (res.Succeeded) await userMgr.AddToRoleAsync(admin, Roles.Admin);
            else log.LogError("Không tạo được Admin: {Errors}", string.Join("; ", res.Errors.Select(e => e.Description)));
        }
        else if (cfg.GetValue<bool>("Seed:SyncAdminPassword") && !await userMgr.CheckPasswordAsync(admin, password))
        {
            // Seeder chỉ tạo Admin khi chưa có, nên đổi Seed:AdminPassword một mình không đổi mật khẩu đã nằm trong DB.
            var reset = await userMgr.ResetPasswordAsync(admin, await userMgr.GeneratePasswordResetTokenAsync(admin), password);
            if (reset.Succeeded) log.LogWarning("Đã đổi mật khẩu Admin {Email} theo Seed:AdminPassword.", email);
            else log.LogError("Không đổi được mật khẩu Admin: {Errors}", string.Join("; ", reset.Errors.Select(e => e.Description)));
        }
    }
}
