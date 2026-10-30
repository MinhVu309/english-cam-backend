using System.Security.Claims;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Admin quản lý tài khoản Admin/Parent. Tài khoản của bé đi qua /api/children, không sửa ở đây.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/users")]
public class AdminUsersController(AppDbContext db, UserManager<AppUser> users, ChildService children) : ControllerBase
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static bool IsStaffRole(string role) => role is Roles.Admin or Roles.Parent;

    private IQueryable<AdminUserDto> Query(string? search, string? role, string? id = null)
    {
        var q = from u in db.Users
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                select new { User = u, Role = r.Name! };
        if (id is not null) q = q.Where(x => x.User.Id == id); // lọc theo id TRƯỚC khi chiếu sang DTO (EF không dịch được sau đó)
        q = string.IsNullOrEmpty(role) ? q.Where(x => x.Role != Roles.Child) : q.Where(x => x.Role == role);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.User.Email!.Contains(s) || x.User.FullName.Contains(s));
        }
        return q.OrderByDescending(x => x.User.CreatedAt).Select(x => new AdminUserDto(
            x.User.Id, x.User.FullName, x.User.Email ?? "", x.Role, x.User.IsActive, x.User.EmailConfirmed,
            x.User.CreatedAt, db.Children.Count(c => c.ParentId == x.User.Id)));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> List(string? search, string? role, int page = 1, int pageSize = 20)
    {
        if (role is not null && !Roles.All.Contains(role)) return BadRequest(new { message = "Role không hợp lệ" });
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = Query(search, role);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<AdminUserDto>(total, page, pageSize, items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var dto = await Query(null, null, id).FirstOrDefaultAsync();
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create(AdminCreateUserDto dto)
    {
        if (!IsStaffRole(dto.Role)) return BadRequest(new { message = "Role chỉ có thể là Admin hoặc Parent" });
        if (dto.Email.EndsWith("@" + ChildService.FakeEmailDomain, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Email không hợp lệ" });
        var user = new AppUser { UserName = dto.Email, Email = dto.Email, FullName = dto.FullName.Trim(), EmailConfirmed = true };
        var result = await users.CreateAsync(user, dto.Password);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        await users.AddToRoleAsync(user, dto.Role);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, await Query(null, null, user.Id).FirstAsync());
    }

    /// <summary>
    /// Sửa tên, đổi role (Admin/Parent), khoá/mở. Đổi role làm SecurityStamp đổi nên người đó phải đăng nhập lại;
    /// khoá tài khoản có hiệu lực ngay. Không tự hạ quyền hay tự khoá chính mình.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, AdminUpdateUserDto dto)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var current = (await users.GetRolesAsync(user)).FirstOrDefault();
        if (current == Roles.Child) return BadRequest(new { message = "Tài khoản của bé được quản lý qua hồ sơ con" });
        if (!IsStaffRole(dto.Role)) return BadRequest(new { message = "Role chỉ có thể là Admin hoặc Parent" });
        if (id == CurrentUserId && (dto.Role != current || !dto.IsActive))
            return BadRequest(new { message = "Không thể tự đổi role hoặc tự khoá chính mình" });

        user.FullName = dto.FullName.Trim();
        user.IsActive = dto.IsActive;
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded) return BadRequest(new { errors = update.Errors.Select(e => e.Description) });
        if (dto.Role != current)
        {
            if (current is not null) await users.RemoveFromRoleAsync(user, current);
            await users.AddToRoleAsync(user, dto.Role);
            await users.UpdateSecurityStampAsync(user);
        }
        return Ok(await Query(null, null, id).FirstAsync());
    }

    /// <summary>Xoá tài khoản. Xoá phụ huynh thì xoá luôn các bé của họ. Không tự xoá chính mình.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (id == CurrentUserId) return BadRequest(new { message = "Không thể tự xoá chính mình" });
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (await users.IsInRoleAsync(user, Roles.Child))
            return BadRequest(new { message = "Tài khoản của bé được quản lý qua hồ sơ con" });

        await children.DeleteAllOfParentAsync(id);
        var result = await users.DeleteAsync(user);
        return result.Succeeded ? NoContent() : BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }
}
