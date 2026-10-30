using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;

namespace EnglishCam.Api.Services;

/// <summary>Một từ trong bản từ vựng đang phát cho app (đủ thông tin để chọn câu hỏi).</summary>
public record SelectableWord(string Id, bool HasPicture, bool Hunt);

/// <summary>
/// Chọn từ kiểu Leitner: từ bé hay sai hoặc chưa gặp được ưu tiên, từ đã thuộc (hộp cao) và chưa tới hạn ôn thì ít xuất hiện.
/// Thuần tuý (không đụng DB, nhận Random) để test được.
/// </summary>
public static class WordSelector
{
    public const int MaxBox = 5;

    /// <summary>Số ngày chờ trước khi ôn lại, theo hộp (chỉ số 1..5).</summary>
    private static readonly int[] IntervalDays = [0, 0, 1, 2, 5, 10];

    public static DateTime NextDue(int box, DateTime at) => at.AddDays(IntervalDays[Math.Clamp(box, 1, MaxBox)]);

    /// <summary>Trọng số chọn: chưa gặp = 4; hộp 1..5 = 5,4,3,2,1; chưa tới hạn ôn thì nhân 0,4.</summary>
    public static double Weight(ChildWordProgress? p, DateTime now)
    {
        if (p is null || p.SeenCount == 0) return 4;
        double w = Math.Clamp(p.Box, 1, MaxBox) switch { 1 => 5, 2 => 4, 3 => 3, 4 => 2, _ => 1 };
        var due = p.NextDueAt is null || p.NextDueAt <= now;
        return due ? w : w * 0.4;
    }

    /// <summary>Chọn <paramref name="count"/> từ khác nhau theo trọng số (lấy mẫu có trọng số không hoàn lại, Efraimidis–Spirakis).</summary>
    public static List<SelectableWord> PickTargets(IReadOnlyList<SelectableWord> pool,
        IReadOnlyDictionary<string, ChildWordProgress> progress, int count, DateTime now, Random rng)
    {
        return pool
            .Select(w => (Word: w, Key: Math.Pow(rng.NextDouble(), 1.0 / Weight(progress.GetValueOrDefault(w.Id), now))))
            .OrderByDescending(x => x.Key)
            .Take(Math.Min(count, pool.Count))
            .Select(x => x.Word)
            .ToList();
    }

    /// <summary>
    /// Câu hỏi "Nghe &amp; chọn hình": mỗi câu có đáp án đúng cộng (<paramref name="optionCount"/> − 1) đáp án nhiễu khác nhau,
    /// vị trí xáo trộn. Nếu cần nhiều câu hơn số từ thì chọn thêm một lượt nữa.
    /// </summary>
    public static List<QuestionDto> BuildQuestions(IReadOnlyList<SelectableWord> pool,
        IReadOnlyDictionary<string, ChildWordProgress> progress, int count, int optionCount, DateTime now, Random rng)
    {
        var questions = new List<QuestionDto>();
        while (questions.Count < count)
        {
            foreach (var target in PickTargets(pool, progress, count - questions.Count, now, rng))
            {
                var options = pool.Where(w => w.Id != target.Id).OrderBy(_ => rng.Next()).Take(optionCount - 1)
                    .Select(w => w.Id).Append(target.Id).OrderBy(_ => rng.Next()).ToList();
                questions.Add(new QuestionDto(target.Id, options));
            }
        }
        return questions;
    }
}
