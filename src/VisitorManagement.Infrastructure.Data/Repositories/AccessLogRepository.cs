using System.Text;
using Dapper;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Infrastructure.Data.Repositories;

/// <summary>
/// 入退館履歴の Repository 実装（設計書 9.2.5節）。
/// </summary>
public sealed class AccessLogRepository(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
    : RepositoryBase(sessionFactory, retryPolicy), IAccessLogRepository
{
    private const string SelectColumns = """
        SELECT  al.LogId, al.VisitorId, al.SiteId, al.TokenId, al.AccessType,
                al.AccessDateTime, al.DeviceInfo, al.OperatorNote,
                v.VisitorName AS VisitorName, v.CompanyName AS CompanyName, s.SiteName AS SiteName
        FROM    dbo.AccessLogs AS al
                INNER JOIN dbo.Visitors AS v ON v.VisitorId = al.VisitorId
                INNER JOIN dbo.Sites    AS s ON s.SiteId = al.SiteId
        """;

    private const string InsertSql = """
        INSERT INTO dbo.AccessLogs
            (VisitorId, SiteId, TokenId, AccessType, AccessDateTime, DeviceInfo, OperatorNote)
        VALUES
            (@VisitorId, @SiteId, @TokenId, @AccessType, @AccessDateTime, @DeviceInfo, @OperatorNote);
        """;

    /// <inheritdoc />
    public Task<long> CreateAsync(AccessLog log, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(log);

        const string sql = $"""
            {InsertSql}
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, ToParameters(log), s.Transaction)));
    }

    /// <inheritdoc />
    public Task CreateManyAsync(IEnumerable<AccessLog> logs, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(logs);

        // 代表者QRの1回の読取につきグループ全員分をまとめて登録する（設計書 6.4節）
        var parameters = logs.Select(ToParameters).ToList();
        if (parameters.Count == 0)
        {
            return Task.CompletedTask;
        }

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(InsertSql, parameters, s.Transaction)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccessLog>> SearchAsync(AccessLogSearchCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var top = condition.MaxRows.HasValue ? $"TOP ({condition.MaxRows.Value}) " : string.Empty;
        var sql = new StringBuilder(SelectColumns.Replace("SELECT  ", $"SELECT  {top}"));
        sql.AppendLine().Append("WHERE 1 = 1");

        if (!string.IsNullOrWhiteSpace(condition.VisitorName))
        {
            sql.AppendLine().Append("  AND v.VisitorName LIKE @VisitorNamePattern");
        }
        if (!string.IsNullOrWhiteSpace(condition.CompanyName))
        {
            sql.AppendLine().Append("  AND v.CompanyName LIKE @CompanyNamePattern");
        }
        if (condition.DateFrom.HasValue)
        {
            sql.AppendLine().Append("  AND al.AccessDateTime >= @DateFromUtc");
        }
        if (condition.DateTo.HasValue)
        {
            sql.AppendLine().Append("  AND al.AccessDateTime < @DateToUtc");
        }
        if (condition.SiteId.HasValue)
        {
            sql.AppendLine().Append("  AND al.SiteId = @SiteId");
        }
        if (condition.AccessType.HasValue)
        {
            sql.AppendLine().Append("  AND al.AccessType = @AccessType");
        }
        // 一般ロールの閲覧範囲（自分が登録した予約の履歴のみ。設計書 11.6節）
        if (condition.CreatedByOperatorId.HasValue)
        {
            sql.AppendLine().Append("  AND v.CreatedByOperatorId = @CreatedByOperatorId");
        }

        sql.AppendLine().Append("ORDER BY al.AccessDateTime DESC, al.LogId DESC;");

        var parameters = new DynamicParameters();
        parameters.Add("VisitorNamePattern", $"%{VisitorRepository.EscapeLike(condition.VisitorName)}%");
        parameters.Add("CompanyNamePattern", $"%{VisitorRepository.EscapeLike(condition.CompanyName)}%");
        // 画面で指定されたローカル日付を、DBに保存されているUTC範囲へ変換する
        parameters.Add("DateFromUtc", ToUtcRangeStart(condition.DateFrom));
        parameters.Add("DateToUtc", ToUtcRangeEndExclusive(condition.DateTo));
        parameters.Add("SiteId", condition.SiteId);
        parameters.Add("AccessType", (byte?)condition.AccessType);
        parameters.Add("CreatedByOperatorId", condition.CreatedByOperatorId);

        var rows = await ExecuteAsync(null, s => s.Connection.QueryAsync<AccessLog>(
            new CommandDefinition(sql.ToString(), parameters, s.Transaction))).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccessLog>> GetForArchiveAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
    {
        var sql = $"""
            {SelectColumns}
            WHERE   al.AccessDateTime >= @FromUtc AND al.AccessDateTime < @ToUtc
            ORDER BY al.AccessDateTime, al.LogId;
            """;

        var rows = await ExecuteAsync(null, s => s.Connection.QueryAsync<AccessLog>(
            new CommandDefinition(sql, new { FromUtc = fromUtcInclusive, ToUtc = toUtcExclusive }, s.Transaction)))
            .ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public Task<int> DeleteOlderThanAsync(DateTime thresholdUtc)
    {
        const string sql = "DELETE FROM dbo.AccessLogs WHERE AccessDateTime < @ThresholdUtc;";

        return ExecuteAsync(null, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { ThresholdUtc = thresholdUtc }, s.Transaction)));
    }

    /// <summary>エンティティをINSERTパラメータへ変換する</summary>
    private static object ToParameters(AccessLog log) => new
    {
        log.VisitorId,
        log.SiteId,
        log.TokenId,
        AccessType = (byte)log.AccessType,
        AccessDateTime = log.AccessDateTime == default ? DateTime.UtcNow : log.AccessDateTime,
        log.DeviceInfo,
        log.OperatorNote,
    };

    /// <summary>ローカル日付の 00:00 をUTCへ変換する</summary>
    internal static DateTime? ToUtcRangeStart(DateOnly? localDate)
        => localDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

    /// <summary>ローカル日付の翌日 00:00（範囲の排他的終端）をUTCへ変換する</summary>
    internal static DateTime? ToUtcRangeEndExclusive(DateOnly? localDate)
        => localDate?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
}
