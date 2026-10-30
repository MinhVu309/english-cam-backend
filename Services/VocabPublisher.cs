using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnglishCam.Api.Data;
using EnglishCam.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Services;

public record PublishOutcome(VocabRelease? Release, List<string> Errors, bool NoChanges);

/// <summary>
/// Biến bản nháp trong DB thành một bản phát hành bất biến: vocab.json (đúng schema assets/vocab/vocab.json của app)
/// + manifest.json (sha256/size của từng file theo đúng cách PackUpdater trong app kiểm tra).
/// </summary>
public class VocabPublisher(AppDbContext db)
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // giữ tiếng Việt và emoji nguyên dạng, không \uXXXX
    };

    public const int MinPicturedWords = 4;
    public const int MaxPackFiles = 2000;

    public static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Kiểm tra toàn bộ bản nháp theo các luật mà app sẽ kiểm tra khi nạp vocab.json.</summary>
    public async Task<List<string>> ValidateDraftAsync()
    {
        var errors = new List<string>();
        var cats = await db.VocabCategories.AsNoTracking().Where(c => !c.IsDeleted).ToListAsync();
        var words = await db.VocabWords.AsNoTracking().Where(w => !w.IsDeleted).ToListAsync();
        var media = (await db.MediaFiles.AsNoTracking().Select(m => m.Path).ToListAsync()).ToHashSet();
        var catIds = cats.Select(c => c.Id).ToHashSet();

        if (cats.Count == 0) errors.Add("Chưa có chủ đề nào");
        var labels = new HashSet<string>();
        foreach (var w in words)
        {
            if (!catIds.Contains(w.CategoryId)) errors.Add($"[{w.Id}] chủ đề \"{w.CategoryId}\" không tồn tại hoặc đã bị xoá");
            if (w.Label is not null)
            {
                if (!ModelLabels.All.Contains(w.Label)) errors.Add($"[{w.Id}] nhãn \"{w.Label}\" không thuộc danh sách của model");
                if (!labels.Add(w.Label)) errors.Add($"[{w.Id}] nhãn \"{w.Label}\" bị trùng");
            }
            foreach (var (field, path) in new[] { ("audio", w.Audio), ("exampleAudio", w.ExampleAudio), ("image", w.Image) })
                if (path is not null && !media.Contains(path)) errors.Add($"[{w.Id}] {field} \"{path}\" chưa được tải lên");
        }
        if (words.Count(w => w.Image is not null || w.Emoji is not null) < MinPicturedWords)
            errors.Add($"Cần ít nhất {MinPicturedWords} từ có ảnh hoặc emoji (cho game Nghe & chọn hình)");
        return errors;
    }

    private static JsonObject WordNode(VocabWord w)
    {
        var o = new JsonObject { ["id"] = w.Id };
        if (w.Label is not null) o["label"] = w.Label;
        o["en"] = w.En;
        o["vi"] = w.Vi;
        o["phonetic"] = w.Phonetic;
        o["exampleEn"] = w.ExampleEn;
        o["exampleVi"] = w.ExampleVi;
        o["emoji"] = w.Emoji;
        o["image"] = w.Image;
        o["audio"] = w.Audio;
        o["exampleAudio"] = w.ExampleAudio;
        o["category"] = w.CategoryId;
        if (w.Label is not null) o["hunt"] = w.Hunt;
        return o;
    }

    /// <summary>Xuất bản. Trả lỗi nếu bản nháp không hợp lệ, hoặc <c>NoChanges</c> nếu giống hệt bản đang phát.</summary>
    public async Task<PublishOutcome> PublishAsync(string? notes)
    {
        var errors = await ValidateDraftAsync();
        if (errors.Count > 0) return new PublishOutcome(null, errors, false);

        var cats = await db.VocabCategories.AsNoTracking().Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync();
        var words = await db.VocabWords.AsNoTracking().Where(w => !w.IsDeleted).OrderBy(w => w.SortOrder).ThenBy(w => w.Id).ToListAsync();

        var vocab = new JsonObject
        {
            ["categories"] = new JsonArray(cats.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id, ["vi"] = c.Vi, ["en"] = c.En, ["emoji"] = c.Emoji, ["color"] = c.Color
            }).ToArray()),
            ["words"] = new JsonArray(words.Select(w => (JsonNode)WordNode(w)).ToArray())
        };
        var vocabJson = vocab.ToJsonString(Pretty);

        // Chỉ file thật sự nằm trong gói (không phải asset đóng gói sẵn trong APK) mới có mặt trong manifest.
        var used = words.SelectMany(w => new[] { w.Audio, w.ExampleAudio, w.Image }).Where(p => p is not null).Select(p => p!).ToHashSet();
        var packFiles = await db.MediaFiles.AsNoTracking().Where(m => !m.Bundled && used.Contains(m.Path))
            .OrderBy(m => m.Path).ToListAsync();
        if (packFiles.Count + 1 > MaxPackFiles) return new PublishOutcome(null, [$"Gói có quá {MaxPackFiles} file"], false);

        var latest = await db.VocabReleases.AsNoTracking().OrderByDescending(r => r.Version)
            .Select(r => new { r.Version, r.VocabJson, r.ManifestJson }).FirstOrDefaultAsync();
        var version = (latest?.Version ?? 0) + 1;

        var files = new JsonArray { new JsonObject { ["path"] = "vocab.json", ["sha256"] = Sha256Hex(vocabJson), ["size"] = Encoding.UTF8.GetByteCount(vocabJson) } };
        foreach (var m in packFiles)
            files.Add(new JsonObject { ["path"] = m.Path, ["sha256"] = m.Sha256, ["size"] = m.Size });

        // Giống bản đang phát (cùng nội dung, cùng danh sách file) thì không xuất bản thêm.
        if (latest is not null && latest.VocabJson == vocabJson && FilesOf(latest.ManifestJson) == files.ToJsonString())
            return new PublishOutcome(null, [], true);

        var manifest = new JsonObject { ["version"] = version };
        if (!string.IsNullOrWhiteSpace(notes)) manifest["notes"] = notes.Trim();
        manifest["files"] = files;

        var release = new VocabRelease
        {
            Version = version, Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            VocabJson = vocabJson, ManifestJson = manifest.ToJsonString(Pretty), WordCount = words.Count
        };
        db.VocabReleases.Add(release);
        await db.SaveChangesAsync();
        return new PublishOutcome(release, [], false);
    }

    private static string FilesOf(string manifestJson) =>
        JsonNode.Parse(manifestJson)!["files"]!.ToJsonString();
}
