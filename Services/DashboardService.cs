using System.Globalization;
using System.Text;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Services;

/// <summary>
/// Tổng hợp tiến độ của bé cho phụ huynh. Chỉ đọc. "Tổng số từ" và "đã học" tính trên bản từ vựng ĐÃ XUẤT BẢN
/// (từ đã bị xoá khỏi bản phát hành thì không còn được tính), để tử số không bao giờ vượt mẫu số.
/// </summary>
public class DashboardService(AppDbContext db, PublishedVocab vocab, LocalTime time)
{
    public const int MaxDays = 90;
    private const int WeakWordLimit = 5;
    private const int RecentLimit = 10;
    private const int RecentBadgeLimit = 5;
    private const int MinAttemptsForWeakWord = 2;

    private static int Minutes(long seconds) => (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero);

    // ---------- Danh sách các bé ----------

    public async Task<List<OverviewChildDto>> OverviewAsync(string parentId)
    {
        var children = await db.Children.AsNoTracking().Where(c => c.ParentId == parentId).OrderBy(c => c.CreatedAt).ToListAsync();
        if (children.Count == 0) return [];
        var ids = children.Select(c => c.Id).ToList();
        var pub = await vocab.GetDetailAsync();
        var published = pub?.Words.Select(w => w.Id).ToHashSet() ?? [];
        var today = time.Today;
        var now = time.NowUtc;

        var stats = await db.ChildStats.AsNoTracking().Where(s => ids.Contains(s.ChildId)).ToDictionaryAsync(s => s.ChildId);
        var learned = (await db.WordProgress.AsNoTracking().Where(p => ids.Contains(p.ChildId) && p.Learned)
            .Select(p => new { p.ChildId, p.WordId }).ToListAsync())
            .Where(p => published.Contains(p.WordId)).GroupBy(p => p.ChildId).ToDictionary(g => g.Key, g => g.Count());
        var todays = await db.DailyActivities.AsNoTracking().Where(d => ids.Contains(d.ChildId) && d.Date == today)
            .ToDictionaryAsync(d => d.ChildId);
        var devices = (await db.ChildSessions.AsNoTracking().Where(s => ids.Contains(s.ChildId) && !s.Revoked && s.ExpiresAt > now)
            .Select(s => s.ChildId).ToListAsync()).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        return children.Select(c =>
        {
            var st = stats.GetValueOrDefault(c.Id);
            var td = todays.GetValueOrDefault(c.Id);
            var streak = st?.LastActiveDate is { } last && last >= today.AddDays(-1) ? st.CurrentStreak : 0;
            return new OverviewChildDto(c.Id, c.Nickname, c.AvatarKey, c.IsActive, c.DailyWordGoal, c.DailyLimitMinutes,
                learned.GetValueOrDefault(c.Id), published.Count, st?.Stars ?? 0, streak, Minutes(td?.Seconds ?? 0),
                td?.WordsLearned ?? 0, devices.GetValueOrDefault(c.Id), st?.LastActiveDate);
        }).ToList();
    }

    // ---------- Dashboard một bé ----------

    public async Task<ChildDashboardDto?> BuildAsync(Guid childId, int days)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var child = await db.Children.AsNoTracking().FirstOrDefaultAsync(c => c.Id == childId);
        if (child is null) return null;
        var pub = await vocab.GetDetailAsync();
        var words = pub?.Words ?? [];
        var byId = words.ToDictionary(w => w.Id);
        var today = time.Today;
        var from = today.AddDays(-(days - 1));

        var stats = await db.ChildStats.AsNoTracking().FirstOrDefaultAsync(s => s.ChildId == childId);
        var progress = await db.WordProgress.AsNoTracking().Where(p => p.ChildId == childId).ToListAsync();
        var learnedIds = progress.Where(p => p.Learned && byId.ContainsKey(p.WordId)).Select(p => p.WordId).ToHashSet();

