namespace EnglishCam.Api.Dtos;

/// <summary>Thẻ tóm tắt của một bé trong danh sách của phụ huynh.</summary>
public record OverviewChildDto(Guid Id, string Nickname, string? AvatarKey, bool IsActive, int DailyWordGoal, int? DailyLimitMinutes,
    int LearnedWords, int TotalWords, int Stars, int CurrentStreak, int MinutesToday, int WordsLearnedToday, int ActiveDevices,
    DateOnly? LastActiveDate);

public record DashboardTotalsDto(int LearnedWords, int TotalWords, int Stars, int Rounds, int PerfectRounds, int Found,
    int CurrentStreak, int BestStreak, int TotalMinutes, int? AccuracyPercent);

public record DashboardTodayDto(int Seconds, int WordsLearned, int Rounds, int Scans, bool GoalMet);

public record DashboardDayDto(DateOnly Date, int Seconds, int WordsLearned, int Rounds, int Scans);

public record DashboardCategoryDto(string Id, string Vi, string En, string? Emoji, string? Color, int Learned, int Total);

/// <summary>Từ bé hay sai: <c>AccuracyPercent</c> = đúng ngay lần đầu / tổng số lần gặp.</summary>
public record DashboardWeakWordDto(string WordId, string En, string Vi, string? Emoji, int Wrong, int Total, int AccuracyPercent);

public record DashboardBadgeDto(string Id, string Name, string Emoji, string Description, DateTime UnlockedAt);

/// <summary>Một hoạt động gần đây. Kind: "match" hoặc "hunt"; hunt có thêm từ đã tìm thấy.</summary>
public record DashboardActivityDto(DateTime At, string Kind, int Stars, int Correct, int Questions, int DurationSec,
    string? WordId, string? WordEn, string? WordVi);

public record ChildDashboardDto(Guid ChildId, string Nickname, string? AvatarKey, bool IsActive, int DailyWordGoal,
    int? DailyLimitMinutes, int Days, int VocabVersion, DashboardTotalsDto Totals, DashboardTodayDto Today,
    List<DashboardDayDto> DaysData, List<DashboardCategoryDto> Categories, List<DashboardWeakWordDto> WeakWords,
    List<DashboardBadgeDto> RecentBadges, int BadgesUnlocked, int BadgesTotal, List<DashboardActivityDto> Recent);
