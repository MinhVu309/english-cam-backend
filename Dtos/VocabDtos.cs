using System.ComponentModel.DataAnnotations;

namespace EnglishCam.Api.Dtos;

public record CategoryCreateDto(
    [Required, RegularExpression(@"^[a-z][a-z0-9_]{0,39}$", ErrorMessage = "Id chỉ gồm chữ thường, số, _ (bắt đầu bằng chữ, tối đa 40 ký tự)")] string Id,
    [Required, StringLength(100)] string Vi, [Required, StringLength(100)] string En,
    [StringLength(20)] string? Emoji,
    [RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "Màu phải có dạng #RRGGBB")] string? Color,
    int? SortOrder);

public record CategoryUpdateDto(
    [Required, StringLength(100)] string Vi, [Required, StringLength(100)] string En,
    [StringLength(20)] string? Emoji,
    [RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "Màu phải có dạng #RRGGBB")] string? Color,
    int? SortOrder);

public record CategoryDto(string Id, string Vi, string En, string? Emoji, string? Color, int SortOrder, bool IsDeleted, int WordCount);

/// <summary>
/// Từ vựng khi tạo/sửa. Tên trường giống vocab.json của app. <c>Id</c> bắt buộc khi tạo (POST), bỏ qua hoặc phải khớp khi sửa (PUT).
/// </summary>
public record WordWriteDto(
    [RegularExpression(@"^[a-z][a-z0-9_]{0,39}$", ErrorMessage = "Id chỉ gồm chữ thường, số, _ (bắt đầu bằng chữ, tối đa 40 ký tự)")] string? Id,
    [StringLength(40)] string? Label,
    [Required, StringLength(100)] string En, [Required, StringLength(100)] string Vi,
    [Required, StringLength(100)] string Phonetic,
    [Required, StringLength(300)] string ExampleEn, [Required, StringLength(300)] string ExampleVi,
    [Required] string Category,
    [Required, StringLength(200)] string Audio, [StringLength(200)] string? ExampleAudio,
    [StringLength(200)] string? Image, [StringLength(20)] string? Emoji,
    bool? Hunt, int? SortOrder);

public record WordDto(string Id, string? Label, string En, string Vi, string Phonetic, string ExampleEn, string ExampleVi,
    string Category, string Audio, string? ExampleAudio, string? Image, string? Emoji, bool Hunt, int SortOrder,
    bool IsDeleted, DateTime UpdatedAt);

public record MediaDto(string Path, string Sha256, long Size, string ContentType, bool Bundled, DateTime UploadedAt);

public class MediaUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;

    /// <summary>Đường dẫn trong gói, vd. <c>audio/words/giraffe.mp3</c>, <c>audio/examples/giraffe.mp3</c>, <c>images/giraffe.png</c>.</summary>
    [Required] public string Path { get; set; } = "";
}

public record PublishDto([StringLength(500)] string? Notes);
public record ReleaseDto(int Version, string? Notes, int WordCount, DateTime CreatedAt);
public record VocabStatusDto(int? PublishedVersion, DateTime? PublishedAt, bool HasUnpublishedChanges, int ActiveWords, int ActiveCategories);
public record VocabVersionDto(int Version, string? Notes, DateTime CreatedAt);
