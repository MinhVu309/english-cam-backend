using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Admin quản lý chủ đề (bản nháp; app chỉ thấy sau khi xuất bản). Xoá là xoá mềm.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/categories")]
public class AdminCategoriesController(AppDbContext db) : ControllerBase
{
    private IQueryable<CategoryDto> Project(IQueryable<VocabCategory> q) =>
        q.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => new CategoryDto(c.Id, c.Vi, c.En, c.Emoji, c.Color,
            c.SortOrder, c.IsDeleted, db.VocabWords.Count(w => w.CategoryId == c.Id && !w.IsDeleted)));

    [HttpGet]
    public async Task<List<CategoryDto>> List(bool includeDeleted = false) =>
        await Project(db.VocabCategories.AsNoTracking().Where(c => includeDeleted || !c.IsDeleted)).ToListAsync();

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var dto = await Project(db.VocabCategories.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync();
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CategoryCreateDto dto)
    {
        if (await db.VocabCategories.AnyAsync(c => c.Id == dto.Id))
            return Conflict(new { message = $"Chủ đề \"{dto.Id}\" đã tồn tại (nếu đã xoá thì dùng restore)" });
        db.VocabCategories.Add(new VocabCategory
        {
            Id = dto.Id, Vi = dto.Vi.Trim(), En = dto.En.Trim(), Emoji = Blank(dto.Emoji), Color = Blank(dto.Color),
            SortOrder = dto.SortOrder ?? (await db.VocabCategories.MaxAsync(c => (int?)c.SortOrder) ?? -1) + 1
        });
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, await Project(db.VocabCategories.Where(c => c.Id == dto.Id)).FirstAsync());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, CategoryUpdateDto dto)
    {
        var c = await db.VocabCategories.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (c is null) return NotFound();
        c.Vi = dto.Vi.Trim();
        c.En = dto.En.Trim();
        c.Emoji = Blank(dto.Emoji);
        c.Color = Blank(dto.Color);
        if (dto.SortOrder is not null) c.SortOrder = dto.SortOrder.Value;
        c.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await Project(db.VocabCategories.Where(x => x.Id == id)).FirstAsync());
    }

    /// <summary>Không xoá được chủ đề còn từ (chuyển hoặc xoá các từ trước).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var c = await db.VocabCategories.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (c is null) return NotFound();
        var words = await db.VocabWords.CountAsync(w => w.CategoryId == id && !w.IsDeleted);
        if (words > 0) return Conflict(new { message = $"Chủ đề còn {words} từ, hãy chuyển hoặc xoá các từ đó trước", wordCount = words });
        c.IsDeleted = true;
        c.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var c = await db.VocabCategories.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (c is null) return NotFound();
        c.IsDeleted = false;
        c.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await Project(db.VocabCategories.Where(x => x.Id == id)).FirstAsync());
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
