using System.ComponentModel.DataAnnotations;

namespace EnglishCam.Api.Dtos;

public record BadgeDto(string Id, string Name, string Description, string Emoji, string Metric, int Threshold);

public record ChildBadgeDto(string Id, string Name, string Description, string Emoji, string Metric, int Threshold,
    bool Unlocked, DateTime? UnlockedAt);

public record BadgeWriteDto(
    [RegularExpression(@"^[a-z][a-z0-9_]{0,39}$", ErrorMessage = "Id chỉ gồm chữ thường, số, _ (bắt đầu bằng chữ, tối đa 40 ký tự)")] string? Id,
    [Required, StringLength(60)] string Name, [Required, StringLength(200)] string Description,
    [Required, StringLength(20)] string Emoji, [Required] string Metric,
    [Range(1, 100000)] int Threshold, int? SortOrder);

/// <summary>Một đáp án trong ván. Match: <c>Attempts</c> = số lần chọn ở câu đó (đúng ngay lần đầu = 1). Hunt: <c>Via</c> = "model" hoặc "parent".</summary>
public record AnswerDto([Required, StringLength(40)] string WordId, bool Correct, [Range(1, 50)] int Attempts, [StringLength(10)] string? Via);

public record SessionDto(Guid Id, [Required] string GameType, DateTime? StartedAt, [Range(0, 36000)] int DurationSec,
    [Required] List<AnswerDto> Answers);

public record SessionResultDto(int StarsEarned, bool Duplicate, List<BadgeDto> NewBadges, ProgressSnapshotDto Progress);

public record TodayDto(int Seconds, int WordsLearned, int Rounds, int Scans);

/// <summary>Trạng thái học của bé trên máy chủ (cộng dồn từ mọi thiết bị). App dùng để đồng bộ về máy.</summary>
public record ProgressSnapshotDto(int Stars, int Rounds, int PerfectRounds, int Found, int CurrentStreak, int BestStreak,
    List<string> LearnedWordIds, List<string> BadgeIds, int DailyWordGoal, int? DailyLimitMinutes, TodayDto Today,
    DateTime ServerTime);

/// <summary>Sự kiện tiến độ. Type: learned (WordId, Learned), scan (WordId), screenTime (Seconds).</summary>
public record ProgressEventDto(Guid Id, [Required] string Type, [StringLength(40)] string? WordId, bool? Learned,
    [Range(0, 100000)] int? Seconds, DateTime? At);

public record EventBatchDto([Required, MaxLength(200)] List<ProgressEventDto> Events);

public record EventBatchResultDto(int Applied, int Duplicates, int Ignored, List<BadgeDto> NewBadges, ProgressSnapshotDto Progress);

public record QuestionDto(string WordId, List<string> OptionIds);
public record QuestionSetDto(int VocabVersion, List<QuestionDto> Questions);
public record HuntTargetsDto(int VocabVersion, List<string> WordIds);