        var window = await db.DailyActivities.AsNoTracking().Where(d => d.ChildId == childId && d.Date >= from && d.Date <= today)
            .ToDictionaryAsync(d => d.Date);
        var daysData = Enumerable.Range(0, days).Select(i =>
        {
            var date = from.AddDays(i);
            var d = window.GetValueOrDefault(date);
            return new DashboardDayDto(date, d?.Seconds ?? 0, d?.WordsLearned ?? 0, d?.Rounds ?? 0, d?.Scans ?? 0);
        }).ToList();
        var todayRow = daysData[^1];
        var totalSeconds = await db.DailyActivities.AsNoTracking().Where(d => d.ChildId == childId).SumAsync(d => (long)d.Seconds);

        // Độ chính xác: tỉ lệ câu đúng ngay lần đầu trong các ván "Nghe & chọn hình" của khoảng đang xem.
        var fromUtc = from.ToDateTime(TimeOnly.MinValue).AddDays(-1); // rộng hơn 1 ngày cho chắc, lọc lại theo ngày địa phương bên dưới
        var matchSessions = (await db.GameSessions.AsNoTracking()
            .Where(s => s.ChildId == childId && s.GameType == Models.GameType.Match && s.StartedAt >= fromUtc)
            .Select(s => new { s.StartedAt, s.StarsEarned, s.Questions }).ToListAsync())
            .Where(s => time.DateOf(s.StartedAt) >= from).ToList();
        var asked = matchSessions.Sum(s => s.Questions);
        int? accuracy = asked == 0 ? null : (int)Math.Round(100.0 * matchSessions.Sum(s => s.StarsEarned) / asked, MidpointRounding.AwayFromZero);

        var categories = (pub?.Categories ?? []).Select(c =>
        {
            var inCat = words.Where(w => w.Category == c.Id).ToList();
            return new DashboardCategoryDto(c.Id, c.Vi, c.En, c.Emoji, c.Color, inCat.Count(w => learnedIds.Contains(w.Id)), inCat.Count);
        }).Where(c => c.Total > 0).ToList();

        var weak = progress
            .Where(p => byId.ContainsKey(p.WordId) && p.WrongCount > 0 && p.CorrectCount + p.WrongCount >= MinAttemptsForWeakWord)
            .Select(p => (P: p, Total: p.CorrectCount + p.WrongCount))
            .OrderByDescending(x => (double)x.P.WrongCount / x.Total).ThenByDescending(x => x.P.WrongCount).ThenBy(x => x.P.WordId, StringComparer.Ordinal)
            .Take(WeakWordLimit)
            .Select(x =>
            {
                var w = byId[x.P.WordId];
                return new DashboardWeakWordDto(w.Id, w.En, w.Vi, w.Emoji, x.P.WrongCount, x.Total,
                    (int)Math.Round(100.0 * x.P.CorrectCount / x.Total, MidpointRounding.AwayFromZero));
            }).ToList();

        var unlocked = await (from cb in db.ChildBadges.AsNoTracking()
                              join b in db.Badges.AsNoTracking() on cb.BadgeId equals b.Id
                              where cb.ChildId == childId
                              orderby cb.UnlockedAt descending
                              select new DashboardBadgeDto(b.Id, b.Name, b.Emoji, b.Description, cb.UnlockedAt)).ToListAsync();

        var recentRows = await db.GameSessions.AsNoTracking().Where(s => s.ChildId == childId)
            .OrderByDescending(s => s.StartedAt).Take(RecentLimit)
            .Select(s => new
            {
                s.StartedAt, s.GameType, s.StarsEarned, s.Correct, s.Questions, s.DurationSec,
                FirstWord = s.Answers.OrderBy(a => a.Id).Select(a => a.WordId).FirstOrDefault()
            }).ToListAsync();
        var recent = recentRows.Select(r =>
        {
            var hunt = r.GameType == Models.GameType.Hunt;
            var w = hunt && r.FirstWord is not null ? byId.GetValueOrDefault(r.FirstWord) : null;
            return new DashboardActivityDto(r.StartedAt, hunt ? "hunt" : "match", r.StarsEarned, r.Correct, r.Questions, r.DurationSec,
                hunt ? r.FirstWord : null, w?.En, w?.Vi);
        }).ToList();

