using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EnglishCam.Api.Services;

/// <summary>Tách giao diện gửi thư để test thay bằng bản giả lấy mã OTP.</summary>
public interface IMailSender
{
    Task SendAsync(string to, string subject, string body);
}

public class EmailService(IConfiguration cfg, ILogger<EmailService> log) : IMailSender
{
    public async Task SendAsync(string to, string subject, string body)
    {
        var host = cfg["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            // Chưa cấu hình SMTP (chạy dev): in nội dung ra log để test.
            log.LogWarning("SMTP chưa cấu hình. Email tới {To}: {Subject} | {Body}", to, subject, body);
            return;
        }

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("English Cam", cfg["Smtp:From"] ?? cfg["Smtp:User"] ?? ""));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(host, int.Parse(cfg["Smtp:Port"] ?? "587"), SecureSocketOptions.StartTls);
        await smtp.AuthenticateAsync(cfg["Smtp:User"] ?? "", cfg["Smtp:Password"] ?? "");
        await smtp.SendAsync(msg);
        await smtp.DisconnectAsync(true);
    }
}
