using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Mail;

/// <summary>
/// SMTP によるメール送信（設計書 5.1節。オンプレ版の既定。Microsoft 365 / Google Workspace 等を想定）。
/// </summary>
public sealed class SmtpMailSender(
    IOptions<MailSettings> mailOptions,
    ILogger<SmtpMailSender> logger) : MailSenderBase
{
    /// <inheritdoc />
    public override MailProviderType ProviderType => MailProviderType.Smtp;

    /// <inheritdoc />
    protected override async Task SendCoreAsync(string to, string subject, string plainTextBody, MailAttachment? attachment)
    {
        var settings = mailOptions.Value.Smtp;
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            throw new InvalidOperationException("SMTPサーバー（Mail:Smtp:Host）が設定されていません。");
        }

        var fromName = string.IsNullOrWhiteSpace(settings.FromName) ? settings.User : settings.FromName;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, settings.User));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            TextBody = plainTextBody,
            HtmlBody = MailTemplates.ToHtmlBody(plainTextBody),
        };

        if (attachment is { } file)
        {
            bodyBuilder.Attachments.Add(file.FileName, file.Content, ContentType.Parse(file.ContentType));
        }

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            // 設計書 11.2節：通信は暗号化する。587番ポートは STARTTLS、465番は暗黙的SSLを使用する
            var socketOptions = settings.EnableSsl
                ? (settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.None;

            await client.ConnectAsync(settings.Host, settings.Port, socketOptions).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(settings.User))
            {
                await client.AuthenticateAsync(settings.User, settings.Password).ConfigureAwait(false);
            }

            await client.SendAsync(message).ConfigureAwait(false);
            logger.LogInformation("SMTP によりメールを送信しました。宛先: {To}", to);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true).ConfigureAwait(false);
            }
        }
    }
}
