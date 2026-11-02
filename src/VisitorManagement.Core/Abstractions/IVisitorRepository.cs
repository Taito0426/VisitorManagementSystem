using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// 業務エンティティのRepository契約（設計書 4.3.1節。DB方式に依存しない）。
/// 設計書に定義された4メソッドはシグネチャをそのまま維持し、
/// グループ操作（複数名同時来館）に必要なメソッドを追加している。
/// </summary>
public interface IVisitorRepository
{
    /// <summary>来館者を1件取得する（該当なしの場合は null）</summary>
    Task<Visitor?> GetByIdAsync(Guid visitorId);

    /// <summary>来館者を1件登録し、VisitorId を返す</summary>
    Task<Guid> CreateAsync(Visitor visitor);

    /// <summary>来館者1名のステータスを更新する</summary>
    Task UpdateStatusAsync(Guid visitorId, VisitorStatus status);

    /// <summary>条件に一致する来館者を検索する</summary>
    Task<IReadOnlyList<Visitor>> SearchAsync(VisitorSearchCondition condition);

    // --- 以下、複数名同時来館（ReservationGroupId 単位）への対応で追加 ---

    /// <summary>来館者を1件取得する（トランザクション内で使用）</summary>
    Task<Visitor?> GetByIdAsync(Guid visitorId, IDbSession? session);

    /// <summary>予約グループに属する全来館者を取得する（代表者を先頭とする）</summary>
    Task<IReadOnlyList<Visitor>> GetByGroupIdAsync(Guid reservationGroupId, IDbSession? session = null);

    /// <summary>来館者を1件登録する（トランザクション内で使用）</summary>
    Task<Guid> CreateAsync(Visitor visitor, IDbSession? session);

    /// <summary>来館者の予約内容を更新する（ステータス以外の項目）</summary>
    Task UpdateAsync(Visitor visitor, IDbSession? session = null);

    /// <summary>予約グループ全員のステータスを一括更新する。更新件数を返す</summary>
    Task<int> UpdateGroupStatusAsync(Guid reservationGroupId, VisitorStatus status, IDbSession? session = null);

    /// <summary>来館者1名のステータスを更新する（トランザクション内で使用）</summary>
    Task UpdateStatusAsync(Guid visitorId, VisitorStatus status, IDbSession? session);

    /// <summary>予約グループを削除する（代表者・同行者・QRトークンを含む）</summary>
    Task DeleteGroupAsync(Guid reservationGroupId, IDbSession? session = null);

    /// <summary>来館者1名を削除する（予約編集で同行者を外した場合に使用）</summary>
    Task DeleteVisitorAsync(Guid visitorId, IDbSession? session = null);

    /// <summary>手動退館処理（設計書 6.4.1節）の対象者を氏名・来館日で検索する</summary>
    Task<IReadOnlyList<ManualCheckOutTarget>> SearchManualCheckOutTargetsAsync(
        string? visitorName, DateOnly? visitDate, int? siteId);
}
