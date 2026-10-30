using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCam.Api.Controllers;

/// <summary>Thống kê toàn hệ thống cho Admin: người dùng theo role, số bé hoạt động, từ bị sai nhiều nhất.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/stats")]
public class AdminStatsController(AdminStatsService stats) : ControllerBase
{
    [HttpGet]
    public async Task<AdminStatsDto> Get() => await stats.BuildAsync();
}
