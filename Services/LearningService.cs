using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Services;

public enum SessionStatus { Ok, Duplicate, Invalid, Conflict, TooMany }

public record SessionOutcome(SessionStatus Status, string? Error, SessionResultDto? Result);

/// <summary>
/// Nơi duy nhất quyết định sao, chuỗi ngày, huy hiệu và hộp Leitner của bé. Server TỰ TÍNH từ danh sách đáp án
/// (không tin con số sao do app gửi) và áp dụng mỗi ván/sự kiện đúng một lần (dựa vào Id do app sinh).
/// </summary>
public class LearningService(AppDbContext db, TimeProvider clock, IConfiguration cfg)
{
    public const int MaxMatchAnswers = 20;
    public const int MaxHuntAnswers = 5;
    public const int MinAnswersForPerfect = 5;
    public const int MaxSessionsPerHour = 60;
    public const int MaxEventsPerBatch = 200;
    private const int MaxSecondsPerEvent = 3600;
    private const int MaxSecondsPerDay = 86400;

    private readonly LocalTime time = new(clock, cfg);
    private DateTime Now => time.NowUtc;
    private DateOnly LocalDate(DateTime utc) => time.DateOf(utc);

    /// <summary>Giờ do app gửi chỉ được tin trong khoảng hợp lý (mất mạng lâu vẫn được); ngoài khoảng thì dùng giờ máy chủ.</summary>
    private DateTime ClampAt(DateTime? at)
    {
        var now = Now;
        if (at is null) return now;
        var t = at.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(at.Value, DateTimeKind.Utc) : at.Value.ToUniversalTime();
        return t > now.AddMinutes(5) || t < now.AddDays(-30) ? now : t;
    }

    // ---------- Trạng thái ----------

    public async Task<Dictionary<string, ChildWordProgress>> ProgressMapAsync(Guid childId) =>
        await db.WordProgress.AsNoTracking().Where(p => p.ChildId == childId).ToDictionaryAsync(p => p.WordId);

    public async Task<ProgressSnapshotDto> SnapshotAsync(Guid childId)
    {
        var stats = await db.ChildStats.AsNoTracking().FirstOrDefaultAsync(s => s.ChildId == childId)
                    ?? new ChildStats { ChildId = childId };
        var learned = await db.WordProgress.AsNoTracking().Where(p => p.ChildId == childId && p.Learned)
            .Select(p => p.WordId).OrderBy(x => x).ToListAsync();
        var badges = await db.ChildBadges.AsNoTracking().Where(b => b.ChildId == childId)
            .Select(b => b.BadgeId).OrderBy(x => x).ToListAsync();
        var child = await db.Children.AsNoTracking().Where(c => c.Id == childId)
            .Select(c => new { c.DailyWordGoal, c.DailyLimitMinutes }).FirstAsync();
        var today = LocalDate(Now);
        var d = await db.DailyActivities.AsNoTracking().FirstOrDefaultAsync(x => x.ChildId == childId && x.Date == today);
        // Chuỗi chỉ còn giá trị nếu hôm qua hoặc hôm nay bé có học; để lỡ một ngày là chuỗi về 0.
        var current = stats.LastActiveDate is { } last && last >= today.AddDays(-1) ? stats.CurrentStreak : 0;
        return new ProgressSnapshotDto(stats.Stars, stats.Rounds, stats.PerfectRounds, stats.Found, current, stats.BestStreak,
            learned, badges, child.DailyWordGoal, child.DailyLimitMinutes,
            new TodayDto(d?.Seconds ?? 0, d?.WordsLearned ?? 0, d?.Rounds ?? 0, d?.Scans ?? 0), Now);
    }

    private async Task<ChildStats> StatsAsync(Guid childId)
    {
        var s = await db.ChildStats.FindAsync(childId);
        if (s is null) db.ChildStats.Add(s = new ChildStats { ChildId = childId });
        return s;
    }

