using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Entities;

/// <summary>
/// メール送信履歴（設計書 9.2.6節 MailHistory）。
/// 招待者宛・予約登録者宛の送信結果をそれぞれ独立したレコードとして記録する。
/// </summary>
public sealed class MailHistory
{
    /// <summary>主キー（IDENTITY）</summary>
    public long MailId { get; set; }

    /// <summary>外部キー→Visitors</summary>
    public Guid VisitorId { get; set; }

    /// <summary>1:来館案内 2:リマインド 3:キャンセル通知 4:再送信</summary>
    public MailType MailType { get; set; }

    /// <summary>1:招待者(代表者のみ) 2:予約登録者(Operators.Email)</summary>
    public MailRecipientType RecipientType { get; set; }

    /// <summary>1:SendGrid 2:SMTP</summary>
    public MailProviderType Provider { get; set; }

    /// <summary>送信日時（UTC）</summary>
    public DateTime SentAt { get; set; }

    /// <summary>0:失敗 1:成功</summary>
    public MailSendStatus Status { get; set; }

    /// <summary>送信失敗時のエラー内容</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>来訪者氏名（照会用の非永続項目）</summary>
    public string? VisitorName { get; set; }

    /// <summary>来訪者所属会社名（照会用の非永続項目）</summary>
    public string? CompanyName { get; set; }
}
