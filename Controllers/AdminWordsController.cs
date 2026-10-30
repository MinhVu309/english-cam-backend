using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>
/// CRUD từ vựng cho Admin. Sửa ở đây là sửa bản nháp: app chỉ thấy sau khi xuất bản (<c>POST /api/admin/vocab/publish</c>).
/// Xoá là xoá mềm (tiến độ của bé còn tham chiếu id từ).
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/words")]
public class AdminWordsController(AppDbContext db) : ControllerBase
{
    private static IQueryable<WordDto> Project(IQueryable<VocabWord> q) =>
        q.Select(w => new WordDto(w.Id, w.Label, w.En, w.Vi, w.Phonetic, w.ExampleEn, w.ExampleVi, w.CategoryId, w.Audio,
            w.ExampleAudio, w.Image, w.Emoji, w.Hunt, w.SortOrder, w.IsDeleted, w.UpdatedAt));

    [HttpGet]
    public async Task<ActionResult<PagedResult<WordDto>>> List(string? search, string? category, bool? hasLabel,
        bool includeDeleted = false, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.VocabWords.AsNoTracking().Where(w => includeDeleted || !w.IsDeleted);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(w => w.CategoryId == category);
        if (hasLabel is not null) q = q.Where(w => (w.Label != null) == hasLabel);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(w => w.Id.Contains(s) || w.En.Contains(s) || w.Vi.Contains(s));
        }
        var total = await q.CountAsync();
        var items = await Project(q.OrderBy(w => w.SortOrder).ThenBy(w => w.Id)).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<WordDto>(total, page, pageSize, items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var dto = await Project(db.VocabWords.AsNoTracking().Where(w => w.Id == id)).FirstOrDefaultAsync();
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create(WordWriteDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id)) return BadRequest(new { errors = new[] { "Thiếu id của từ" } });
        if (await db.VocabWords.AnyAsync(w => w.Id == dto.Id))
            return Conflict(new { message = $"Từ \"{dto.Id}\" đã tồn tại (nếu đã xoá thì dùng restore)" });
        var errors = await ValidateAsync(dto, dto.Id);
        if (errors.Count > 0) return BadRequest(new { errors });

        var word = new VocabWord { Id = dto.Id };
        Apply(word, dto);
        word.SortOrder = dto.SortOrder ?? (await db.VocabWords.MaxAsync(w => (int?)w.SortOrder) ?? -1) + 1;
        db.VocabWords.Add(word);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = word.Id }, await Project(db.VocabWords.Where(w => w.Id == word.Id)).FirstAsync());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, WordWriteDto dto)
    {
        if (dto.Id is not null && dto.Id != id) return BadRequest(new { errors = new[] { "Không đổi được id của từ" } });
        var word = await db.VocabWords.FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted);
        if (word is null) return NotFound();
        var errors = await ValidateAsync(dto, id);
        if (errors.Count > 0) return BadRequest(new { errors });
        Apply(word, dto);
        if (dto.SortOrder is not null) word.SortOrder = dto.SortOrder.Value;
        await db.SaveChangesAsync();
        return Ok(await Project(db.VocabWords.Where(w => w.Id == id)).FirstAsync());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var word = await db.VocabWords.FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted);
        if (word is null) return NotFound();
        word.IsDeleted = true;
        word.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Khôi phục từ đã xoá. Thất bại nếu nhãn của nó đã được gán cho từ khác hoặc chủ đề đã bị xoá.</summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var word = await db.VocabWords.FirstOrDefaultAsync(w => w.Id == id && w.IsDeleted);
        if (word is null) return NotFound();
        if (!await db.VocabCategories.AnyAsync(c => c.Id == word.CategoryId && !c.IsDeleted))
            return Conflict(new { message = $"Chủ đề \"{word.CategoryId}\" đã bị xoá, hãy khôi phục chủ đề trước" });
        if (word.Label is not null && await db.VocabWords.AnyAsync(w => w.Label == word.Label && !w.IsDeleted))
            return Conflict(new { message = $"Nhãn \"{word.Label}\" đang được từ khác dùng" });
        word.IsDeleted = false;
        word.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await Project(db.VocabWords.Where(w => w.Id == id)).FirstAsync());
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(VocabWord w, WordWriteDto d)
    {
        w.Label = Blank(d.Label);
        w.En = d.En.Trim();
        w.Vi = d.Vi.Trim();
        w.Phonetic = d.Phonetic.Trim();
        w.ExampleEn = d.ExampleEn.Trim();
        w.ExampleVi = d.ExampleVi.Trim();
        w.CategoryId = d.Category;
        w.Audio = d.Audio.Trim();
        w.ExampleAudio = Blank(d.ExampleAudio);
        w.Image = Blank(d.Image);
        w.Emoji = Blank(d.Emoji);
        w.Hunt = w.Label is not null && (d.Hunt ?? true); // game "Tìm vật" cần nhãn của model
        w.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<List<string>> ValidateAsync(WordWriteDto d, string id)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.En) || string.IsNullOrWhiteSpace(d.Vi) || string.IsNullOrWhiteSpace(d.Phonetic) ||
            string.IsNullOrWhiteSpace(d.ExampleEn) || string.IsNullOrWhiteSpace(d.ExampleVi) || string.IsNullOrWhiteSpace(d.Audio))
            errors.Add("en, vi, phonetic, exampleEn, exampleVi, audio không được để trống");

        if (!await db.VocabCategories.AnyAsync(c => c.Id == d.Category && !c.IsDeleted))
            errors.Add($"Chủ đề \"{d.Category}\" không tồn tại");

        var label = Blank(d.Label);
        if (label is not null)
        {
            if (!ModelLabels.All.Contains(label))
                errors.Add($"Nhãn \"{label}\" không thuộc danh sách model nhận ra ({ModelLabels.All.Count} nhãn, xem /api/admin/vocab/model-labels)");
            else if (await db.VocabWords.AnyAsync(w => w.Label == label && !w.IsDeleted && w.Id != id))
                errors.Add($"Nhãn \"{label}\" đã gán cho từ khác");
        }

        foreach (var (field, path) in new[] { ("audio", d.Audio), ("exampleAudio", d.ExampleAudio), ("image", d.Image) })
        {
            var p = Blank(path);
            if (p is not null && !await db.MediaFiles.AnyAsync(m => m.Path == p))
                errors.Add($"{field} \"{p}\" chưa được tải lên (POST /api/admin/media)");
        }
        return errors;
    }
}
