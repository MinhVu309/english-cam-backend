using System.Security.Claims;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>
/// Phụ huynh quản lý hồ sơ con của mình. Mọi thao tác theo {id} đều kiểm tra quyền sở hữu: bé không phải của mình
/// thì trả 404 như không tồn tại (không lộ id có thật). Admin xem/sửa được mọi bé.
/// </summary>
[ApiController]
[Authorize(Roles = $"{Roles.Parent},{Roles.Admin}")]
[Route("api/children")]
public class ChildrenController(AppDbContext db, ChildService children) : ControllerBase
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<ChildProfile?> GetAccessibleAsync(Guid id)
    {
        var q = db.Children.Where(c => c.Id == id);
        if (!User.IsInRole(Roles.Admin)) q = q.Where(c => c.ParentId == CurrentUserId);
        return await q.FirstOrDefaultAsync();
    }

    private Task<ChildDto> DtoOf(Guid id) =>
        ChildService.Project(db.Children.AsNoTracking().Where(c => c.Id == id), DateTime.UtcNow).FirstAsync();

    [HttpGet]
    public async Task<ActionResult<List<ChildDto>>> List() =>
        await ChildService.Project(db.Children.AsNoTracking().Where(c => c.ParentId == CurrentUserId)
            .OrderBy(c => c.CreatedAt), DateTime.UtcNow).ToListAsync();

    [HttpPost]
    public async Task<IActionResult> Create(ChildCreateDto dto)
    {
        var (child, error) = await children.CreateAsync(CurrentUserId, dto);
        if (child is null) return BadRequest(new { message = error });
        return CreatedAtAction(nameof(Get), new { id = child.Id }, await DtoOf(child.Id));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) =>
        await GetAccessibleAsync(id) is null ? NotFound() : Ok(await DtoOf(id));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, ChildUpdateDto dto)
    {
        var child = await GetAccessibleAsync(id);
        if (child is null) return NotFound();
        child.Nickname = dto.Nickname.Trim();
        child.AvatarKey = string.IsNullOrWhiteSpace(dto.AvatarKey) ? null : dto.AvatarKey.Trim();
        child.BirthYear = dto.BirthYear;
        child.IsActive = dto.IsActive; // tắt hồ sơ: token của bé chết ngay (TokenValidation kiểm tra IsActive)
        var user = await db.Users.FindAsync(child.UserId);
        if (user is not null) user.FullName = child.Nickname;
        await db.SaveChangesAsync();
        return Ok(await DtoOf(id));
    }

    [HttpPut("{id:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(Guid id, ChildSettingsDto dto)
    {
        var child = await GetAccessibleAsync(id);
        if (child is null) return NotFound();
        child.DailyLimitMinutes = dto.DailyLimitMinutes;
        child.DailyWordGoal = dto.DailyWordGoal;
        await db.SaveChangesAsync();
        return Ok(await DtoOf(id));
    }

    /// <summary>Xoá bé và toàn bộ dữ liệu của bé.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var child = await GetAccessibleAsync(id);
        if (child is null) return NotFound();
        await children.DeleteAsync(child);
        return NoContent();
    }

    /// <summary>Xoá tiến độ học của bé trên máy chủ (hồ sơ và thiết bị giữ nguyên). Các máy của bé sẽ nhận trạng thái trống ở lần đồng bộ sau.</summary>
    [HttpDelete("{id:guid}/progress")]
    public async Task<IActionResult> ResetProgress(Guid id)
    {
        if (await GetAccessibleAsync(id) is null) return NotFound();
        await children.ResetProgressAsync(id);
        return NoContent();
    }

    /// <summary>Dùng ngay trên máy của phụ huynh: chọn hồ sơ bé và nhận token role Child cho thiết bị này.</summary>
    [HttpPost("{id:guid}/session")]
    public async Task<IActionResult> StartSession(Guid id, ChildSessionRequestDto dto)
    {
        var child = await GetAccessibleAsync(id);
        if (child is null) return NotFound();
        var (result, error) = await children.IssueSessionAsync(child, dto.DeviceId, dto.DeviceName);
        return result is null ? BadRequest(new { message = error }) : Ok(result);
    }

    /// <summary>Tạo mã 8 số để bé nhập trên máy khác (<c>POST /api/auth/child-link</c>). Sống 10 phút, dùng một lần.</summary>
    [HttpPost("{id:guid}/link-code")]
    public async Task<IActionResult> CreateLinkCode(Guid id)
    {
        var child = await GetAccessibleAsync(id);
        if (child is null) return NotFound();
        if (!child.IsActive) return BadRequest(new { message = "Hồ sơ bé đang bị tắt" });
        return Ok(await children.CreateLinkCodeAsync(child));
    }

    [HttpGet("{id:guid}/sessions")]
    public async Task<IActionResult> Sessions(Guid id)
    {
        if (await GetAccessibleAsync(id) is null) return NotFound();
        var now = DateTime.UtcNow;
        return Ok(await db.ChildSessions.AsNoTracking()
            .Where(s => s.ChildId == id && !s.Revoked && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt ?? s.CreatedAt)
            .Select(s => new ChildSessionDto(s.Id, s.DeviceId, s.DeviceName, s.CreatedAt, s.LastUsedAt, s.ExpiresAt))
            .ToListAsync());
    }

    /// <summary>Thu hồi một thiết bị; các thiết bị khác của bé vẫn dùng bình thường.</summary>
    [HttpDelete("{id:guid}/sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid id, Guid sessionId)
    {
        if (await GetAccessibleAsync(id) is null) return NotFound();
        var n = await db.ChildSessions.Where(s => s.Id == sessionId && s.ChildId == id && !s.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
        return n == 0 ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/revoke-sessions")]
    public async Task<IActionResult> RevokeAll(Guid id)
    {
        if (await GetAccessibleAsync(id) is null) return NotFound();
        await db.ChildSessions.Where(s => s.ChildId == id && !s.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true));
        return NoContent();
    }
}