    private static void MarkActive(ChildStats s, DateOnly day)
    {
        if (s.LastActiveDate is null) s.CurrentStreak = 1;
        else if (day == s.LastActiveDate) return;
        else if (day == s.LastActiveDate.Value.AddDays(1)) s.CurrentStreak++;
        else if (day > s.LastActiveDate) s.CurrentStreak = 1;
        else return; // sự kiện cũ hơn ngày gần nhất (đồng bộ muộn): không làm đổi chuỗi
        s.LastActiveDate = day;
        s.BestStreak = Math.Max(s.BestStreak, s.CurrentStreak);
    }

    // ---------- Ván chơi ----------

    public async Task<SessionOutcome> RecordSessionAsync(Guid childId, SessionDto dto)
    {
        try
        {
            return await RecordSessionCoreAsync(childId, dto);
        }
        catch (DbUpdateException)
        {
            // Hai thiết bị gửi cùng lúc (tạo ChildStats lần đầu, huy hiệu, hoặc chính ván này): làm lại một lần,
            // lần này sẽ thấy dữ liệu của bên kia và trả "Duplicate" hoặc cộng tiếp đúng.
            db.ChangeTracker.Clear();
            return await RecordSessionCoreAsync(childId, dto);
        }
    }

    private async Task<SessionOutcome> RecordSessionCoreAsync(Guid childId, SessionDto dto)
    {
        var existing = await db.GameSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dto.Id);
        if (existing is not null)
        {
            if (existing.ChildId != childId) return new(SessionStatus.Conflict, "Mã ván đã được dùng", null);
            return new(SessionStatus.Duplicate, null,
                new SessionResultDto(existing.StarsEarned, true, [], await SnapshotAsync(childId)));
        }

