using Microsoft.AspNetCore.Identity;

namespace EnglishCam.Api.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Parent = "Parent";
    public const string Child = "Child";

    public static readonly string[] All = [Admin, Parent, Child];
}

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Mã OTP đang chờ dùng của một người dùng (mỗi người chỉ giữ một mã; chỉ lưu băm).</summary>
public class OtpCode
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public bool Used { get; set; }
}

/// <summary>JWT đã đăng xuất (theo jti). Giữ đến hết hạn của token rồi dọn.</summary>
public class RevokedToken
{
    public string Jti { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Hồ sơ của một bé. Mỗi bé có một AppUser riêng (role Child, không mật khẩu, email giả) để đi qua cùng cơ chế JWT.
/// Chỉ giữ thông tin tối thiểu: biệt danh, avatar, năm sinh (tuỳ chọn).
/// </summary>
public class ChildProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public AppUser? User { get; set; }
    public string ParentId { get; set; } = "";
    public AppUser? Parent { get; set; }
    public string Nickname { get; set; } = "";
    public string? AvatarKey { get; set; }
    public int? BirthYear { get; set; }

    /// <summary>Giới hạn số phút chơi mỗi ngày do phụ huynh đặt; null = không giới hạn.</summary>
    public int? DailyLimitMinutes { get; set; }
    public int DailyWordGoal { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ChildSession> Sessions { get; set; } = [];
}

/// <summary>Một thiết bị đã đăng nhập cho bé. Một bé có nhiều phiên cùng lúc; thu hồi từng phiên được.</summary>
public class ChildSession
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }
    public ChildProfile? Child { get; set; }
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Revoked { get; set; }
}

/// <summary>Mã liên kết thiết bị (8 số, sống 10 phút, dùng một lần). Chỉ lưu băm.</summary>
public class ChildLinkCode
{
    public int Id { get; set; }
    public Guid ChildId { get; set; }
    public ChildProfile? Child { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
}
