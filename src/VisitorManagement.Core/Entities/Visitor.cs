using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Entities;

/// <summary>
/// 来館予約（設計書 9.2.3節 Visitors）。
/// 代表者1名＋任意の同行者を ReservationGroupId で束ねる（複数名同時来館対応）。
/// </summary>
public sealed class Visitor
{
    /// <summary>主キー</summary>
    public Guid VisitorId { get; set; }

    /// <summary>同一予約内の代表者・同行者を束ねるグループID</summary>
    public Guid ReservationGroupId { get; set; }

    /// <summary>true:代表者 false:同行者</summary>
    public bool IsRepresentative { get; set; } = true;

    /// <summary>来館先拠点（外部キー→Sites）</summary>
    public int SiteId { get; set; }

    /// <summary>来訪者所属会社名</summary>
    public string? CompanyName { get; set; }

    /// <summary>来訪者氏名</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>フリガナ</summary>
    public string? VisitorNameKana { get; set; }

    /// <summary>招待者連絡先。代表者は必須運用（当日受付の代表者は任意）。同行者行は未使用</summary>
    public string? VisitorEmail { get; set; }

    /// <summary>電話番号</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>来館予定日（当日受付は受付日と同一）</summary>
    public DateOnly VisitDate { get; set; }

    /// <summary>来館予定時刻（開始）</summary>
    public TimeOnly VisitTimeFrom { get; set; }

    /// <summary>来館予定時刻（終了）</summary>
    public TimeOnly? VisitTimeTo { get; set; }

    /// <summary>訪問先担当者名</summary>
    public string? HostEmployeeName { get; set; }

    /// <summary>来館目的</summary>
    public string? PurposeOfVisit { get; set; }

    /// <summary>1:事前予約 2:当日受付</summary>
    public ReservationType ReservationType { get; set; } = ReservationType.Advance;

    /// <summary>予約登録者（外部キー→Operators）</summary>
    public int CreatedByOperatorId { get; set; }

    /// <summary>予約ステータス</summary>
    public VisitorStatus Status { get; set; } = VisitorStatus.NotArrived;

    /// <summary>登録日時（UTC）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>更新日時（UTC）</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>楽観的排他制御用（rowversion）</summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>来館先拠点名（照会用の非永続項目）</summary>
    public string? SiteName { get; set; }

    /// <summary>予約登録者の表示名（照会用の非永続項目）</summary>
    public string? CreatedByDisplayName { get; set; }
}
