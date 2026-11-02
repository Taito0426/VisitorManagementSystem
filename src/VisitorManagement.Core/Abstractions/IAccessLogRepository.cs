using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// 入退館履歴のRepository契約（設計書 4.3.1節：入退館履歴登録・照会）。
/// </summary>
public interface IAccessLogRepository
{
    /// <summary>入退館履歴を1件登録する</summary>
    Task<long> CreateAsync(AccessLog log, IDbSession? session = null);

    /// <summary>入退館履歴を複数件まとめて登録する（グループ単位読取。設計書 6.4節）</summary>
    Task CreateManyAsync(IEnumerable<AccessLog> logs, IDbSession? session = null);

    /// <summary>条件に一致する入退館履歴を検索する</summary>
    Task<IReadOnlyList<AccessLog>> SearchAsync(AccessLogSearchCondition condition);

    /// <summary>指定期間より前の履歴を取得する（月次アーカイブバッチ用。設計書 10.4節）</summary>
    Task<IReadOnlyList<AccessLog>> GetForArchiveAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive);

    /// <summary>指定日時より前の履歴をDBから削除する（設計書 10.4節）。削除件数を返す</summary>
    Task<int> DeleteOlderThanAsync(DateTime thresholdUtc);
}
