using System.ComponentModel.DataAnnotations;

namespace EnglishCam.Api.Dtos;

public record RegisterDto([Required, EmailAddress] string Email, [Required, MinLength(8)] string Password,
    [Required, StringLength(100, MinimumLength = 2)] string FullName);
public record LoginDto([Required] string UserNameOrEmail, [Required] string Password);
public record VerifyOtpDto([Required] string UserNameOrEmail, [Required] string Code);
public record ResendOtpDto([Required] string UserNameOrEmail);
public record ForgotPasswordDto([Required, EmailAddress] string Email);
public record ResetPasswordDto([Required, EmailAddress] string Email, [Required] string Code, [Required, MinLength(8)] string NewPassword);
public record GoogleLoginDto([Required] string IdToken);
public record UpdateProfileDto([Required, StringLength(100, MinimumLength = 2)] string FullName,
    [RegularExpression(@"^\+?[0-9 ]{9,15}$", ErrorMessage = "Số điện thoại không hợp lệ")] string? PhoneNumber);

/// <summary>Kết quả đăng nhập thành công. Role để app điều hướng Admin / Parent / Child.</summary>
public record AuthResultDto(string Token, DateTime ExpiresAt, string Role, string UserId, string FullName, string Email);
public record ProfileDto(string Id, string FullName, string Email, string? PhoneNumber, string Role);
