namespace VisitorManagement.Core.Enums;

/// <summary>
/// メール種別（設計書 9.2.6節 / MailHistory.MailType）。
/// </summary>
public enum MailType : byte
{
    /// <summary>来館案内</summary>
    VisitInvitation = 1,

    /// <summary>リマインド</summary>
    Reminder = 2,

    /// <summary>キャンセル通知</summary>
    CancellationNotice = 3,

    /// <summary>再送信（紛失・未着対応）</summary>
    Resend = 4,
}
