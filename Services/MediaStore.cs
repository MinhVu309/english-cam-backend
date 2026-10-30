using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace EnglishCam.Api.Services;

public record StoredBlob(string Sha256, long Size, string ContentType);

/// <summary>
/// Lưu file âm thanh/ảnh theo mã băm nội dung (một file = một blob, đặt tên bằng sha256). Đường dẫn trong gói chỉ là
/// con trỏ tới blob (bảng MediaFile / manifest), nên tải lên đè cùng đường dẫn không làm hỏng bản đã xuất bản.
/// </summary>
public partial class MediaStore(IConfiguration cfg, IWebHostEnvironment env)
{
    public const long MaxAudioBytes = 1_000_000;
    public const long MaxImageBytes = 2_000_000;

    /// <summary>Quy tắc tên đoạn đường dẫn giống isSafePackPath của app: chữ, số, _ - . và không bắt đầu bằng dấu chấm.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.\-]*$")]
    private static partial Regex Segment();

    private string Root => cfg["Media:Root"] is { Length: > 0 } r ? r : Path.Combine(env.ContentRootPath, "wwwroot", "media");

    public string BlobPath(string sha256) => Path.Combine(Root, sha256);

    /// <summary>Đường dẫn hợp lệ trong gói: audio/words/*.mp3, audio/examples/*.mp3 hoặc images/*.png|jpg|jpeg|webp.</summary>
    public static string? ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 200) return "Đường dẫn rỗng hoặc dài quá 200 ký tự";
        var parts = path.Split('/');
        if (!parts.All(p => Segment().IsMatch(p))) return "Đường dẫn chỉ gồm chữ, số, _ - . và /, không có '..'";
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var isAudioDir = path.StartsWith("audio/words/") || path.StartsWith("audio/examples/");
        var isImageDir = path.StartsWith("images/");
        if (isAudioDir && parts.Length == 3 && ext == ".mp3") return null;
        if (isImageDir && parts.Length >= 2 && ext is ".png" or ".jpg" or ".jpeg" or ".webp") return null;
        return "Chỉ nhận audio/words/*.mp3, audio/examples/*.mp3 hoặc images/*.png|jpg|jpeg|webp";
    }

    public static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    /// <summary>Kiểm tra byte đầu của file có đúng loại không (không tin phần mở rộng).</summary>
    public static bool LooksLike(string contentType, ReadOnlySpan<byte> head) => contentType switch
    {
        "audio/mpeg" => head.Length >= 3 && ((head[0] == 'I' && head[1] == 'D' && head[2] == '3') || (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)),
        "image/png" => head.Length >= 4 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47,
        "image/jpeg" => head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
        "image/webp" => head.Length >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                        && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P',
        _ => false
    };

    /// <summary>Ghi file (đã kiểm tra loại và cỡ) thành blob. Trả mã băm và cỡ. Ném <see cref="InvalidDataException"/> nếu sai.</summary>
    public async Task<StoredBlob> SaveAsync(Stream input, string path)
    {
        var contentType = ContentTypeOf(path);
        var limit = contentType == "audio/mpeg" ? MaxAudioBytes : MaxImageBytes;
        Directory.CreateDirectory(Root);
        var tmp = Path.Combine(Root, $"upload-{Guid.NewGuid():N}.tmp");
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            var head = new byte[12];
            var headLen = 0;
            await using (var output = File.Create(tmp))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await input.ReadAsync(buffer)) > 0)
                {
                    if (headLen < head.Length)
                    {
                        var take = Math.Min(head.Length - headLen, n);
                        Array.Copy(buffer, 0, head, headLen, take);
                        headLen += take;
                    }
                    size += n;
                    if (size > limit) throw new InvalidDataException($"File quá lớn (tối đa {limit / 1_000_000.0:0.#} MB)");
                    hash.AppendData(buffer, 0, n);
                    await output.WriteAsync(buffer.AsMemory(0, n));
                }
            }
            if (size == 0) throw new InvalidDataException("File rỗng");
            if (!LooksLike(contentType, head.AsSpan(0, headLen)))
                throw new InvalidDataException("Nội dung file không đúng với phần mở rộng");

            var sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var dest = BlobPath(sha);
            if (File.Exists(dest)) File.Delete(tmp); else File.Move(tmp, dest);
            return new StoredBlob(sha, size, contentType);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
