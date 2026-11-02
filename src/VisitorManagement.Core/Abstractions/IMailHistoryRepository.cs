using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// メール送信履歴のRepository契約（設計書 4.3.1節：送信履歴登録・照会）。
/// </summary>
public interface IMailHistoryRepository
{
    /// <summary>送信履歴を1件登録する</summary>
    Task<long> CreateAsync(MailHistory history, IDbSession? session = null);

    /// <summary>条件に一致する送信履歴を検索する</summary>
    Task<IReadOnlyList<MailHistory>> SearchAsync(MailHistorySearchCondition condition);

    /// <summary>指定期間の履歴を取得する（月次アーカイブバッチ用。設計書 10.4節）</summary>
    Task<IReadOnlyList<MailHistory>> GetForArchiveAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive);

    /// <summary>指定日時より前の履歴をDBから削除する（設計書 10.4節）。削除件数を返す</summary>
    Task<int> DeleteOlderThanAsync(DateTime thresholdUtc);
}
