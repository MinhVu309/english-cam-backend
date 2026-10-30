using System.Security.Cryptography;
using System.Text;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Services;

/// <summary>Logic hồ sơ bé dùng chung cho controller phụ huynh và Admin: tạo/xoá, phiên thiết bị, mã liên kết.</summary>
public class ChildService(AppDbContext db, UserManager<AppUser> users, JwtService jwt)
{
    public const string FakeEmailDomain = "child.englishcam.local";
    public const int MaxChildrenPerParent = 10;
    public const int MaxDevicesPerChild = 10;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"childlink:{code}")));

    /// <summary>Chiếu hồ sơ sang DTO ngay trong SQL (đếm thiết bị còn hiệu lực, lần dùng cuối).</summary>
    public static IQueryable<ChildDto> Project(IQueryable<ChildProfile> q, DateTime now) =>
        q.Select(c => new ChildDto(c.Id, c.Nickname, c.AvatarKey, c.BirthYear, c.DailyLimitMinutes, c.DailyWordGoal,
            c.IsActive, c.Sessions.Count(s => !s.Revoked && s.ExpiresAt > now),
            c.Sessions.Max(s => s.LastUsedAt), c.CreatedAt));

    public async Task<(ChildProfile? Child, string? Error)> CreateAsync(string parentId, ChildCreateDto dto)
    {
        if (await db.Children.CountAsync(c => c.ParentId == parentId) >= MaxChildrenPerParent)
            return (null, $"Mỗi tài khoản tối đa {MaxChildrenPerParent} hồ sơ bé");

        var nickname = dto.Nickname.Trim();
        await using var tx = await db.Database.BeginTransactionAsync();
        var key = Guid.NewGuid().ToString("N");
        var user = new AppUser
        {
            UserName = $"child-{key}", Email = $"child-{key}@{FakeEmailDomain}", EmailConfirmed = true, FullName = nickname
        };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded) return (null, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, Roles.Child);

        var child = new ChildProfile
        {
            UserId = user.Id, ParentId = parentId, Nickname = nickname,
            AvatarKey = string.IsNullOrWhiteSpace(dto.AvatarKey) ? null : dto.AvatarKey.Trim(), BirthYear = dto.BirthYear
        };
        db.Children.Add(child);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (child, null);
    }

    /// <summary>Xoá bé và mọi dữ liệu của bé (phiên, mã liên kết; sau này cả tiến độ qua cascade).</summary>
    public async Task DeleteAsync(ChildProfile child)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var userId = child.UserId;
        db.Children.Remove(child);
        await db.SaveChangesAsync();
        var user = await users.FindByIdAsync(userId);
        if (user is not null) await users.DeleteAsync(user);
        await tx.CommitAsync();
    }

    /// <summary>Xoá toàn bộ tiến độ học của bé (sao, ván, từ đã học, huy hiệu, hoạt động). Hồ sơ và thiết bị của bé giữ nguyên.</summary>
    public async Task ResetProgressAsync(Guid childId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.ChildBadges.Where(x => x.ChildId == childId).ExecuteDeleteAsync();
        await db.WordProgress.Where(x => x.ChildId == childId).ExecuteDeleteAsync();
        await db.GameSessions.Where(x => x.ChildId == childId).ExecuteDeleteAsync(); // đáp án xoá theo (cascade)
        await db.DailyActivities.Where(x => x.ChildId == childId).ExecuteDeleteAsync();
        await db.ProcessedEvents.Where(x => x.ChildId == childId).ExecuteDeleteAsync();
        await db.ChildStats.Where(x => x.ChildId == childId).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }

    /// <summary>Xoá mọi bé của một phụ huynh (gọi trước khi xoá chính tài khoản phụ huynh).</summary>
    public async Task DeleteAllOfParentAsync(string parentId)
    {
        foreach (var child in await db.Children.Where(c => c.ParentId == parentId).ToListAsync())
            await DeleteAsync(child);
    }

    /// <summary>Cấp phiên cho một thiết bị. Cùng thiết bị đăng nhập lại thì phiên cũ của thiết bị đó bị thay.</summary>
    public async Task<(AuthResultDto? Result, string? Error)> IssueSessionAsync(ChildProfile child, string deviceId, string? deviceName)
    {
        if (!child.IsActive) return (null, "Hồ sơ bé đang bị tắt");
        var now = DateTime.UtcNow;
        deviceId = deviceId.Trim();

        await db.ChildSessions.Where(s => s.ChildId == child.Id && s.DeviceId == deviceId && !s.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
        var active = await db.ChildSessions.CountAsync(s => s.ChildId == child.Id && !s.Revoked && s.ExpiresAt > now);
        if (active >= MaxDevicesPerChild)
            return (null, $"Bé đã đăng nhập trên {MaxDevicesPerChild} thiết bị. Hãy thu hồi bớt thiết bị cũ.");

        var user = await users.FindByIdAsync(child.UserId);
        if (user is null) return (null, "Không tìm thấy tài khoản của bé");

        var sid = Guid.NewGuid();
        var token = jwt.Create(user, [Roles.Child], sid);
        db.ChildSessions.Add(new ChildSession
        {
            Id = sid, ChildId = child.Id, DeviceId = deviceId, ExpiresAt = token.ExpiresAt,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Thiết bị" : deviceName.Trim()
        });
        await db.SaveChangesAsync();
        return (new AuthResultDto(token.Token, token.ExpiresAt, Roles.Child, user.Id, child.Nickname, ""), null);
    }

    /// <summary>Tạo mã liên kết 8 số; mã chưa dùng trước đó của bé bị huỷ.</summary>
    public async Task<LinkCodeDto> CreateLinkCodeAsync(ChildProfile child)
    {
        var now = DateTime.UtcNow;
        await db.ChildLinkCodes.Where(l => l.ChildId == child.Id || l.ExpiresAt < now).ExecuteDeleteAsync();

        while (true)
        {
            var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
            var hash = HashCode(code);
            if (await db.ChildLinkCodes.AnyAsync(l => l.CodeHash == hash)) continue;
            var expires = now + CodeLifetime;
            db.ChildLinkCodes.Add(new ChildLinkCode { ChildId = child.Id, CodeHash = hash, ExpiresAt = expires });
            await db.SaveChangesAsync();
            return new LinkCodeDto(code, expires);
        }
    }

    /// <summary>Đổi mã lấy phiên. Mã chỉ dùng được một lần (đánh dấu bằng UPDATE có điều kiện, an toàn khi gọi song song).</summary>
    public async Task<AuthResultDto?> RedeemLinkCodeAsync(string code, string deviceId, string? deviceName)
    {
        var digits = new string(code.Where(char.IsDigit).ToArray());
        if (digits.Length != 8) return null;
        var hash = HashCode(digits);
        var now = DateTime.UtcNow;

        var link = await db.ChildLinkCodes.AsNoTracking().Include(l => l.Child)
            .FirstOrDefaultAsync(l => l.CodeHash == hash && !l.Used && l.ExpiresAt > now);
        if (link?.Child is null || !link.Child.IsActive) return null;
        var claimed = await db.ChildLinkCodes.Where(l => l.Id == link.Id && !l.Used)
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.Used, true));
        if (claimed == 0) return null;

        var (result, _) = await IssueSessionAsync(link.Child, deviceId, deviceName);
        return result;
    }
}
