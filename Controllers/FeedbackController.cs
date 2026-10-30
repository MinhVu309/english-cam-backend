using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>
/// Góp ý khi app nhận diện sai (bé hoặc phụ huynh chọn lại hay gõ tên đúng). Chỉ nhận CHỮ, không nhận ảnh.
/// Mỗi ảnh chụp có một <c>Id</c> do app sinh: gửi lại hoặc đổi đáp án trên cùng ảnh thì cập nhật cùng một dòng.
/// </summary>
[ApiController]
[Authorize]
[Route("api/feedback")]
public class FeedbackController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>Số góp ý MỚI tối đa của một tài khoản trong một ngày (chống làm đầy bảng); sửa góp ý cũ không tính.</summary>
    public const int MaxNewPerDay = 100;

    public const int MaxTextLength = 60;

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Bỏ ký tự điều khiển, gộp khoảng trắng, cắt hai đầu. Trả null nếu rỗng hoặc quá dài.</summary>
    public static string? CleanText(string? raw)
    {
        if (raw is null) return null;
        var sb = new StringBuilder(raw.Length);
        var lastSpace = true;
        foreach (var ch in raw)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }
        var t = sb.ToString().Trim();
        return t.Length is 0 or > MaxTextLength ? null : t;
    }

    [HttpPost("recognition")]
    public async Task<IActionResult> Recognition(RecognitionFeedbackDto dto)
    {
        if (!FeedbackSources.All.Contains(dto.Source))
            return BadRequest(new { errors = new[] { $"source phải là một trong: {string.Join(", ", FeedbackSources.All)}" } });
        var text = CleanText(dto.CorrectText);
        if (text is null)
            return BadRequest(new { errors = new[] { $"Từ đúng cần từ 1 đến {MaxTextLength} ký tự" } });
        var predicted = CleanText(dto.PredictedLabel);
        if (predicted is null) return BadRequest(new { errors = new[] { "predictedLabel không hợp lệ" } });

        // Mã từ chỉ giữ nếu thật sự có trong từ vựng (không tin dữ liệu từ máy khách).
        string? wordId = null;
        if (!string.IsNullOrWhiteSpace(dto.CorrectWordId) &&
            await db.VocabWords.AnyAsync(w => w.Id == dto.CorrectWordId && !w.IsDeleted))
            wordId = dto.CorrectWordId;

        var candidates = (dto.Top3 ?? []).Select(c => new { label = c.Label.Trim(), p = Math.Round(c.Probability, 4) }).ToList();
        var json = candidates.Count == 0 ? null : JsonSerializer.Serialize(candidates);
        var now = clock.GetUtcNow().UtcDateTime;

        var existing = await db.RecognitionFeedback.FirstOrDefaultAsync(f => f.Id == dto.Id);
        if (existing is not null && existing.UserId != CurrentUserId)
            return Conflict(new { message = "Mã góp ý đã được dùng" });

        if (existing is null)
        {
            var since = now.Date;
            if (await db.RecognitionFeedback.CountAsync(f => f.UserId == CurrentUserId && f.CreatedAt >= since) >= MaxNewPerDay)
                return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Hôm nay bạn đã góp ý nhiều rồi, cảm ơn bạn! Mai gửi tiếp nhé." });
            existing = new RecognitionFeedback { Id = dto.Id, UserId = CurrentUserId, CreatedAt = now };
            db.RecognitionFeedback.Add(existing);
        }
        var updated = db.Entry(existing).State != EntityState.Added;

        existing.UpdatedAt = now;
        existing.PredictedLabel = predicted;
        existing.PredictedProbability = Math.Round(dto.PredictedProbability, 4);
        existing.CandidatesJson = json;
        existing.CorrectWordId = wordId;
        existing.CorrectText = text;
        existing.Source = dto.Source;
        existing.ModelSource = CleanText(dto.ModelSource);
        existing.VocabVersion = dto.VocabVersion;
        await db.SaveChangesAsync();
        return Ok(new FeedbackResultDto(existing.Id, updated));
    }
}
