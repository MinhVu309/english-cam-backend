using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Services;

/// <summary>
/// Thống kê toàn hệ thống cho Admin (chỉ đọc). Ngày chia theo <see cref="LocalTime"/> như chuỗi ngày và dashboard phụ huynh.
/// Từ khó đếm trên bản từ vựng ĐÃ XUẤT BẢN, cộng dồn mọi bé, để Admin biết nên sửa/thêm ví dụ cho từ nào.
/// </summary>
public class AdminStatsService(AppDbContext db, PublishedVocab vocab, LocalTime time)
{
    public const int HardWordLimit = 10;

    /// <summary>Từ phải có ít nhất chừng này lượt trả lời (đúng + sai, mọi bé) mới được xét, để một lần sai ngẫu nhiên không lên đầu bảng.</summary>
    public const int MinAnswersForHardWord = 5;

    public const int RecentDays = 7;

    public async Task<AdminStatsDto> BuildAsync()
    {
        var now = time.NowUtc;
        var today = time.Today;
        var since = today.AddDays(-(RecentDays - 1)); // 7 ngày gồm cả hôm nay

        // Số người theo role. Bé có tài khoản riêng (role Child) nên đếm role là đếm đúng.
        var byRole = await (from ur in db.UserRoles
                            join r in db.Roles on ur.RoleId equals r.Id
                            group r by r.Name into g
                            select new { Role = g.Key!, N = g.Count() }).ToDictionaryAsync(x => x.Role, x => x.N);
        // "Khoá" chỉ tính tài khoản Admin/Parent: bé tắt hồ sơ là việc của phụ huynh, không phải Admin khoá.
        var locked = await (from u in db.Users
                            join ur in db.UserRoles on u.Id equals ur.UserId
                            join r in db.Roles on ur.RoleId equals r.Id
                            where !u.IsActive && r.Name != Roles.Child
                            select u.Id).CountAsync();
        var parentIds = await (from ur in db.UserRoles
                               join r in db.Roles on ur.RoleId equals r.Id
                               where r.Name == Roles.Parent
                               select ur.UserId).ToListAsync();
        var weekAgo = now.AddDays(-RecentDays);
        var newParents = await db.Users.CountAsync(u => parentIds.Contains(u.Id) && u.CreatedAt >= weekAgo);

        var childTotal = await db.Children.CountAsync();
        var childActive = await db.Children.CountAsync(c => c.IsActive);
        var activeToday = await db.DailyActivities.Where(d => d.Date == today).Select(d => d.ChildId).Distinct().CountAsync();
        var active7 = await db.DailyActivities.Where(d => d.Date >= since).Select(d => d.ChildId).Distinct().CountAsync();
        var devices = await db.ChildSessions.CountAsync(s => !s.Revoked && s.ExpiresAt > now);

        var pub = await vocab.GetDetailAsync();
        var published = pub?.Words.ToDictionary(w => w.Id) ?? [];

        var sessionsTotal = await db.GameSessions.CountAsync();
        var sessions7 = await db.GameSessions.CountAsync(s => s.StartedAt >= weekAgo);
        var seconds7 = await db.DailyActivities.Where(d => d.Date >= since).SumAsync(d => (long?)d.Seconds) ?? 0;
        var stars = await db.ChildStats.SumAsync(s => (long?)s.Stars) ?? 0;

        var progress = await db.WordProgress.AsNoTracking().Where(p => p.WrongCount > 0)
            .GroupBy(p => p.WordId)
            .Select(g => new { WordId = g.Key, Correct = g.Sum(p => p.CorrectCount), Wrong = g.Sum(p => p.WrongCount), Kids = g.Count() })
            .ToListAsync();
        var hardest = progress
            .Where(x => published.ContainsKey(x.WordId) && x.Correct + x.Wrong >= MinAnswersForHardWord)
            .Select(x => (X: x, Total: x.Correct + x.Wrong))
            .OrderByDescending(t => (double)t.X.Wrong / t.Total).ThenByDescending(t => t.X.Wrong).ThenBy(t => t.X.WordId, StringComparer.Ordinal)
            .Take(HardWordLimit)
            .Select(t =>
            {
                var w = published[t.X.WordId];
                return new AdminHardWordDto(w.Id, w.En, w.Vi, t.Total, t.X.Wrong, Math.Round((double)t.X.Wrong / t.Total, 3), t.X.Kids);
            }).ToList();

        return new AdminStatsDto(
            new AdminUserCountsDto(byRole.GetValueOrDefault(Roles.Admin), byRole.GetValueOrDefault(Roles.Parent),
                byRole.GetValueOrDefault(Roles.Child), locked, newParents),
            new AdminChildCountsDto(childTotal, childActive, activeToday, active7, devices),
            new AdminVocabCountsDto(pub?.Version, published.Count, pub?.Categories.Count ?? 0),
            new AdminActivityDto(sessionsTotal, sessions7, (int)Math.Round(seconds7 / 60.0, MidpointRounding.AwayFromZero), stars),
            hardest, now);
    }
}
