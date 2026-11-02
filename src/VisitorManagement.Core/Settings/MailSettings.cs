namespace VisitorManagement.Core.Settings;

/// <summary>
/// Mail セクション（設計書 5.5節・付録B）。
/// </summary>
public sealed class MailSettings
{
    /// <summary>設定セクション名</summary>
    public const string SectionName = "Mail";

    /// <summary>"SendGrid" | "Smtp"</summary>
    public string Provider { get; set; } = MailProviderNames.SendGrid;

    /// <summary>SendGrid の設定</summary>
    public SendGridSettings SendGrid { get; set; } = new();

    /// <summary>SMTP の設定</summary>
    public SmtpSettings Smtp { get; set; } = new();
}

/// <summary>SendGrid の設定（クラウド版の既定）。</summary>
public sealed class SendGridSettings
{
    /// <summary>APIキー</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>差出人メールアドレス</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>差出人表示名</summary>
    public string FromName { get; set; } = string.Empty;
}

/// <summary>SMTP の設定（オンプレ版の既定）。</summary>
public sealed class SmtpSettings
{
    /// <summary>SMTPサーバーホスト名</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>ポート番号</summary>
    public int Port { get; set; } = 587;

    /// <summary>SSL/TLSを使用するか</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>認証ユーザー（差出人アドレスを兼ねる）</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>認証パスワード</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>差出人表示名（未設定時は User を使用）</summary>
    public string FromName { get; set; } = string.Empty;
}

/// <summary>
/// メール送信方式の識別子。
/// </summary>
public static class MailProviderNames
{
    /// <summary>SendGrid</summary>
    public const string SendGrid = "SendGrid";

    /// <summary>SMTP</summary>
    public const string Smtp = "Smtp";
}
