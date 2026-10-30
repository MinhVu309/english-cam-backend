using System.ComponentModel.DataAnnotations;

namespace EnglishCam.Api.Dtos;

public record FeedbackCandidateDto([Required, StringLength(40)] string Label, [Range(0, 1)] double Probability);

/// <summary>Góp ý nhận diện từ app. <c>Id</c> do app sinh theo từng ảnh chụp (gửi lại hay sửa đáp án thì cập nhật cùng dòng).</summary>
public record RecognitionFeedbackDto(
    Guid Id,
    [Required, StringLength(40, MinimumLength = 1)] string PredictedLabel,
    [Range(0, 1)] double PredictedProbability,
    [MaxLength(5)] List<FeedbackCandidateDto>? Top3,
    [StringLength(40)] string? CorrectWordId,
    [Required, StringLength(100, MinimumLength = 1)] string CorrectText,
    [Required] string Source,
    [StringLength(20)] string? ModelSource,
    int? VocabVersion);

public record FeedbackResultDto(Guid Id, bool Updated);

public record AdminFeedbackDto(Guid Id, DateTime CreatedAt, DateTime UpdatedAt, string PredictedLabel, double PredictedProbability,
    List<FeedbackCandidateDto> Top3, string? CorrectWordId, string CorrectText, string Source, string? ModelSource, int? VocabVersion);

public record FeedbackPairDto(string PredictedLabel, string Correct, int Count);
public record FeedbackTextDto(string Text, int Count);

/// <summary>Tổng hợp góp ý cho Admin: tổng số, 7 ngày, cặp "model đoán → đúng là" hay gặp, và từ lạ người dùng gõ.</summary>
public record FeedbackSummaryDto(int Total, int Last7Days, int SameAsPrediction, List<FeedbackPairDto> TopConfusions,
    List<FeedbackTextDto> UnknownWords);
