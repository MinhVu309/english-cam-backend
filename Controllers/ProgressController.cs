using System.Security.Claims;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Tiến độ học của bé (token role Child), cộng dồn từ mọi thiết bị của bé.</summary>
[ApiController]
[Authorize(Roles = Roles.Child)]
[Route("api/progress")]
public class ProgressController(AppDbContext db, LearningService learning) : ControllerBase
{
    private async Task<Guid?> ChildIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await db.Children.AsNoTracking().Where(c => c.UserId == userId).Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
    }

    /// <summary>Trạng thái hiện tại: sao, ván, chuỗi ngày, từ đã học, huy hiệu, mục tiêu/giới hạn của bố mẹ, hoạt động hôm nay.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me() =>
        await ChildIdAsync() is { } childId ? Ok(await learning.SnapshotAsync(childId)) : Unauthorized();

    /// <summary>
    /// Gửi một lô sự kiện (tối đa 200): <c>learned</c> (đánh dấu đã học/bỏ), <c>scan</c> (nhận ra vật qua camera),
    /// <c>screenTime</c> (số giây dùng app). Mỗi sự kiện có <c>id</c> do app sinh nên gửi lại không bị áp dụng hai lần.
    /// Sự kiện sai (từ lạ, loại lạ) bị bỏ qua và đếm trong <c>ignored</c>.
    /// </summary>
    [HttpPost("events")]
    public async Task<IActionResult> Events(EventBatchDto dto)
    {
        if (await ChildIdAsync() is not { } childId) return Unauthorized();
        return Ok(await learning.ApplyEventsAsync(childId, dto.Events));
    }
}
