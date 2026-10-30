using System.Text.Json.Nodes;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace EnglishCam.Api.Controllers;

/// <summary>
/// Phát từ vựng cho app, công khai (nội dung học không nhạy cảm, nên app chạy được ngay cả khi chưa đăng nhập).
/// Luôn phát bản đã xuất bản gần nhất; sửa nháp không ảnh hưởng đến khi Admin bấm xuất bản.
/// </summary>
[ApiController]
[AllowAnonymous]
public class VocabPublicController(AppDbContext db, MediaStore media) : ControllerBase
{
    private const string Json = "application/json; charset=utf-8";

    private bool NotModified(string etag)
    {
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "no-cache"; // cho phép lưu nhưng luôn hỏi lại bằng ETag (rẻ: 304)
        return Request.Headers.IfNoneMatch.Contains(etag);
    }

    /// <summary>Toàn bộ từ vựng và chủ đề, đúng schema <c>assets/vocab/vocab.json</c> của app.</summary>
    [HttpGet("api/vocab")]
    public async Task<IActionResult> Get()
    {
        var r = await db.VocabReleases.AsNoTracking().OrderByDescending(x => x.Version)
            .Select(x => new { x.Version, x.VocabJson }).FirstOrDefaultAsync();
        if (r is null) return NotFound(new { message = "Chưa có bản từ vựng nào được xuất bản" });
        return NotModified($"\"v{r.Version}\"") ? StatusCode(304) : Content(r.VocabJson, Json);
    }

    /// <summary>Số phiên bản đang phát: app so với bản đã cài để biết có cần cập nhật không.</summary>
    [HttpGet("api/vocab/version")]
    public async Task<IActionResult> Version()
    {
        var r = await db.VocabReleases.AsNoTracking().OrderByDescending(x => x.Version)
            .Select(x => new VocabVersionDto(x.Version, x.Notes, x.CreatedAt)).FirstOrDefaultAsync();
        return r is null ? NotFound() : Ok(r);
    }

    /// <summary>
    /// "Gói từ vựng" tương thích PackUpdater của app: đặt <c>PACK_URL</c> = <c>https://host/api/pack/</c>.
    /// <c>manifest.json</c>, <c>vocab.json</c> và các file âm thanh/ảnh khai báo trong manifest.
    /// </summary>
    [HttpGet("api/pack/{**path}")]
    public async Task<IActionResult> Pack(string path)
    {
        var r = await db.VocabReleases.AsNoTracking().OrderByDescending(x => x.Version)
            .Select(x => new { x.Version, x.VocabJson, x.ManifestJson }).FirstOrDefaultAsync();
        if (r is null) return NotFound();

        if (path == "manifest.json") return NotModified($"\"m{r.Version}\"") ? StatusCode(304) : Content(r.ManifestJson, Json);
        if (path == "vocab.json") return NotModified($"\"v{r.Version}\"") ? StatusCode(304) : Content(r.VocabJson, Json);

        var file = JsonNode.Parse(r.ManifestJson)!["files"]!.AsArray()
            .FirstOrDefault(f => (string?)f!["path"] == path);
        if (file is null) return NotFound();
        var sha = (string)file["sha256"]!;
        var blob = media.BlobPath(sha);
        if (!System.IO.File.Exists(blob)) return NotFound(new { message = "File chưa có trên máy chủ" });
        Response.Headers.CacheControl = "no-cache";
        return PhysicalFile(blob, MediaStore.ContentTypeOf(path), lastModified: null,
            entityTag: new EntityTagHeaderValue($"\"{sha}\""), enableRangeProcessing: true);
    }
}
