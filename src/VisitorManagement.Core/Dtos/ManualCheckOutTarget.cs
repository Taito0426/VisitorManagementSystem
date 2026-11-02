using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Dtos;

/// <summary>
/// 手動退館処理（設計書 6.4.1節）の対象者検索結果。
/// 予約グループ内の来館者一覧、および氏名・来館日での検索結果として使用する。
/// </summary>
public sealed class ManualCheckOutTarget
{
    /// <summary>来館者ID</summary>
    public Guid VisitorId { get; set; }

    /// <summary>予約グループID</summary>
    public Guid ReservationGroupId { get; set; }

    /// <summary>true:代表者 false:同行者</summary>
    public bool IsRepresentative { get; set; }

    /// <summary>氏名</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>会社名</summary>
    public string? CompanyName { get; set; }

    /// <summary>来館予定日</summary>
    public DateOnly VisitDate { get; set; }

    /// <summary>現在のステータス</summary>
    public VisitorStatus Status { get; set; }

    /// <summary>来館先拠点</summary>
    public int SiteId { get; set; }

    /// <summary>来館先拠点名</summary>
    public string? SiteName { get; set; }

    /// <summary>入館時刻（AccessLogs の入館記録。未入館の場合は null）</summary>
    public DateTime? CheckInDateTime { get; set; }
}
