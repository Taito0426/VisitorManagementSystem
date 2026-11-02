using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// 来館案内メールの件名・本文を組み立てる契約。
///
/// 実体は Infrastructure.Mail の定型文（MailTemplates）だが、
/// PDFプレビュー（Infrastructure.Printing）からも同一文面を参照する必要があるため、
/// メール送信基盤（MailKit/SendGrid）への依存を持ち込まずに済むよう Core 側で契約を定義する。
/// </summary>
public interface IMailBodyComposer
{
    /// <summary>QRコード画像の添付ファイル名</summary>
    string QrAttachmentFileName { get; }

    /// <summary>メールの件名を組み立てる</summary>
    string BuildSubject(MailType mailType, MailRecipientType recipientType);

    /// <summary>来館案内メール／再送信メールの本文（プレーンテキスト）を組み立てる</summary>
    string BuildInvitationBody(
        Visitor representative,
        Operator registeredBy,
        MailRecipientType recipientType,
        IReadOnlyList<Visitor>? companions,
        string? siteName);
}
