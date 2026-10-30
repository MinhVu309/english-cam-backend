using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Trạng thái bản nháp, xuất bản, lịch sử các bản đã xuất bản và danh sách nhãn của model.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/vocab")]
public class AdminVocabController(AppDbContext db, VocabPublisher publisher) : ControllerBase
{
    [HttpGet("status")]
    public async Task<VocabStatusDto> Status()
    {
        var latest = await db.VocabReleases.AsNoTracking().OrderByDescending(r => r.Version)
            .Select(r => new { r.Version, r.CreatedAt }).FirstOrDefaultAsync();
        var dirty = latest is null || await db.VocabWords.AnyAsync(w => w.UpdatedAt > latest.CreatedAt)
                    || await db.VocabCategories.AnyAsync(c => c.UpdatedAt > latest.CreatedAt);
        return new VocabStatusDto(latest?.Version, latest?.CreatedAt, dirty,
            await db.VocabWords.CountAsync(w => !w.IsDeleted), await db.VocabCategories.CountAsync(c => !c.IsDeleted));
    }

    /// <summary>
    /// Đóng băng bản nháp thành phiên bản mới cho app. Kiểm tra: chủ đề tồn tại, nhãn thuộc model và không trùng,
    /// mọi file âm thanh/ảnh đã tải lên, đủ từ có hình. 400 kèm danh sách lỗi; 409 nếu không có gì thay đổi.
    /// </summary>
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(PublishDto dto)
    {
        PublishOutcome outcome;
        try
        {
            outcome = await publisher.PublishAsync(dto.Notes);
        }
        catch (DbUpdateException) // hai Admin cùng xuất bản: khoá chính Version chặn bản thứ hai
        {
            return Conflict(new { message = "Có người vừa xuất bản, hãy thử lại" });
        }
        if (outcome.Errors.Count > 0) return BadRequest(new { errors = outcome.Errors });
        if (outcome.NoChanges) return Conflict(new { message = "Không có thay đổi nào so với bản đang phát" });
        var r = outcome.Release!;
        return CreatedAtAction(nameof(Releases), new ReleaseDto(r.Version, r.Notes, r.WordCount, r.CreatedAt));
    }

    /// <summary>Kiểm tra bản nháp mà không xuất bản.</summary>
    [HttpGet("validate")]
    public async Task<IActionResult> Validate()
    {
        var errors = await publisher.ValidateDraftAsync();
        return Ok(new { valid = errors.Count == 0, errors });
    }

    [HttpGet("releases")]
    public async Task<List<ReleaseDto>> Releases() =>
        await db.VocabReleases.AsNoTracking().OrderByDescending(r => r.Version)
            .Select(r => new ReleaseDto(r.Version, r.Notes, r.WordCount, r.CreatedAt)).ToListAsync();

    /// <summary>Các nhãn model nhận ra và từ đang dùng nhãn đó (null = chưa từ nào dùng).</summary>
    [HttpGet("model-labels")]
    public async Task<IActionResult> ModelLabelList()
    {
        var used = await db.VocabWords.AsNoTracking().Where(w => !w.IsDeleted && w.Label != null)
            .ToDictionaryAsync(w => w.Label!, w => w.Id);
        return Ok(ModelLabels.All.Order().Select(l => new { label = l, usedBy = used.GetValueOrDefault(l) }));
    }
}
