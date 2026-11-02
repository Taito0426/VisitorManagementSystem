using System.Text;
using Dapper;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Infrastructure.Data.Repositories;

/// <summary>
/// 操作監査ログの Repository 実装（設計書 9.2.8節・11.5節）。
/// </summary>
public sealed class AuditLogRepository(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
    : RepositoryBase(sessionFactory, retryPolicy), IAuditLogRepository
{
    private const string SelectColumns = """
        SELECT  AuditLogId, TargetEntity, TargetId, ChangeType, OldValue, NewValue,
                ChangedAt, ChangedByOperatorId
        FROM    dbo.AuditLog
        """;

    /// <inheritdoc />
    public Task<long> CreateAsync(AuditLog log, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(log);

        const string sql = """
            INSERT INTO dbo.AuditLog
                (TargetEntity, TargetId, ChangeType, OldValue, NewValue, ChangedAt, ChangedByOperatorId)
            VALUES
                (@TargetEntity, @TargetId, @ChangeType, @OldValue, @NewValue, sysutcdatetime(), @ChangedByOperatorId);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            log.TargetEntity,
            log.TargetId,
            ChangeType = (byte)log.ChangeType,
            log.OldValue,
            log.NewValue,
            log.ChangedByOperatorId,
        }, s.Transaction)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> SearchAsync(
        string? targetEntity, ChangeType? changeType, DateOnly? dateFrom, DateOnly? dateTo, int? maxRows)
    {
        var top = maxRows.HasValue ? $"TOP ({maxRows.Value}) " : string.Empty;
        var sql = new StringBuilder(SelectColumns.Replace("SELECT  ", $"SELECT  {top}"));
        sql.AppendLine().Append("WHERE 1 = 1");

        if (!string.IsNullOrWhiteSpace(targetEntity))
        {
            sql.AppendLine().Append("  AND TargetEntity = @TargetEntity");
        }
        if (changeType.HasValue)
        {
            sql.AppendLine().Append("  AND ChangeType = @ChangeType");
        }
        if (dateFrom.HasValue)
        {
            sql.AppendLine().Append("  AND ChangedAt >= @DateFromUtc");
        }
        if (dateTo.HasValue)
        {
            sql.AppendLine().Append("  AND ChangedAt < @DateToUtc");
        }

        sql.AppendLine().Append("ORDER BY ChangedAt DESC, AuditLogId DESC;");

        var parameters = new DynamicParameters();
        parameters.Add("TargetEntity", targetEntity);
        parameters.Add("ChangeType", (byte?)changeType);
        parameters.Add("DateFromUtc", AccessLogRepository.ToUtcRangeStart(dateFrom));
        parameters.Add("DateToUtc", AccessLogRepository.ToUtcRangeEndExclusive(dateTo));

        var rows = await ExecuteAsync(null, s => s.Connection.QueryAsync<AuditLog>(
            new CommandDefinition(sql.ToString(), parameters, s.Transaction))).ConfigureAwait(false);

        return rows.ToList();
    }
}
