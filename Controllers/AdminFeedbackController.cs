using System.Globalization;
using System.Text;
using System.Text.Json;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Admin xem góp ý nhận diện để biết model hay nhầm gì và người dùng muốn thêm từ nào.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/feedback")]
public class AdminFeedbackController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    private const int SummaryLimit = 10;

    private static List<FeedbackCandidateDto> Candidates(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Candidate>>(json)?.Select(c => new FeedbackCandidateDto(c.label ?? "", Math.Clamp(c.p, 0, 1))).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record Candidate(string? label, double p);

    private static AdminFeedbackDto ToDto(RecognitionFeedback f) => new(f.Id, f.CreatedAt, f.UpdatedAt, f.PredictedLabel,
        f.PredictedProbability, Candidates(f.CandidatesJson), f.CorrectWordId, f.CorrectText, f.Source, f.ModelSource, f.VocabVersion);

    private IQueryable<RecognitionFeedback> Filtered(string? search, string? source, bool? unknownOnly)
    {
        var q = db.RecognitionFeedback.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(f => f.Source == source);
        if (unknownOnly == true) q = q.Where(f => f.CorrectWordId == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(f => f.CorrectText.Contains(s) || f.PredictedLabel.Contains(s) || (f.CorrectWordId != null && f.CorrectWordId.Contains(s)));
        }
        return q;
    }

    /// <summary>Danh sách góp ý, mới nhất trước. Lọc theo từ khoá, nguồn (top3/typed), hoặc chỉ những từ chưa có trong từ vựng.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminFeedbackDto>>> List(string? search, string? source, bool? unknownOnly, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = Filtered(search, source, unknownOnly);
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(f => f.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<AdminFeedbackDto>(total, page, pageSize, rows.Select(ToDto).ToList());
    }

    [HttpGet("summary")]
    public async Task<FeedbackSummaryDto> Summary()
    {
        var weekAgo = clock.GetUtcNow().UtcDateTime.AddDays(-7);
        var all = db.RecognitionFeedback.AsNoTracking();
        var total = await all.CountAsync();
        var last7 = await all.CountAsync(f => f.CreatedAt >= weekAgo);
        var same = await all.CountAsync(f => f.CorrectWordId == f.PredictedLabel);

        // "Model đoán X nhưng đúng là Y": bỏ các trường hợp model đoán đúng.
        var pairs = await all.Where(f => f.CorrectWordId == null || f.CorrectWordId != f.PredictedLabel)
            .GroupBy(f => new { f.PredictedLabel, Correct = f.CorrectWordId ?? f.CorrectText })
            .Select(g => new { g.Key.PredictedLabel, g.Key.Correct, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.PredictedLabel).Take(SummaryLimit).ToListAsync();

        // Từ người dùng gõ mà từ vựng chưa có: gợi ý nên thêm từ nào.
        var unknown = await all.Where(f => f.CorrectWordId == null)
            .GroupBy(f => f.CorrectText.ToLower())
            .Select(g => new { Text = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Text).Take(SummaryLimit).ToListAsync();

        return new FeedbackSummaryDto(total, last7, same,
            pairs.Select(p => new FeedbackPairDto(p.PredictedLabel, p.Correct, p.Count)).ToList(),
            unknown.Select(u => new FeedbackTextDto(u.Text, u.Count)).ToList());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var f = await db.RecognitionFeedback.FirstOrDefaultAsync(x => x.Id == id);
        if (f is null) return NotFound();
        db.RecognitionFeedback.Remove(f);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Xuất CSV (UTF-8 có BOM để Excel đọc đúng tiếng Việt; ô nguy hiểm được chặn CSV injection).</summary>
    [HttpGet("export.csv")]
    public async Task<IActionResult> Export()
    {
        var rows = await db.RecognitionFeedback.AsNoTracking().OrderBy(f => f.CreatedAt).Take(50_000).ToListAsync();
        var sb = new StringBuilder("created_at,predicted_label,predicted_probability,correct_word_id,correct_text,source,model_source,vocab_version\r\n");
        foreach (var f in rows)
            sb.Append($"{f.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)},{DashboardService.Cell(f.PredictedLabel)},{f.PredictedProbability.ToString("0.####", CultureInfo.InvariantCulture)},{DashboardService.Cell(f.CorrectWordId)},{DashboardService.Cell(f.CorrectText)},{f.Source},{DashboardService.Cell(f.ModelSource)},{f.VocabVersion?.ToString(CultureInfo.InvariantCulture)}\r\n");
        return File([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())], "text/csv; charset=utf-8", "recognition-feedback.csv");
    }
}
