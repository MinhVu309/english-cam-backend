using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<AppUser> users, OtpService otp, JwtService jwt, ChildService children, AppDbContext db, IConfiguration cfg) : ControllerBase
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Tài khoản của bé không có mật khẩu/email thật nên không bao giờ đi qua các luồng email: coi như không tồn tại.</summary>
    private async Task<AppUser?> ExcludeChild(AppUser? u) =>
        u is not null && await users.IsInRoleAsync(u, Roles.Child) ? null : u;

    private async Task<AppUser?> FindAsync(string key) =>
        await ExcludeChild(await users.FindByEmailAsync(key) ?? await users.FindByNameAsync(key));

    private async Task<AppUser?> FindByEmailAsync(string email) => await ExcludeChild(await users.FindByEmailAsync(email));

    private static object Errors(IdentityResult r) => new { errors = r.Errors.Select(e => e.Description) };

    private async Task<AuthResultDto> IssueAsync(AppUser u)
    {
        var roles = await users.GetRolesAsync(u);
        var t = jwt.Create(u, roles);
        return new AuthResultDto(t.Token, t.ExpiresAt, roles.FirstOrDefault() ?? Roles.Parent, u.Id, u.FullName, u.Email ?? "");
    }

    /// <summary>
    /// Đăng ký bằng email: luôn tạo role <b>Parent</b>. Tài khoản mới CHƯA xác minh email; server gửi mã 6 số,
    /// nhập mã ở <c>verify-otp</c> thì email được xác minh và nhận JWT.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        if (dto.Email.EndsWith("@" + ChildService.FakeEmailDomain, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { errors = new[] { "Email không hợp lệ" } });
        var user = await users.FindByEmailAsync(dto.Email);
        if (user is not null && await users.IsInRoleAsync(user, Roles.Child))
            return BadRequest(new { errors = new[] { "Email không hợp lệ" } });
        if (user is null)
        {
            user = new AppUser { UserName = dto.Email, Email = dto.Email, FullName = dto.FullName.Trim(), EmailConfirmed = false };
            var result = await users.CreateAsync(user, dto.Password);
            if (!result.Succeeded) return BadRequest(Errors(result));
            await users.AddToRoleAsync(user, Roles.Parent);
        }
        else if (user.EmailConfirmed)
        {
            // Cùng dạng lỗi với Identity để app dịch sang tiếng Việt.
            return BadRequest(new { errors = new[] { $"Email '{dto.Email}' is already taken." } });
        }
        else
        {
            // Email này đã có người đăng ký nhưng chưa ai xác minh được. Cho đăng ký lại (ghi đè tên và mật khẩu)
            // để người giữ hộp thư thật không bị kẻ khác chiếm email; ai cũng vẫn phải nhập mã gửi về hộp thư mới vào được.
            var reset = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), dto.Password);
            if (!reset.Succeeded) return BadRequest(Errors(reset));
            user.FullName = dto.FullName.Trim();
            await users.UpdateAsync(user);
        }

        await otp.SendAsync(user);
        return Ok(new { requiresEmailVerification = true, message = "Đã gửi mã xác minh tới email. Nhập mã để hoàn tất đăng ký." });
    }

    /// <summary>Bước 1: email + mật khẩu. Đúng thì gửi Email OTP. Sai 5 lần liên tiếp thì khoá tạm 5 phút.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await FindAsync(dto.UserNameOrEmail);
        if (user is null) return Unauthorized(new { message = "Sai tài khoản hoặc mật khẩu" });
        if (await users.IsLockedOutAsync(user))
            return StatusCode(423, new { message = "Đăng nhập sai quá nhiều lần, hãy thử lại sau ít phút" });
        if (!await users.CheckPasswordAsync(user, dto.Password))
        {
            await users.AccessFailedAsync(user);
            return Unauthorized(new { message = "Sai tài khoản hoặc mật khẩu" });
        }
        await users.ResetAccessFailedCountAsync(user);
        if (!user.IsActive) return StatusCode(403, new { message = "Tài khoản đã bị khoá" });

        await otp.SendAsync(user);
        return Ok(new { requiresOtp = true, message = "Đã gửi mã OTP tới email" });
    }

    /// <summary>Gửi lại mã. Luôn trả cùng một thông báo; gửi dồn dập (dưới 1 phút) bị bỏ qua.</summary>
    [HttpPost("resend-otp")]
    public async Task<IActionResult> Resend(ResendOtpDto dto)
    {
        var user = await FindAsync(dto.UserNameOrEmail);
        if (user is not null && user.IsActive && !await otp.SentRecentlyAsync(user)) await otp.SendAsync(user);
        return Ok(new { message = "Nếu tài khoản tồn tại, mã OTP mới đã được gửi" });
    }

    /// <summary>Bước 2: nhập OTP → nhận JWT. Role trả về để app điều hướng.</summary>
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(VerifyOtpDto dto)
    {
        var user = await FindAsync(dto.UserNameOrEmail);
        if (user is null || !user.IsActive || !await otp.VerifyAsync(user, dto.Code))
            return Unauthorized(new { message = "OTP sai hoặc hết hạn" });

        // Nhập đúng mã gửi tới hộp thư nghĩa là người dùng giữ hộp thư này: coi như email đã xác minh.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }
        return Ok(await IssueAsync(user));
    }

    /// <summary>
    /// Quên mật khẩu, bước 1: gửi mã 6 số tới email. Luôn trả cùng một thông báo dù email có tài khoản hay không,
    /// để không lộ email nào đã đăng ký.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        var user = await FindByEmailAsync(dto.Email);
        if (user is not null && user.IsActive && !await otp.SentRecentlyAsync(user))
            await otp.SendAsync(user, OtpPurpose.PasswordReset);
        return Ok(new { message = "Nếu email có tài khoản, mã đặt lại mật khẩu đã được gửi. Mã có hiệu lực 5 phút." });
    }

    /// <summary>
    /// Quên mật khẩu, bước 2: mã + mật khẩu mới. Đúng mã thì đổi mật khẩu và đăng nhập luôn (trả JWT).
    /// Đổi mật khẩu làm SecurityStamp đổi nên mọi token cũ của tài khoản mất hiệu lực.
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
    {
        var user = await FindByEmailAsync(dto.Email);
        if (user is null || !user.IsActive) return Unauthorized(new { message = "Mã không đúng hoặc đã hết hạn" });

        // Kiểm tra luật mật khẩu TRƯỚC khi dùng mã, để nhập mật khẩu yếu không làm mất mã.
        foreach (var validator in users.PasswordValidators)
        {
            var check = await validator.ValidateAsync(users, user, dto.NewPassword);
            if (!check.Succeeded) return BadRequest(Errors(check));
        }

        if (!await otp.VerifyAsync(user, dto.Code, OtpPurpose.PasswordReset))
            return Unauthorized(new { message = "Mã không đúng hoặc đã hết hạn" });

        var reset = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), dto.NewPassword);
        if (!reset.Succeeded) return BadRequest(Errors(reset));

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }
        await users.ResetAccessFailedCountAsync(user);
        return Ok(await IssueAsync(user));
    }

    /// <summary>OAuth2: app đăng nhập Google, gửi idToken lên đây để backend xác thực. Tài khoản mới là Parent.</summary>
    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin(GoogleLoginDto dto)
    {
        var clientId = cfg["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(503, new { message = "Server chưa cấu hình đăng nhập Google" });

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
        }
        catch (InvalidJwtException)
        {
            return Unauthorized(new { message = "Google token không hợp lệ" });
        }
        if (!payload.EmailVerified || string.IsNullOrEmpty(payload.Email))
            return Unauthorized(new { message = "Email Google chưa được xác minh" });

        var user = await FindByEmailAsync(payload.Email);
        if (user is null)
        {
            user = new AppUser { UserName = payload.Email, Email = payload.Email, EmailConfirmed = true,
                FullName = payload.Name ?? payload.Email };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) return BadRequest(Errors(created));
            await users.AddToRoleAsync(user, Roles.Parent);
        }
        if (!user.IsActive) return StatusCode(403, new { message = "Tài khoản đã bị khoá" });
        return Ok(await IssueAsync(user));
    }

    /// <summary>
    /// Máy của bé nhập mã liên kết (do phụ huynh tạo ở <c>POST /api/children/{id}/link-code</c>) để nhận token role Child.
    /// Giới hạn số lần thử theo IP vì mã chỉ có 8 số và không gắn với bé cụ thể.
    /// </summary>
    [HttpPost("child-link"), EnableRateLimiting("child-link")]
    public async Task<IActionResult> ChildLink(ChildLinkRequestDto dto)
    {
        var result = await children.RedeemLinkCodeAsync(dto.Code, dto.DeviceId, dto.DeviceName);
        return result is null ? Unauthorized(new { message = "Mã không đúng hoặc đã hết hạn" }) : Ok(result);
    }

    /// <summary>Đăng xuất: token hiện tại bị thu hồi ngay trên server (các máy khác của cùng tài khoản không bị ảnh hưởng). Với bé: thu hồi phiên của thiết bị này.</summary>
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (Guid.TryParse(User.FindFirstValue(JwtService.ChildSessionClaim), out var sid))
            await db.ChildSessions.Where(s => s.Id == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (jti is not null && !await db.RevokedTokens.AnyAsync(r => r.Jti == jti))
        {
            var exp = long.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Exp), out var e)
                ? DateTimeOffset.FromUnixTimeSeconds(e).UtcDateTime : DateTime.UtcNow.AddDays(31);
            db.RevokedTokens.Add(new RevokedToken { Jti = jti, ExpiresAt = exp });
            db.RevokedTokens.RemoveRange(await db.RevokedTokens.Where(r => r.ExpiresAt < DateTime.UtcNow).ToListAsync());
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    private async Task<ProfileDto> ProfileOf(AppUser u) =>
        new(u.Id, u.FullName, u.EmailConfirmed && u.Email!.EndsWith("@" + ChildService.FakeEmailDomain) ? "" : u.Email ?? "", u.PhoneNumber, (await users.GetRolesAsync(u)).FirstOrDefault() ?? Roles.Parent);

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await users.FindByIdAsync(CurrentUserId);
        return user is null ? Unauthorized() : Ok(await ProfileOf(user));
    }

    /// <summary>Tự cập nhật hồ sơ: họ tên và số điện thoại (để trống = xoá). Email và role không tự đổi được.</summary>
    [Authorize(Roles = $"{Roles.Parent},{Roles.Admin}"), HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileDto dto)
    {
        var user = await users.FindByIdAsync(CurrentUserId);
        if (user is null) return Unauthorized();
        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? Ok(await ProfileOf(user)) : BadRequest(Errors(result));
    }
}