        var streak = stats?.LastActiveDate is { } last && last >= today.AddDays(-1) ? stats.CurrentStreak : 0;
        return new ChildDashboardDto(child.Id, child.Nickname, child.AvatarKey, child.IsActive, child.DailyWordGoal, child.DailyLimitMinutes,
            days, pub?.Version ?? 0,
            new DashboardTotalsDto(learnedIds.Count, words.Count, stats?.Stars ?? 0, stats?.Rounds ?? 0, stats?.PerfectRounds ?? 0,
                stats?.Found ?? 0, streak, stats?.BestStreak ?? 0, Minutes(totalSeconds), accuracy),
            new DashboardTodayDto(todayRow.Seconds, todayRow.WordsLearned, todayRow.Rounds, todayRow.Scans, todayRow.WordsLearned >= child.DailyWordGoal),
            daysData, categories, weak, unlocked.Take(RecentBadgeLimit).ToList(), unlocked.Count, await db.Badges.CountAsync(), recent);
    }

    // ---------- Xuất CSV ----------

    /// <summary>Ô CSV: bọc ngoặc kép khi cần, và chặn "CSV injection" (ô bắt đầu bằng = + - @ bị Excel/Sheets hiểu là công thức).</summary>
    public static string Cell(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + s : s;
        return t.IndexOfAny(['"', ',', '\n', '\r']) >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;
    }

    private static byte[] Utf8WithBom(string text) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];

    /// <summary>Mỗi ngày một dòng (đủ cả ngày không học): date,minutes,seconds,words_learned,rounds,scans.</summary>
    public async Task<byte[]?> DailyCsvAsync(Guid childId, int days)
    {
        var d = await BuildAsync(childId, days);
        if (d is null) return null;
        var sb = new StringBuilder("date,minutes,seconds,words_learned,rounds,scans\r\n");
        foreach (var x in d.DaysData)
            sb.Append(CultureInfo.InvariantCulture, $"{x.Date:yyyy-MM-dd},{x.Seconds / 60.0:0.#},{x.Seconds},{x.WordsLearned},{x.Rounds},{x.Scans}\r\n");
        return Utf8WithBom(sb.ToString());
    }

    /// <summary>Mỗi từ của bản đang phát một dòng, kèm tình trạng học của bé.</summary>
    public async Task<byte[]?> WordsCsvAsync(Guid childId)
    {
        if (!await db.Children.AnyAsync(c => c.Id == childId)) return null;
        var pub = await vocab.GetDetailAsync();
        var progress = await db.WordProgress.AsNoTracking().Where(p => p.ChildId == childId).ToDictionaryAsync(p => p.WordId);
        var cats = (pub?.Categories ?? []).ToDictionary(c => c.Id, c => c.Vi);
        var sb = new StringBuilder("word_id,english,vietnamese,category,learned,times_seen,correct,wrong,box,last_seen_utc\r\n");
        foreach (var w in pub?.Words ?? [])
        {
            var p = progress.GetValueOrDefault(w.Id);
            sb.Append(CultureInfo.InvariantCulture,
                $"{Cell(w.Id)},{Cell(w.En)},{Cell(w.Vi)},{Cell(cats.GetValueOrDefault(w.Category))},{(p?.Learned == true ? "yes" : "no")},{p?.SeenCount ?? 0},{p?.CorrectCount ?? 0},{p?.WrongCount ?? 0},{p?.Box ?? 1},{p?.LastSeenAt:yyyy-MM-ddTHH:mm:ssZ}\r\n");
        }
        return Utf8WithBom(sb.ToString());
    }
}
