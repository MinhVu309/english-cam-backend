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
/// Dashboard cho phụ huynh xem tiến độ con. Chỉ đọc. Bé không phải của mình thì 404 như không tồn tại (không lộ id có thật);
/// Admin xem được mọi bé.
/// </summary>
[ApiController]
[Authorize(Roles = $"{Roles.Parent},{Roles.Admin}")]
[Route("api/parent")]
public class ParentController(AppDbContext db, DashboardService dashboard) : ControllerBase
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<bool> CanSeeAsync(Guid childId) =>
        await db.Children.AnyAsync(c => c.Id == childId && (User.IsInRole(Roles.Admin) || c.ParentId == CurrentUserId));

    /// <summary>Thẻ tóm tắt của tất cả các bé của mình: từ đã học, sao, chuỗi ngày, số phút hôm nay.</summary>
    [HttpGet("overview")]
    public async Task<List<OverviewChildDto>> Overview() => await dashboard.OverviewAsync(CurrentUserId);

    /// <summary>Dashboard của một bé trong <c>days</c> ngày gần nhất (1–90, mặc định 7).</summary>
    [HttpGet("children/{id:guid}/dashboard")]
    public async Task<IActionResult> Dashboard(Guid id, int days = 7)
    {
        if (!await CanSeeAsync(id)) return NotFound();
        var result = await dashboard.BuildAsync(id, days);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Xuất CSV (UTF-8 có BOM để Excel đọc đúng tiếng Việt). <c>kind=daily</c>: hoạt động từng ngày trong <c>days</c> ngày;
    /// <c>kind=words</c>: tình trạng học từng từ.
    /// </summary>
    [HttpGet("children/{id:guid}/export.csv")]
    public async Task<IActionResult> Export(Guid id, string kind = "daily", int days = 30)
    {
        if (!await CanSeeAsync(id)) return NotFound();
        if (kind is not ("daily" or "words")) return BadRequest(new { message = "kind phải là daily hoặc words" });
        var bytes = kind == "words" ? await dashboard.WordsCsvAsync(id) : await dashboard.DailyCsvAsync(id, days);
        return bytes is null ? NotFound() : File(bytes, "text/csv; charset=utf-8", $"english-cam-{kind}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
