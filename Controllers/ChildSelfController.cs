using System.Security.Claims;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Việc bé tự làm với token role Child: xem hồ sơ và mục tiêu/giới hạn do phụ huynh đặt.</summary>
[ApiController]
[Authorize(Roles = Roles.Child)]
[Route("api/child")]
public class ChildSelfController(AppDbContext db) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var dto = await ChildService.Project(db.Children.AsNoTracking().Where(c => c.UserId == userId), DateTime.UtcNow)
            .FirstOrDefaultAsync();
        return dto is null ? NotFound() : Ok(dto);
    }
}
