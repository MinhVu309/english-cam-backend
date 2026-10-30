using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Admin quản lý huy hiệu. Xoá huy hiệu thì các bé không còn thấy nó (kể cả đã mở).</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/badges")]
public class AdminBadgesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<BadgeDto>> List() =>
        await db.Badges.AsNoTracking().OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .Select(b => new BadgeDto(b.Id, b.Name, b.Description, b.Emoji, b.Metric, b.Threshold)).ToListAsync();

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var b = await db.Badges.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return b is null ? NotFound() : Ok(LearningService.ToDto(b));
    }

    private static string? Check(BadgeWriteDto d) =>
        BadgeMetrics.All.Contains(d.Metric) ? null : $"Metric phải là một trong: {string.Join(", ", BadgeMetrics.All)}";

    [HttpPost]
    public async Task<IActionResult> Create(BadgeWriteDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id)) return BadRequest(new { errors = new[] { "Thiếu id của huy hiệu" } });
        if (Check(dto) is { } error) return BadRequest(new { errors = new[] { error } });
        if (await db.Badges.AnyAsync(b => b.Id == dto.Id)) return Conflict(new { message = $"Huy hiệu \"{dto.Id}\" đã tồn tại" });
        var badge = new Badge
        {
            Id = dto.Id, Name = dto.Name.Trim(), Description = dto.Description.Trim(), Emoji = dto.Emoji.Trim(),
            Metric = dto.Metric, Threshold = dto.Threshold,
            SortOrder = dto.SortOrder ?? (await db.Badges.MaxAsync(b => (int?)b.SortOrder) ?? -1) + 1
        };
        db.Badges.Add(badge);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = badge.Id }, LearningService.ToDto(badge));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, BadgeWriteDto dto)
    {
        if (dto.Id is not null && dto.Id != id) return BadRequest(new { errors = new[] { "Không đổi được id của huy hiệu" } });
        if (Check(dto) is { } error) return BadRequest(new { errors = new[] { error } });
        var badge = await db.Badges.FirstOrDefaultAsync(b => b.Id == id);
        if (badge is null) return NotFound();
        badge.Name = dto.Name.Trim();
        badge.Description = dto.Description.Trim();
        badge.Emoji = dto.Emoji.Trim();
        badge.Metric = dto.Metric;
        badge.Threshold = dto.Threshold;
        if (dto.SortOrder is not null) badge.SortOrder = dto.SortOrder.Value;
        await db.SaveChangesAsync();
        return Ok(LearningService.ToDto(badge));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var badge = await db.Badges.FirstOrDefaultAsync(b => b.Id == id);
        if (badge is null) return NotFound();
        db.Badges.Remove(badge);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
