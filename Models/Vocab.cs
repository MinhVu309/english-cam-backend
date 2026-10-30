namespace EnglishCam.Api.Models;

/// <summary>Chủ đề từ vựng. Xoá mềm vì từ cũ và tiến độ của bé còn tham chiếu.</summary>
public class VocabCategory
{
    public string Id { get; set; } = "";
    public string Vi { get; set; } = "";
    public string En { get; set; } = "";
    public string? Emoji { get; set; }
    public string? Color { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Một từ. <see cref="Id"/> cũng là khoá nội bộ trong app và tên file âm thanh.
/// <see cref="Label"/> phải là một dòng trong labels.txt của model (hợp đồng model); từ không có Label chỉ để học.
/// </summary>
public class VocabWord
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }
    public string En { get; set; } = "";
    public string Vi { get; set; } = "";
    public string Phonetic { get; set; } = "";
    public string ExampleEn { get; set; } = "";
    public string ExampleVi { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public VocabCategory? Category { get; set; }

    /// <summary>`assets/...` = file đã đóng gói trong APK; đường dẫn tương đối = file trong gói tải về (xem <see cref="MediaFile"/>).</summary>
    public string Audio { get; set; } = "";
    public string? ExampleAudio { get; set; }
    public string? Image { get; set; }
    public string? Emoji { get; set; }

    /// <summary>Có dùng làm đích của game "Tìm vật" không. Chỉ có nghĩa khi có <see cref="Label"/>.</summary>
    public bool Hunt { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// File âm thanh/ảnh mà từ vựng tham chiếu. <see cref="Bundled"/>: đã nằm trong APK (đường dẫn `assets/...`), server không giữ file.
/// Ngược lại nội dung lưu theo mã băm (<see cref="Sha256"/>) nên đã xuất bản rồi thì tải lên đè cùng đường dẫn cũng không đổi bản đã phát.
/// </summary>
public class MediaFile
{
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string ContentType { get; set; } = "";
    public bool Bundled { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Một bản từ vựng đã xuất bản, bất biến: vocab.json và manifest.json đã sinh sẵn (đúng từng byte mà app sẽ băm).
/// Bản có Version lớn nhất là bản đang phát cho app.
/// </summary>
public class VocabRelease
{
    public int Version { get; set; }
    public string? Notes { get; set; }
    public string VocabJson { get; set; } = "";
    public string ManifestJson { get; set; } = "";
    public int WordCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
