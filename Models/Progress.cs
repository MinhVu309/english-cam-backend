namespace EnglishCam.Api.Models;

public static class BadgeMetrics
{
    public const string Stars = "stars";
    public const string Rounds = "rounds";
    public const string Perfect = "perfect";
    public const string Found = "found";
    public const string Learned = "learned";
    public const string Streak = "streak";

    public static readonly string[] All = [Stars, Rounds, Perfect, Found, Learned, Streak];
}

public enum GameType { Match, Hunt }

/// <summary>Định nghĩa huy hiệu: mở khoá khi chỉ số <see cref="Metric"/> của bé đạt <see cref="Threshold"/>. Admin quản lý.</summary>
public class Badge
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Emoji { get; set; } = "";
    public string Metric { get; set; } = BadgeMetrics.Stars;
    public int Threshold { get; set; }
    public int SortOrder { get; set; }
}

public class ChildBadge
{
    public Guid ChildId { get; set; }
    public string BadgeId { get; set; } = "";
    public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Tổng hợp của một bé, cộng dồn từ MỌI thiết bị của bé (server là nguồn sự thật).</summary>
public class ChildStats
{
    public Guid ChildId { get; set; }
    public int Stars { get; set; }
    public int Rounds { get; set; }
    public int PerfectRounds { get; set; }
    public int Found { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }

    /// <summary>Ngày (giờ địa phương) gần nhất bé có hoạt động học; dùng tính chuỗi ngày liên tiếp.</summary>
    public DateOnly? LastActiveDate { get; set; }
}

/// <summary>
/// Tiến độ của bé với từng từ. <see cref="Box"/> theo kiểu Leitner (1 = hay sai/mới, 5 = thuộc rồi) để chọn câu hỏi thích ứng.
/// <see cref="Learned"/> do bố mẹ/bé đánh dấu; khi nhiều máy bật/tắt cùng một từ thì bản có <see cref="LearnedAt"/> mới hơn thắng.
/// </summary>
public class ChildWordProgress
{
    public Guid ChildId { get; set; }
    public string WordId { get; set; } = "";
    public bool Learned { get; set; }
    public DateTime? LearnedAt { get; set; }
    public int SeenCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int Box { get; set; } = 1;
    public DateTime? LastSeenAt { get; set; }
    public DateTime? NextDueAt { get; set; }
}

/// <summary>Một ván chơi đã ghi nhận. <see cref="Id"/> do app sinh nên gửi lại (mất mạng, thử lại) không bị cộng đôi.</summary>
public class GameSession
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }
    public GameType GameType { get; set; }
    public DateTime StartedAt { get; set; }
    public int DurationSec { get; set; }
    public int Questions { get; set; }
    public int Correct { get; set; }
    public int StarsEarned { get; set; }
    public bool Perfect { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<GameAnswer> Answers { get; set; } = [];
}

public class GameAnswer
{
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public string WordId { get; set; } = "";
    public bool Correct { get; set; }
    public int Attempts { get; set; }
}

/// <summary>Hoạt động trong một ngày (giờ địa phương): dữ liệu cho dashboard phụ huynh và giới hạn thời gian.</summary>
public class DailyActivity
{
    public Guid ChildId { get; set; }
    public DateOnly Date { get; set; }
    public int Seconds { get; set; }
    public int WordsLearned { get; set; }
    public int Rounds { get; set; }
    public int Scans { get; set; }
}

/// <summary>Sự kiện tiến độ đã xử lý (chống áp dụng hai lần khi app gửi lại).</summary>
public class ProcessedEvent
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
