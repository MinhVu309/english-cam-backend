using System.ComponentModel.DataAnnotations;

namespace EnglishCam.Api.Dtos;

public record AdminCreateUserDto([Required, EmailAddress] string Email, [Required, MinLength(8)] string Password,
    [Required, StringLength(100, MinimumLength = 2)] string FullName, [Required] string Role);
public record AdminUpdateUserDto([Required, StringLength(100, MinimumLength = 2)] string FullName, [Required] string Role, bool IsActive);
public record AdminUserDto(string Id, string FullName, string Email, string Role, bool IsActive, bool EmailConfirmed,
    DateTime CreatedAt, int ChildCount);
public record PagedResult<T>(int Total, int Page, int PageSize, IReadOnlyList<T> Items);

public record AdminUserCountsDto(int Admins, int Parents, int Children, int Locked, int NewParentsLast7Days);
public record AdminChildCountsDto(int Total, int Active, int ActiveToday, int ActiveLast7Days, int LinkedDevices);
public record AdminVocabCountsDto(int? PublishedVersion, int Words, int Categories);
public record AdminActivityDto(int SessionsTotal, int SessionsLast7Days, int MinutesLast7Days, long StarsTotal);
public record AdminHardWordDto(string WordId, string En, string Vi, int Answers, int Wrong, double WrongRate, int Children);
public record AdminStatsDto(AdminUserCountsDto Users, AdminChildCountsDto Children, AdminVocabCountsDto Vocab,
    AdminActivityDto Activity, List<AdminHardWordDto> HardestWords, DateTime GeneratedAt);
