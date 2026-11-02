using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Dtos;

/// <summary>
/// 来館予約の登録・編集入力（設計書 6.1節・6.6節）。
/// 事前予約・当日受付で同一のDTOを使用し、特別な例外フローを設けない。
/// </summary>
public sealed class ReservationInput
{
    /// <summary>来館先拠点</summary>
    public int SiteId { get; set; }

    /// <summary>来館予定日</summary>
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

    /// <summary>代表者情報</summary>
    public ReservationMemberInput Representative { get; set; } = new();

    /// <summary>同行者情報（0名以上）</summary>
    public List<ReservationMemberInput> Companions { get; set; } = [];
}

/// <summary>
/// 予約に含まれる来館者1名分の入力。
/// </summary>
public sealed class ReservationMemberInput
{
    /// <summary>既存行の更新時に指定する VisitorId（新規は null）</summary>
    public Guid? VisitorId { get; set; }

    /// <summary>来訪者所属会社名</summary>
    public string? CompanyName { get; set; }

    /// <summary>来訪者氏名</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>フリガナ</summary>
    public string? VisitorNameKana { get; set; }

    /// <summary>メールアドレス（代表者のみ使用。同行者行は未使用）</summary>
    public string? VisitorEmail { get; set; }

    /// <summary>電話番号</summary>
    public string? PhoneNumber { get; set; }
}
