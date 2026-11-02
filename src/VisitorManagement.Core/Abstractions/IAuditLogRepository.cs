using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// 操作監査ログのRepository契約（設計書 9.2.8節・11.5節）。
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>監査ログを1件登録する</summary>
    Task<long> CreateAsync(AuditLog log, IDbSession? session = null);

    /// <summary>監査ログを検索する</summary>
    Task<IReadOnlyList<AuditLog>> SearchAsync(
        string? targetEntity, ChangeType? changeType, DateOnly? dateFrom, DateOnly? dateTo, int? maxRows);
}
