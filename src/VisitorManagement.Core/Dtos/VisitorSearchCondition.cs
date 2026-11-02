using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Dtos;

/// <summary>
/// 来館予約の検索条件（設計書 4.3.1節 IVisitorRepository.SearchAsync）。
/// 閲覧範囲の絞り込み（11.6節 RBAC）も本条件で表現する。
/// </summary>
public sealed class VisitorSearchCondition
{
    /// <summary>来訪者氏名（部分一致）</summary>
    public string? VisitorName { get; set; }

    /// <summary>会社名（部分一致）</summary>
    public string? CompanyName { get; set; }

    /// <summary>来館日（開始）</summary>
    public DateOnly? VisitDateFrom { get; set; }

    /// <summary>来館日（終了）</summary>
    public DateOnly? VisitDateTo { get; set; }

    /// <summary>拠点で絞り込む場合に指定</summary>
    public int? SiteId { get; set; }

    /// <summary>ステータスで絞り込む場合に指定</summary>
    public VisitorStatus? Status { get; set; }

    /// <summary>予約種別で絞り込む場合に指定</summary>
    public ReservationType? ReservationType { get; set; }

    /// <summary>予約グループで絞り込む場合に指定</summary>
    public Guid? ReservationGroupId { get; set; }

    /// <summary>代表者行のみを対象とする場合に true</summary>
    public bool RepresentativeOnly { get; set; }

    /// <summary>閲覧範囲の制限：指定した操作ユーザーが登録した予約のみを対象とする（一般ロール用）</summary>
    public int? CreatedByOperatorId { get; set; }

    /// <summary>取得最大件数（未指定時は上限なし）</summary>
    public int? MaxRows { get; set; }
}
