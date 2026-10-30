using System.Security.Claims;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Controllers;

/// <summary>Game cho bé (token role Child): câu hỏi thích ứng, ghi nhận ván chơi, huy hiệu.</summary>
[ApiController]
[Authorize(Roles = Roles.Child)]
[Route("api/games")]
public class GamesController(AppDbContext db, PublishedVocab vocab, LearningService learning, TimeProvider clock) : ControllerBase
{
    private async Task<Guid?> ChildIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await db.Children.AsNoTracking().Where(c => c.UserId == userId).Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Câu hỏi "Nghe &amp; chọn hình" ưu tiên từ bé hay sai hoặc chưa gặp (Leitner). Chỉ chọn từ bản từ vựng đã xuất bản.
    /// App có thể bỏ qua và tự tạo câu hỏi khi offline.
    /// </summary>
    [HttpGet("match/questions")]
    public async Task<IActionResult> MatchQuestions(int count = 8)
    {
        if (await ChildIdAsync() is not { } childId) return Unauthorized();
        var pub = await vocab.GetAsync();
        if (pub is null) return NotFound(new { message = "Chưa có bản từ vựng nào được xuất bản" });
        var pool = pub.Words.Where(w => w.HasPicture).ToList();
        if (pool.Count < 4) return Conflict(new { message = "Cần ít nhất 4 từ có hình minh hoạ" });
        var questions = WordSelector.BuildQuestions(pool, await learning.ProgressMapAsync(childId),
            Math.Clamp(count, 1, LearningService.MaxMatchAnswers), 4, clock.GetUtcNow().UtcDateTime, Random.Shared);
        return Ok(new QuestionSetDto(pub.Version, questions));
    }

    /// <summary>Gợi ý vật cần tìm cho game "Tìm vật" (chỉ từ có nhãn model và được phép dùng cho game), ưu tiên từ bé ít thuộc.</summary>
    [HttpGet("hunt/targets")]
    public async Task<IActionResult> HuntTargets(int count = 5)
    {
        if (await ChildIdAsync() is not { } childId) return Unauthorized();
        var pub = await vocab.GetAsync();
        if (pub is null) return NotFound(new { message = "Chưa có bản từ vựng nào được xuất bản" });
        var pool = pub.Words.Where(w => w.Hunt).ToList();
        if (pool.Count == 0) return Conflict(new { message = "Chưa có từ nào dùng được cho game Tìm vật" });
        var targets = WordSelector.PickTargets(pool, await learning.ProgressMapAsync(childId),
            Math.Clamp(count, 1, 20), clock.GetUtcNow().UtcDateTime, Random.Shared);
        return Ok(new HuntTargetsDto(pub.Version, targets.Select(t => t.Id).ToList()));
    }

    /// <summary>
    /// Ghi nhận một ván. Server tự tính sao/huy hiệu từ danh sách đáp án. Gửi lại cùng <c>id</c> (mất mạng, thử lại)
    /// không bị cộng đôi: trả <c>duplicate: true</c>.
    /// </summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> RecordSession(SessionDto dto)
    {
        if (await ChildIdAsync() is not { } childId) return Unauthorized();
        var outcome = await learning.RecordSessionAsync(childId, dto);
        return outcome.Status switch
        {
            SessionStatus.Ok or SessionStatus.Duplicate => Ok(outcome.Result),
            SessionStatus.TooMany => StatusCode(StatusCodes.Status429TooManyRequests, new { message = outcome.Error }),
            SessionStatus.Conflict => Conflict(new { message = outcome.Error }),
            _ => BadRequest(new { errors = new[] { outcome.Error } })
        };
    }

    /// <summary>Danh sách huy hiệu kèm trạng thái đã mở của bé.</summary>
    [HttpGet("badges")]
    public async Task<IActionResult> Badges()
    {
        if (await ChildIdAsync() is not { } childId) return Unauthorized();
        var unlocked = await db.ChildBadges.AsNoTracking().Where(b => b.ChildId == childId)
            .ToDictionaryAsync(b => b.BadgeId, b => b.UnlockedAt);
        var badges = await db.Badges.AsNoTracking().OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToListAsync();
        return Ok(badges.Select(b => new ChildBadgeDto(b.Id, b.Name, b.Description, b.Emoji, b.Metric, b.Threshold,
            unlocked.ContainsKey(b.Id), unlocked.TryGetValue(b.Id, out var at) ? at : null)));
    }
}
