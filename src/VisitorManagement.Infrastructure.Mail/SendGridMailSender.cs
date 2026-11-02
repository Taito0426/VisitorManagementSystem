using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using VisitorManagement.Core.Enums;
// SendGrid.Helpers.Mail にも MailSettings が存在するため、別名を付けて曖昧参照を避ける
using MailSettings = VisitorManagement.Core.Settings.MailSettings;

namespace VisitorManagement.Infrastructure.Mail;

/// <summary>
/// SendGrid によるメール送信（設計書 5.1節。クラウド版の既定）。
/// </summary>
public sealed class SendGridMailSender(
    IOptions<MailSettings> mailOptions,
    ILogger<SendGridMailSender> logger) : MailSenderBase
{
    /// <inheritdoc />
    public override MailProviderType ProviderType => MailProviderType.SendGrid;

    /// <inheritdoc />
    protected override async Task SendCoreAsync(string to, string subject, string plainTextBody, MailAttachment? attachment)
    {
        var settings = mailOptions.Value.SendGrid;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("SendGrid の APIキー（Mail:SendGrid:ApiKey）が設定されていません。");
        }

        var message = new SendGridMessage
        {
            From = new EmailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            PlainTextContent = plainTextBody,
            HtmlContent = MailTemplates.ToHtmlBody(plainTextBody),
        };
        message.AddTo(new EmailAddress(to));

        if (attachment is { } file)
        {
            message.AddAttachment(file.FileName, Convert.ToBase64String(file.Content), file.ContentType);
        }

        var client = new SendGridClient(settings.ApiKey);
        var response = await client.SendEmailAsync(message).ConfigureAwait(false);

        if (!IsSuccessStatusCode(response))
        {
            var detail = await response.Body.ReadAsStringAsync().ConfigureAwait(false);
            logger.LogError("SendGrid によるメール送信に失敗しました。宛先: {To} / ステータス: {Status}", to, response.StatusCode);
            throw new InvalidOperationException($"SendGrid によるメール送信に失敗しました（{(int)response.StatusCode}）: {detail}");
        }

        logger.LogInformation("SendGrid によりメールを送信しました。宛先: {To}", to);
    }

    /// <summary>SendGrid のレスポンスが成功かを判定する</summary>
    private static bool IsSuccessStatusCode(Response response)
        => (int)response.StatusCode is >= 200 and < 300;
}
