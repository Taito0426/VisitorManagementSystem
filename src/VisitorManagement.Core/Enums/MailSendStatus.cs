namespace VisitorManagement.Core.Enums;

/// <summary>
/// メール送信結果（設計書 9.2.6節 / MailHistory.Status）。
/// </summary>
public enum MailSendStatus : byte
{
    /// <summary>失敗</summary>
    Failed = 0,

    /// <summary>成功</summary>
    Succeeded = 1,
}
