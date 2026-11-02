using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Infrastructure.Mail;

/// <summary>
/// メール定型文（MailTemplates）を <see cref="IMailBodyComposer"/> として公開するアダプタ。
/// 文面の実体は MailTemplates 側に一本化し、送信とPDFプレビューで同じ本文を使用する。
/// </summary>
public sealed class MailBodyComposer : IMailBodyComposer
{
    /// <inheritdoc />
    public string QrAttachmentFileName => MailTemplates.QrAttachmentFileName;

    /// <inheritdoc />
    public string BuildSubject(MailType mailType, MailRecipientType recipientType)
        => MailTemplates.BuildSubject(mailType, recipientType);

    /// <inheritdoc />
    public string BuildInvitationBody(
        Visitor representative,
        Operator registeredBy,
        MailRecipientType recipientType,
        IReadOnlyList<Visitor>? companions,
        string? siteName)
        => MailTemplates.BuildInvitationBody(representative, registeredBy, recipientType, companions, siteName);
}
