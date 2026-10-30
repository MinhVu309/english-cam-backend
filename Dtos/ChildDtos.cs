using System.ComponentModel.DataAnnotations;

namespace EnglishCam.Api.Dtos;

public record ChildCreateDto([Required, StringLength(30, MinimumLength = 1)] string Nickname,
    [StringLength(30)] string? AvatarKey, [Range(2000, 2100)] int? BirthYear);
public record ChildUpdateDto([Required, StringLength(30, MinimumLength = 1)] string Nickname,
    [StringLength(30)] string? AvatarKey, [Range(2000, 2100)] int? BirthYear, bool IsActive);
public record ChildSettingsDto([Range(5, 600)] int? DailyLimitMinutes, [Range(1, 50)] int DailyWordGoal);

public record ChildDto(Guid Id, string Nickname, string? AvatarKey, int? BirthYear, int? DailyLimitMinutes,
    int DailyWordGoal, bool IsActive, int ActiveDevices, DateTime? LastActiveAt, DateTime CreatedAt);

public record ChildSessionRequestDto([Required, MaxLength(200)] string DeviceId, [MaxLength(100)] string? DeviceName);
public record ChildLinkRequestDto([Required, MaxLength(20)] string Code, [Required, MaxLength(200)] string DeviceId,
    [MaxLength(100)] string? DeviceName);
public record LinkCodeDto(string Code, DateTime ExpiresAt);
public record ChildSessionDto(Guid Id, string DeviceId, string DeviceName, DateTime CreatedAt, DateTime? LastUsedAt, DateTime ExpiresAt);
