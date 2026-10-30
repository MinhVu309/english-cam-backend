using System.Text.Json.Nodes;
using EnglishCam.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Services;

public record PublishedVocabData(int Version, IReadOnlyList<SelectableWord> Words);

public record PublishedWord(string Id, string En, string Vi, string Category, string? Emoji, string? Image, bool Hunt);
public record PublishedCategory(string Id, string Vi, string En, string? Emoji, string? Color);
public record PublishedVocabDetail(int Version, IReadOnlyList<PublishedWord> Words, IReadOnlyList<PublishedCategory> Categories);

/// <summary>
/// Từ vựng của bản ĐÃ XUẤT BẢN gần nhất (đúng thứ app đang có), không phải bản nháp: câu hỏi chỉ được chọn
/// từ những từ mà máy của bé chắc chắn đã có, và dashboard đếm "đã học / tổng" trên đúng bộ từ đó.
/// </summary>
public class PublishedVocab(AppDbContext db)
{
    public async Task<PublishedVocabDetail?> GetDetailAsync()
    {
        var r = await db.VocabReleases.AsNoTracking().OrderByDescending(x => x.Version)
            .Select(x => new { x.Version, x.VocabJson }).FirstOrDefaultAsync();
        if (r is null) return null;
        var root = JsonNode.Parse(r.VocabJson)!;
        var words = root["words"]!.AsArray().Select(w => new PublishedWord(
            (string)w!["id"]!, (string)w["en"]!, (string)w["vi"]!, (string)w["category"]!,
            (string?)w["emoji"], (string?)w["image"], w["label"] is not null && ((bool?)w["hunt"] ?? false))).ToList();
        var cats = root["categories"]!.AsArray().Select(c => new PublishedCategory(
            (string)c!["id"]!, (string)c["vi"]!, (string)c["en"]!, (string?)c["emoji"], (string?)c["color"])).ToList();
        return new PublishedVocabDetail(r.Version, words, cats);
    }

    /// <summary>Bản rút gọn dùng để chọn câu hỏi.</summary>
    public async Task<PublishedVocabData?> GetAsync()
    {
        var d = await GetDetailAsync();
        return d is null ? null : new PublishedVocabData(d.Version,
            d.Words.Select(w => new SelectableWord(w.Id, w.Emoji is not null || w.Image is not null, w.Hunt)).ToList());
    }
}
