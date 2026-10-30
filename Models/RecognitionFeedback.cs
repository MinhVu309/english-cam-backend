namespace EnglishCam.Api.Models;

/// <summary>Cách người dùng chọn từ đúng khi sửa kết quả nhận diện.</summary>
public static class FeedbackSources
{
    /// <summary>Chọn một trong các gợi ý (top-3) của model.</summary>
    public const string Top3 = "top3";

    /// <summary>Gõ tên từ khác (có thể trùng một từ trong từ vựng, có thể là từ app chưa có).</summary>
    public const string Typed = "typed";

    public static readonly string[] All = [Top3, Typed];
}

/// <summary>
/// Góp ý khi nhận diện sai: model đoán gì, người dùng nói đó là gì. Chỉ lưu CHỮ, không bao giờ có ảnh chụp
/// (ảnh của bé không rời máy). <see cref="Id"/> do app sinh cho mỗi ảnh chụp: chọn lại trên cùng ảnh thì cập nhật dòng này
/// thay vì thêm dòng mới, và gửi lại khi mất mạng không bị đếm đôi.
/// </summary>
public class RecognitionFeedback
{
    public Guid Id { get; set; }

    /// <summary>Tài khoản gửi (bé hoặc phụ huynh). Xoá tài khoản thì xoá góp ý.</summary>
    public string UserId { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Nhãn model đoán đúng nhất và xác suất của nó.</summary>
    public string PredictedLabel { get; set; } = "";
    public double PredictedProbability { get; set; }

    /// <summary>Top-3 của model dạng JSON <c>[{"label":"hat","p":0.81}, ...]</c>.</summary>
    public string? CandidatesJson { get; set; }

    /// <summary>Mã từ trong từ vựng nếu chọn/gõ đúng một từ có sẵn; null nếu là từ app chưa có.</summary>
    public string? CorrectWordId { get; set; }

    /// <summary>Chữ người dùng chọn hoặc gõ (đã làm sạch).</summary>
    public string CorrectText { get; set; } = "";

    public string Source { get; set; } = FeedbackSources.Top3;

    /// <summary>Model đang dùng trên máy ("bundled" hoặc "pack") và phiên bản từ vựng, để biết góp ý ứng với bản nào.</summary>
    public string? ModelSource { get; set; }
    public int? VocabVersion { get; set; }
}
