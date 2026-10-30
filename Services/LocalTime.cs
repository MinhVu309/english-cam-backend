namespace EnglishCam.Api.Services;

/// <summary>
/// Giờ địa phương của người dùng (mặc định UTC+7, đổi bằng <c>App:TimeZoneOffsetHours</c>). Chuỗi ngày, hoạt động theo ngày
/// và dashboard đều phải dùng cùng một cách chia ngày này, nên nằm ở một chỗ.
/// </summary>
public sealed class LocalTime(TimeProvider clock, IConfiguration cfg)
{
    private TimeSpan Zone => TimeSpan.FromHours(cfg.GetValue("App:TimeZoneOffsetHours", 7.0));

    public DateTime NowUtc => clock.GetUtcNow().UtcDateTime;
    public DateOnly Today => DateOf(NowUtc);
    public DateOnly DateOf(DateTime utc) => DateOnly.FromDateTime(utc + Zone);
}
