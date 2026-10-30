using System.Text.Json.Nodes;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Data;

/// <summary>
/// Lần chạy đầu (DB chưa có từ nào): nạp vocab.json đang đóng gói trong app (Data/Seed/vocab.json) và xuất bản thành bản 1.
/// Giữ nguyên đường dẫn `assets/...` nên không phải tải lại âm thanh và ảnh.
/// </summary>
public static class VocabSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        if (await db.VocabWords.AnyAsync() || await db.VocabCategories.AnyAsync()) return;

        var root = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "vocab.json")))!;
        var now = DateTime.UtcNow;
        var order = 0;
        foreach (var c in root["categories"]!.AsArray())
            db.VocabCategories.Add(new VocabCategory
            {
                Id = (string)c!["id"]!, Vi = (string)c["vi"]!, En = (string)c["en"]!,
                Emoji = (string?)c["emoji"], Color = (string?)c["color"], SortOrder = order++, UpdatedAt = now
            });

        order = 0;
        var bundled = new HashSet<string>();
        foreach (var w in root["words"]!.AsArray())
        {
            var label = (string?)w!["label"];
            var word = new VocabWord
            {
                Id = (string)w["id"]!, Label = label, En = (string)w["en"]!, Vi = (string)w["vi"]!,
                Phonetic = (string)w["phonetic"]!, ExampleEn = (string)w["exampleEn"]!, ExampleVi = (string)w["exampleVi"]!,
                CategoryId = (string)w["category"]!, Audio = (string)w["audio"]!, ExampleAudio = (string?)w["exampleAudio"],
                Image = (string?)w["image"], Emoji = (string?)w["emoji"],
                Hunt = label is not null && ((bool?)w["hunt"] ?? true), SortOrder = order++, CreatedAt = now, UpdatedAt = now
            };
            db.VocabWords.Add(word);
            foreach (var p in new[] { word.Audio, word.ExampleAudio, word.Image })
                if (p is not null && p.StartsWith("assets/")) bundled.Add(p);
        }
        foreach (var p in bundled.Order())
            db.MediaFiles.Add(new MediaFile { Path = p, Bundled = true, ContentType = MediaStore.ContentTypeOf(p), UploadedAt = now });
        await db.SaveChangesAsync();

        var result = await new VocabPublisher(db).PublishAsync($"Bản đầu tiên ({order} từ)");
        if (result.Release is null)
            throw new InvalidOperationException("Không xuất bản được bản từ vựng đầu tiên: " + string.Join("; ", result.Errors));
    }
}
