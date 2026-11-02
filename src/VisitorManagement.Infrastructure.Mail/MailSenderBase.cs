using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Infrastructure.Mail;

/// <summary>
/// メール送信の共通処理（宛先解決・本文組み立て）。
/// 実際の送信手段（SendGrid / SMTP）のみを派生クラスが実装する（設計書 5.1節）。
/// </summary>
public abstract class MailSenderBase : IMailSender
{
    /// <inheritdoc />
    public abstract MailProviderType ProviderType { get; }

    /// <inheritdoc />
    /// <remarks>実送信を行う SendGrid / SMTP は false。モックの DemoMailSender のみ true とする。</remarks>
    public virtual bool IsTestMode => false;

    /// <summary>実際の送信処理。失敗時は例外を送出する</summary>
    /// <param name="to">送信先メールアドレス</param>
    /// <param name="subject">件名</param>
    /// <param name="plainTextBody">プレーンテキスト本文</param>
    /// <param name="attachment">添付ファイル（QRコード画像。不要な場合は null）</param>
    protected abstract Task SendCoreAsync(string to, string subject, string plainTextBody, MailAttachment? attachment);

    /// <inheritdoc />
    public Task SendVisitInvitationAsync(
        Visitor visitor,
        Operator registeredBy,
        byte[] qrCodePng,
        MailRecipientType recipientType,
        IReadOnlyList<Visitor>? companions = null)
        => SendInvitationCoreAsync(visitor, registeredBy, qrCodePng, recipientType, companions, MailType.VisitInvitation);

    /// <inheritdoc />
    public Task ResendInvitationAsync(
        Visitor visitor,
        Operator registeredBy,
        byte[] qrCodePng,
        MailRecipientType recipientType = MailRecipientType.Invitee,
        IReadOnlyList<Visitor>? companions = null)
        => SendInvitationCoreAsync(visitor, registeredBy, qrCodePng, recipientType, companions, MailType.Resend);

    /// <inheritdoc />
    public Task SendReminderAsync(Visitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        var to = RequireAddress(visitor.VisitorEmail, "招待者（代表者）");
        var subject = MailTemplates.BuildSubject(MailType.Reminder, MailRecipientType.Invitee);
        return SendCoreAsync(to, subject, MailTemplates.BuildReminderBody(visitor), null);
    }

    /// <inheritdoc />
    public Task SendCancellationNoticeAsync(Visitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        var to = RequireAddress(visitor.VisitorEmail, "招待者（代表者）");
        var subject = MailTemplates.BuildSubject(MailType.CancellationNotice, MailRecipientType.Invitee);
        return SendCoreAsync(to, subject, MailTemplates.BuildCancellationBody(visitor), null);
    }

    /// <summary>来館案内メール（初回送信・再送信）の共通処理</summary>
    private Task SendInvitationCoreAsync(
        Visitor visitor,
        Operator registeredBy,
        byte[] qrCodePng,
        MailRecipientType recipientType,
        IReadOnlyList<Visitor>? companions,
        MailType mailType)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentNullException.ThrowIfNull(registeredBy);
        ArgumentNullException.ThrowIfNull(qrCodePng);

        // 宛先は招待者（代表者）または予約登録者の2者のみ（設計書 5.2節）。同行者個人へは送信しない
        var to = recipientType == MailRecipientType.Invitee
            ? RequireAddress(visitor.VisitorEmail, "招待者（代表者）")
            : RequireAddress(registeredBy.Email, "予約登録者");

        var subject = MailTemplates.BuildSubject(mailType, recipientType);
        var body = MailTemplates.BuildInvitationBody(visitor, registeredBy, recipientType, companions, visitor.SiteName);

        // 予約登録者宛にも代表者と同一のQRコードを添付する（紛失時の転送起点。設計書 5.2節）
        var attachment = new MailAttachment(MailTemplates.QrAttachmentFileName, "image/png", qrCodePng);

        return SendCoreAsync(to, subject, body, attachment);
    }

    /// <summary>送信先アドレスが設定されていることを確認する</summary>
    private static string RequireAddress(string? address, string recipientLabel)
        => string.IsNullOrWhiteSpace(address)
            ? throw new InvalidOperationException($"{recipientLabel}のメールアドレスが設定されていません。")
            : address;
}

/// <summary>
/// メール添付ファイル。
/// </summary>
/// <param name="FileName">ファイル名</param>
/// <param name="ContentType">MIMEタイプ</param>
/// <param name="Content">ファイル内容</param>
public readonly record struct MailAttachment(string FileName, string ContentType, byte[] Content);
