using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnglishCam.Api.Data;
using EnglishCam.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Services;

/// <summary>
/// Kiểm tra thêm với DB sau khi chữ ký JWT hợp lệ: tài khoản còn tồn tại và đang hoạt động, SecurityStamp còn khớp
/// (đổi mật khẩu hoặc đổi role = đăng xuất mọi máy), token chưa bị đăng xuất. Token của bé còn phải gắn với một phiên
/// thiết bị chưa bị thu hồi và hồ sơ bé còn bật. Nhờ đó khoá tài khoản, đăng xuất, thu hồi thiết bị có hiệu lực ngay.
/// </summary>
public static class TokenValidation
{
    private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(5);

    public static async Task OnTokenValidated(TokenValidatedContext ctx)
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var p = ctx.Principal!;
        var userId = p.FindFirstValue(ClaimTypes.NameIdentifier);
        var jti = p.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var stamp = p.FindFirstValue(JwtService.StampClaim);

        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.SecurityStamp }).FirstOrDefaultAsync();
        if (user is null || !user.IsActive || user.SecurityStamp != stamp ||
            (jti is not null && await db.RevokedTokens.AnyAsync(r => r.Jti == jti)))
        {
            ctx.Fail("Phiên đăng nhập không còn hiệu lực");
            return;
        }

        if (!p.IsInRole(Roles.Child)) return;

        var now = DateTime.UtcNow;
        if (!Guid.TryParse(p.FindFirstValue(JwtService.ChildSessionClaim), out var sid))
        {
            ctx.Fail("Thiếu phiên thiết bị");
            return;
        }
        var session = await db.ChildSessions.AsNoTracking()
            .Where(s => s.Id == sid && !s.Revoked && s.ExpiresAt > now && s.Child!.IsActive && s.Child.UserId == userId)
            .Select(s => new { s.LastUsedAt }).FirstOrDefaultAsync();
        if (session is null)
        {
            ctx.Fail("Thiết bị này đã bị thu hồi");
            return;
        }
        // Ghi "lần dùng cuối" thưa thớt để không thêm một lệnh UPDATE cho mỗi request.
        if (session.LastUsedAt is null || now - session.LastUsedAt > TouchEvery)
            await db.ChildSessions.Where(s => s.Id == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, now));
    }
}
