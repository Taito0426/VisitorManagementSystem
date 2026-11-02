using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Dtos;

/// <summary>
/// メール送信履歴の検索条件（設計書 6.8節）。
/// </summary>
public sealed class MailHistorySearchCondition
{
    /// <summary>来訪者氏名（部分一致）</summary>
    public string? VisitorName { get; set; }

    /// <summary>会社名（部分一致）</summary>
    public string? CompanyName { get; set; }

    /// <summary>送信日時（開始・ローカル日付）</summary>
    public DateOnly? DateFrom { get; set; }

    /// <summary>送信日時（終了・ローカル日付）</summary>
    public DateOnly? DateTo { get; set; }

    /// <summary>拠点で絞り込む場合に指定</summary>
    public int? SiteId { get; set; }

    /// <summary>宛先区分で絞り込む場合に指定</summary>
    public MailRecipientType? RecipientType { get; set; }

    /// <summary>送信結果で絞り込む場合に指定</summary>
    public MailSendStatus? Status { get; set; }

    /// <summary>閲覧範囲の制限：指定した操作ユーザーが登録した予約の履歴のみ（一般ロール用）</summary>
    public int? CreatedByOperatorId { get; set; }

    /// <summary>取得最大件数（未指定時は上限なし）</summary>
    public int? MaxRows { get; set; }
}
