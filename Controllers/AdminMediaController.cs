using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Tải lên âm thanh (mp3 ≤ 1 MB) và ảnh (png/jpg/webp ≤ 2 MB) cho từ mới. Từ tham chiếu bằng <c>path</c> đã tải lên.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/media")]
public class AdminMediaController(AppDbContext db, MediaStore store) : ControllerBase
{
    private static MediaDto ToDto(MediaFile m) => new(m.Path, m.Sha256, m.Size, m.ContentType, m.Bundled, m.UploadedAt);

    [HttpGet]
    public async Task<ActionResult<PagedResult<MediaDto>>> List(string? prefix, bool includeBundled = false, int page = 1, int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.MediaFiles.AsNoTracking().Where(m => includeBundled || !m.Bundled);
        if (!string.IsNullOrWhiteSpace(prefix)) q = q.Where(m => m.Path.StartsWith(prefix));
        var total = await q.CountAsync();
        var items = await q.OrderBy(m => m.Path).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MediaDto(m.Path, m.Sha256, m.Size, m.ContentType, m.Bundled, m.UploadedAt)).ToListAsync();
        return new PagedResult<MediaDto>(total, page, pageSize, items);
    }

    /// <summary>Tải lên cùng đường dẫn thì thay nội dung của bản nháp; bản đã xuất bản không đổi cho đến khi xuất bản lại.</summary>
    [HttpPost, Consumes("multipart/form-data"), RequestSizeLimit(3_000_000)]
    public async Task<IActionResult> Upload([FromForm] MediaUploadForm form)
    {
        var path = form.Path.Trim();
        var pathError = MediaStore.ValidatePath(path);
        if (pathError is not null) return BadRequest(new { errors = new[] { pathError } });

        StoredBlob blob;
        try
        {
            await using var stream = form.File.OpenReadStream();
            blob = await store.SaveAsync(stream, path);
        }
        catch (InvalidDataException e)
        {
            return BadRequest(new { errors = new[] { e.Message } });
        }

        var media = await db.MediaFiles.FirstOrDefaultAsync(m => m.Path == path);
        var created = media is null;
        if (created) db.MediaFiles.Add(media = new MediaFile { Path = path });
        media!.Sha256 = blob.Sha256;
        media.Size = blob.Size;
        media.ContentType = blob.ContentType;
        media.Bundled = false;
        media.UploadedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        // Từ đang dùng file này đổi nội dung nên bản nháp coi như có thay đổi chưa xuất bản.
        await db.VocabWords.Where(w => w.Audio == path || w.ExampleAudio == path || w.Image == path)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.UpdatedAt, DateTime.UtcNow));
        var dto = ToDto(media);
        return created ? Created($"/api/admin/media?prefix={Uri.EscapeDataString(path)}", dto) : Ok(dto);
    }

    /// <summary>Xoá file khỏi bản nháp. Không xoá được file còn từ dùng hoặc asset đóng gói sẵn.</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string path)
    {
        var media = await db.MediaFiles.FirstOrDefaultAsync(m => m.Path == path);
        if (media is null) return NotFound();
        if (media.Bundled) return BadRequest(new { message = "Đây là asset đóng gói sẵn trong app, không xoá ở đây" });
        var users = await db.VocabWords.Where(w => !w.IsDeleted && (w.Audio == path || w.ExampleAudio == path || w.Image == path))
            .Select(w => w.Id).ToListAsync();
        if (users.Count > 0) return Conflict(new { message = "File đang được các từ sau dùng", words = users });
        db.MediaFiles.Remove(media);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