        if (!Enum.TryParse<GameType>(dto.GameType, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            return Invalid("gameType phải là match hoặc hunt");
        var maxAnswers = type == GameType.Match ? MaxMatchAnswers : MaxHuntAnswers;
        if (dto.Answers.Count < 1 || dto.Answers.Count > maxAnswers)
            return Invalid($"Số đáp án phải từ 1 đến {maxAnswers}");

        var ids = dto.Answers.Select(a => a.WordId).Distinct().ToList();
        var known = (await db.VocabWords.Where(w => ids.Contains(w.Id)).Select(w => w.Id).ToListAsync()).ToHashSet();
        var answers = dto.Answers.Where(a => known.Contains(a.WordId)).ToList();
        if (answers.Count == 0) return Invalid("Không có từ nào hợp lệ trong ván này");

        int stars, correct, found = 0, rounds = 0;
        var perfect = false;
        if (type == GameType.Match)
        {
            correct = answers.Count(a => a.Correct);
            stars = answers.Count(a => a.Correct && a.Attempts == 1); // đúng ngay lần chọn đầu = 1 sao
            perfect = answers.Count >= MinAnswersForPerfect && stars == answers.Count;
            rounds = 1;
        }
        else
        {
            var wins = answers.Where(a => a.Correct).ToList();
            if (wins.Count == 0) return Invalid("Ván Tìm vật cần ít nhất một vật đã tìm được");
            found = correct = wins.Count;
            stars = wins.Sum(a => string.Equals(a.Via, "parent", StringComparison.OrdinalIgnoreCase) ? 1 : 2);
        }

        var startedAt = ClampAt(dto.StartedAt);
        var hourAgo = startedAt.AddHours(-1);
        if (await db.GameSessions.CountAsync(s => s.ChildId == childId && s.StartedAt > hourAgo && s.StartedAt <= startedAt) >= MaxSessionsPerHour)
            return new(SessionStatus.TooMany, "Chơi nhiều quá rồi, nghỉ một chút nhé", null);

        var session = new GameSession
        {
            Id = dto.Id, ChildId = childId, GameType = type, StartedAt = startedAt, DurationSec = dto.DurationSec,
            Questions = answers.Count, Correct = correct, StarsEarned = stars, Perfect = perfect
        };
        session.Answers.AddRange(answers.Select(a => new GameAnswer { WordId = a.WordId, Correct = a.Correct, Attempts = a.Attempts }));
        db.GameSessions.Add(session);

        var stats = await StatsAsync(childId);
        stats.Stars += stars;
        stats.Rounds += rounds;
        stats.PerfectRounds += perfect ? 1 : 0;
        stats.Found += found;
        var day = LocalDate(startedAt);
        MarkActive(stats, day);
        (await DailyAsync(childId, [day]))[day].Rounds += rounds;

        var progress = await LoadProgressAsync(childId, ids);
        foreach (var a in answers)
        {
            var p = ProgressRow(progress, childId, a.WordId);
            var firstTry = type == GameType.Match ? a.Correct && a.Attempts == 1 : a.Correct;
            p.SeenCount++;
            p.LastSeenAt = startedAt;
            if (firstTry)
            {
                p.Box = Math.Min(WordSelector.MaxBox, p.Box + 1);
                p.CorrectCount++;
                p.NextDueAt = WordSelector.NextDue(p.Box, startedAt);
            }
            else
            {
                p.Box = 1; // sai (hoặc phải thử nhiều lần) thì quay lại hộp đầu, sớm được hỏi lại
                p.WrongCount++;
                p.NextDueAt = startedAt;
            }
        }

        await db.SaveChangesAsync();
        var fresh = await EvaluateBadgesAsync(childId, stats);
        return new(SessionStatus.Ok, null,
            new SessionResultDto(stars, false, fresh.Select(ToDto).ToList(), await SnapshotAsync(childId)));
    }

    private static SessionOutcome Invalid(string error) => new(SessionStatus.Invalid, error, null);

    // ---------- Sự kiện tiến độ ----------

    public async Task<EventBatchResultDto> ApplyEventsAsync(Guid childId, IReadOnlyList<ProgressEventDto> events)
    {
        try
        {
            return await ApplyEventsCoreAsync(childId, events);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await ApplyEventsCoreAsync(childId, events);
        }
    }

    private async Task<EventBatchResultDto> ApplyEventsCoreAsync(Guid childId, IReadOnlyList<ProgressEventDto> events)
    {
        var ordered = events.Select((e, i) => (E: e, I: i, At: ClampAt(e.At))).OrderBy(x => x.At).ThenBy(x => x.I).ToList();

        var eventIds = ordered.Select(x => x.E.Id).Distinct().ToList();
        var done = (await db.ProcessedEvents.Where(p => eventIds.Contains(p.Id)).Select(p => p.Id).ToListAsync()).ToHashSet();
        var wordIds = ordered.Where(x => x.E.WordId is not null).Select(x => x.E.WordId!).Distinct().ToList();
        var known = (await db.VocabWords.Where(w => wordIds.Contains(w.Id)).Select(w => w.Id).ToListAsync()).ToHashSet();
        var progress = await LoadProgressAsync(childId, wordIds);
        var stats = await StatsAsync(childId);
        var daily = await DailyAsync(childId, ordered.Select(x => LocalDate(x.At)).Distinct());

        int applied = 0, duplicates = 0, ignored = 0;
        var seen = new HashSet<Guid>();
        foreach (var (e, _, at) in ordered)
        {
            if (done.Contains(e.Id) || !seen.Add(e.Id))
            {
                duplicates++;
                continue;
            }
            var day = LocalDate(at);
            var ok = false;
            switch (e.Type.ToLowerInvariant())
            {
                case "learned" when e.WordId is not null && known.Contains(e.WordId) && e.Learned is not null:
                {
                    var p = ProgressRow(progress, childId, e.WordId);
                    if (p.LearnedAt is null || at >= p.LearnedAt) // nhiều máy cùng bật/tắt một từ: bản mới hơn thắng
                    {
                        var was = p.Learned;
                        p.Learned = e.Learned.Value;
                        p.LearnedAt = at;
                        if (!was && p.Learned)
                        {
                            daily[day].WordsLearned++;
                            MarkActive(stats, day);
                        }
                    }
                    ok = true;
                    break;
                }
                case "scan" when e.WordId is not null && known.Contains(e.WordId):
                {
                    var p = ProgressRow(progress, childId, e.WordId);
                    p.SeenCount++;
                    p.LastSeenAt = at;
                    daily[day].Scans++;
                    MarkActive(stats, day);
                    ok = true;
                    break;
                }
                case "screentime" when e.Seconds is > 0:
                {
                    var d = daily[day];
                    d.Seconds = Math.Min(MaxSecondsPerDay, d.Seconds + Math.Min(e.Seconds.Value, MaxSecondsPerEvent));
                    ok = true;
                    break;
                }
            }
            if (!ok)
            {
                ignored++;
                continue;
            }
            db.ProcessedEvents.Add(new ProcessedEvent { Id = e.Id, ChildId = childId, ProcessedAt = Now });
            applied++;
        }

        await db.SaveChangesAsync();
        var fresh = await EvaluateBadgesAsync(childId, stats);
        return new EventBatchResultDto(applied, duplicates, ignored, fresh.Select(ToDto).ToList(), await SnapshotAsync(childId));
    }

    // ---------- Dùng chung ----------

    private async Task<Dictionary<string, ChildWordProgress>> LoadProgressAsync(Guid childId, IReadOnlyCollection<string> wordIds) =>
        await db.WordProgress.Where(p => p.ChildId == childId && wordIds.Contains(p.WordId)).ToDictionaryAsync(p => p.WordId);

    private ChildWordProgress ProgressRow(Dictionary<string, ChildWordProgress> map, Guid childId, string wordId)
    {
        if (!map.TryGetValue(wordId, out var p))
        {
            map[wordId] = p = new ChildWordProgress { ChildId = childId, WordId = wordId };
            db.WordProgress.Add(p);
        }
        return p;
    }

    private async Task<Dictionary<DateOnly, DailyActivity>> DailyAsync(Guid childId, IEnumerable<DateOnly> days)
    {
        var list = days.Distinct().ToList();
        var map = (await db.DailyActivities.Where(d => d.ChildId == childId && list.Contains(d.Date)).ToListAsync())
            .ToDictionary(d => d.Date);
        foreach (var day in list.Where(d => !map.ContainsKey(d)))
        {
            map[day] = new DailyActivity { ChildId = childId, Date = day };
            db.DailyActivities.Add(map[day]);
        }
        return map;
    }

    private async Task<List<Badge>> EvaluateBadgesAsync(Guid childId, ChildStats stats)
    {
        var learned = await db.WordProgress.CountAsync(p => p.ChildId == childId && p.Learned);
        var unlocked = (await db.ChildBadges.Where(b => b.ChildId == childId).Select(b => b.BadgeId).ToListAsync()).ToHashSet();
        var fresh = new List<Badge>();
        foreach (var badge in await db.Badges.AsNoTracking().OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToListAsync())
        {
            if (unlocked.Contains(badge.Id)) continue;
            var value = badge.Metric switch
            {
                BadgeMetrics.Stars => stats.Stars,
                BadgeMetrics.Rounds => stats.Rounds,
                BadgeMetrics.Perfect => stats.PerfectRounds,
                BadgeMetrics.Found => stats.Found,
                BadgeMetrics.Streak => stats.BestStreak,
                BadgeMetrics.Learned => learned,
                _ => 0
            };
            if (value < badge.Threshold) continue;
            db.ChildBadges.Add(new ChildBadge { ChildId = childId, BadgeId = badge.Id, UnlockedAt = Now });
            fresh.Add(badge);
        }
        if (fresh.Count > 0) await db.SaveChangesAsync();
        return fresh;
    }

    public static BadgeDto ToDto(Badge b) => new(b.Id, b.Name, b.Description, b.Emoji, b.Metric, b.Threshold);
}
