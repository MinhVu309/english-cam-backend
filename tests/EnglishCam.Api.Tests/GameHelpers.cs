using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EnglishCam.Api.Dtos;

namespace EnglishCam.Api.Tests;

/// <summary>Dựng thân yêu cầu cho các test game/tiến độ.</summary>
public static class G
{
    /// <summary>Từ có sẵn trong seed (đều có hình minh hoạ).</summary>
    public static readonly string[] Words = ["book", "pen", "bag", "ruler", "scissors", "clock", "cup", "bottle", "chair", "table", "bowl", "spoon"];

    /// <summary>Thời điểm "trưa giờ địa phương (UTC+7)" của ngày cách hôm nay <paramref name="daysAgo"/> ngày; không vượt quá hiện tại.</summary>
    public static DateTime At(int daysAgo = 0)
    {
        var todayLocal = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var noon = todayLocal.AddDays(-daysAgo).ToDateTime(new TimeOnly(12, 0)).AddHours(-7);
        return DateTime.SpecifyKind(noon, DateTimeKind.Utc) > DateTime.UtcNow ? DateTime.UtcNow : DateTime.SpecifyKind(noon, DateTimeKind.Utc);
    }

    public static object Answer(string word, bool correct = true, int attempts = 1, string? via = null) =>
        new { wordId = word, correct, attempts, via };

    /// <summary>Ván "Nghe và chọn hình": <paramref name="firstTry"/> câu đúng ngay lần đầu, <paramref name="retried"/> câu đúng sau khi chọn sai.</summary>
    public static object Match(int firstTry, int retried = 0, Guid? id = null, DateTime? startedAt = null, object? extra = null)
    {
        var answers = new List<object>();
        for (var i = 0; i < firstTry; i++) answers.Add(Answer(Words[i % Words.Length]));
        for (var i = 0; i < retried; i++) answers.Add(Answer(Words[(firstTry + i) % Words.Length], true, 2));
        return new { id = id ?? Guid.NewGuid(), gameType = "match", startedAt = startedAt ?? DateTime.UtcNow, durationSec = 60, answers, extra };
    }

    public static object Hunt(string word, string via = "model", Guid? id = null, DateTime? startedAt = null) =>
        new { id = id ?? Guid.NewGuid(), gameType = "hunt", startedAt = startedAt ?? DateTime.UtcNow, durationSec = 20, answers = new[] { Answer(word, true, 1, via) } };

    public static async Task<SessionResultDto> PlayAsync(HttpClient kid, object session)
    {
        var r = await kid.PostAsJsonAsync("/api/games/sessions", session);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<SessionResultDto>())!;
    }

    public static object Ev(string type, string? word = null, bool? learned = null, int? seconds = null, DateTime? at = null, Guid? id = null) =>
        new { id = id ?? Guid.NewGuid(), type, wordId = word, learned, seconds, at = at ?? DateTime.UtcNow };

    public static async Task<EventBatchResultDto> SendAsync(HttpClient kid, params object[] events)
    {
        var r = await kid.PostAsJsonAsync("/api/progress/events", new { events });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<EventBatchResultDto>())!;
    }

    public static Task<ProgressSnapshotDto> MeAsync(HttpClient kid) =>
        kid.GetFromJsonAsync<ProgressSnapshotDto>("/api/progress/me")!;

    public static async Task<JsonNode> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonNode>())!;
}
