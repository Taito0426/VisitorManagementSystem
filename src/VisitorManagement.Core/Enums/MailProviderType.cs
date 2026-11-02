namespace VisitorManagement.Core.Enums;

/// <summary>
/// メール送信プロバイダ（設計書 5章 / MailHistory.Provider）。
/// </summary>
public enum MailProviderType : byte
{
    /// <summary>SendGrid（クラウド版の既定）</summary>
    SendGrid = 1,

    /// <summary>SMTP（オンプレ版の既定）</summary>
    Smtp = 2,
}
