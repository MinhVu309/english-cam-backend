using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EnglishCam.Api.Data;
using EnglishCam.Api.Dtos;
using EnglishCam.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishCam.Api.Tests;

/// <summary>Thay việc gửi email: giữ lại thư cuối cùng của từng người nhận để test đọc mã OTP.</summary>
public class FakeMailSender : IMailSender
{
    private readonly ConcurrentDictionary<string, string> _last = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(string to, string subject, string body)
    {
        _last[to] = body;
        return Task.CompletedTask;
    }

    public string LastCode(string to) =>
        Regex.Match(_last[to], @"\b(\d{6})\b").Groups[1].Value;
}

/// <summary>
/// Chạy API thật trên SQL Server Docker (cổng 1434) với một database riêng cho mỗi lần chạy, xoá khi xong.
/// Cấu hình đặt bằng biến môi trường (ưu tiên hơn appsettings.json) để không bao giờ đụng DB dev.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin@123456";
    public const string Password = "Matkhau123";

    public FakeMailSender Mail { get; } = new();
    private readonly string _dbName = $"EnglishCamTest_{Guid.NewGuid():N}";

    protected virtual int ChildLinkPerMinute => 1000;

    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"englishcam-media-{Guid.NewGuid():N}");

    public ApiFactory()
    {
        void Env(string k, string v) => Environment.SetEnvironmentVariable(k, v);
        Env("ConnectionStrings__DefaultConnection",
            $"Server=localhost,1434;Database={_dbName};User Id=sa;Password=EnglishCam@123;TrustServerCertificate=True;Encrypt=False");
        Env("Jwt__Key", "test-only-jwt-key-at-least-32-characters-long!!");
        Env("Jwt__Issuer", "EnglishCamTest");
        Env("Jwt__Audience", "EnglishCamTestApp");
        Env("Seed__AdminEmail", AdminEmail);
        Env("Seed__AdminPassword", AdminPassword);
        Env("Google__ClientId", "");
        Env("Media__Root", _mediaRoot);
        Env("RateLimit__ChildLinkPerMinute", ChildLinkPerMinute.ToString());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IMailSender>();
            s.AddSingleton<IMailSender>(Mail);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
        if (Directory.Exists(_mediaRoot)) Directory.Delete(_mediaRoot, recursive: true);
    }

    /// <summary>Chạy một đoạn mã với DbContext (để chỉnh dữ liệu trực tiếp, ví dụ làm mã hết hạn).</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public static string NewEmail() => $"u{Guid.NewGuid():N}@test.com";

    public HttpClient Anonymous() => CreateClient();

    public HttpClient WithToken(string token)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>Đăng ký + nhập OTP xác minh email → phụ huynh đã đăng nhập.</summary>
    public async Task<Session> RegisterParentAsync(string? email = null, string fullName = "Phu Huynh")
    {
        email ??= NewEmail();
        var anon = Anonymous();
        (await anon.PostAsJsonAsync("/api/auth/register", new { email, password = Password, fullName })).EnsureSuccessStatusCode();
        return await VerifyAsync(email);
    }

    /// <summary>Đăng nhập 2 bước (mật khẩu rồi OTP).</summary>
    public async Task<Session> LoginAsync(string email, string password = Password)
    {
        var anon = Anonymous();
        (await anon.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = email, password })).EnsureSuccessStatusCode();
        return await VerifyAsync(email);
    }

    public Task<Session> LoginAdminAsync() => LoginAsync(AdminEmail, AdminPassword);

    private async Task<Session> VerifyAsync(string email)
    {
        var anon = Anonymous();
        var resp = await anon.PostAsJsonAsync("/api/auth/verify-otp", new { userNameOrEmail = email, code = Mail.LastCode(email) });
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResultDto>())!;
        return new Session(WithToken(auth.Token), auth, email);
    }

    /// <summary>Admin tải một file lên (nội dung giả nhưng đúng byte đầu của mp3/png).</summary>
    public static async Task<HttpResponseMessage> UploadAsync(Session admin, string path, byte[] bytes, string fileName = "f.bin")
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(bytes), "File", fileName },
            { new StringContent(path), "Path" }
        };
        return await admin.Client.PostAsync("/api/admin/media", form);
    }

    /// <summary>mp3 giả: có thẻ ID3 ở đầu và phần thân khác nhau theo seed.</summary>
    public static byte[] FakeMp3(int seed = 1, int length = 2000)
    {
        var b = new byte[length];
        new Random(seed).NextBytes(b);
        b[0] = (byte)'I'; b[1] = (byte)'D'; b[2] = (byte)'3';
        return b;
    }

    public static byte[] FakePng(int seed = 1, int length = 500)
    {
        var b = new byte[length];
        new Random(seed).NextBytes(b);
        b[0] = 0x89; b[1] = 0x50; b[2] = 0x4E; b[3] = 0x47;
        return b;
    }

    /// <summary>Phụ huynh + một bé + một thiết bị của bé, sẵn sàng chơi.</summary>
    public async Task<Kid> NewKidAsync(string nickname = "Bi")
    {
        var parent = await RegisterParentAsync();
        var childId = await CreateChildAsync(parent, nickname);
        return new Kid(parent, childId, await LinkDeviceAsync(parent, childId, $"may-{Guid.NewGuid():N}"));
    }

    /// <summary>Phụ huynh + một bé + một thiết bị của bé, sẵn sàng chơi.</summary>
    public Task<Session> AddDeviceAsync(Kid kid) => LinkDeviceAsync(kid.Parent, kid.ChildId, $"may-{Guid.NewGuid():N}");

    /// <summary>Phụ huynh tạo một bé và trả về id.</summary>
    public static async Task<Guid> CreateChildAsync(Session parent, string nickname = "Bi")
    {
        var resp = await parent.Client.PostAsJsonAsync("/api/children", new { nickname, avatarKey = "cat" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ChildDto>())!.Id;
    }

    /// <summary>Phụ huynh tạo mã liên kết, một thiết bị mới của bé đổi mã lấy token.</summary>
    public async Task<Session> LinkDeviceAsync(Session parent, Guid childId, string deviceId)
    {
        var codeResp = await parent.Client.PostAsync($"/api/children/{childId}/link-code", null);
        codeResp.EnsureSuccessStatusCode();
        var code = (await codeResp.Content.ReadFromJsonAsync<LinkCodeDto>())!.Code;
        var resp = await Anonymous().PostAsJsonAsync("/api/auth/child-link", new { code, deviceId, deviceName = deviceId });
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResultDto>())!;
        return new Session(WithToken(auth.Token), auth, "");
    }
}

public record Session(HttpClient Client, AuthResultDto Auth, string Email);

public record Kid(Session Parent, Guid ChildId, Session Device);

/// <summary>Factory giới hạn mã liên kết còn 3 lần/phút để test chống dò mã.</summary>
public class RateLimitedApiFactory : ApiFactory
{
    protected override int ChildLinkPerMinute => 3;
}
