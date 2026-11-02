using System.Text;
using Dapper;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Infrastructure.Data.Repositories;

/// <summary>
/// メール送信履歴の Repository 実装（設計書 9.2.6節）。
/// 招待者宛・予約登録者宛はそれぞれ独立したレコードとして記録する（設計書 5.2節）。
/// </summary>
public sealed class MailHistoryRepository(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
    : RepositoryBase(sessionFactory, retryPolicy), IMailHistoryRepository
{
    private const string SelectColumns = """
        SELECT  mh.MailId, mh.VisitorId, mh.MailType, mh.RecipientType, mh.Provider,
                mh.SentAt, mh.Status, mh.ErrorMessage,
                v.VisitorName AS VisitorName, v.CompanyName AS CompanyName
        FROM    dbo.MailHistory AS mh
                INNER JOIN dbo.Visitors AS v ON v.VisitorId = mh.VisitorId
        """;

    /// <inheritdoc />
    public Task<long> CreateAsync(MailHistory history, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(history);

        const string sql = """
            INSERT INTO dbo.MailHistory
                (VisitorId, MailType, RecipientType, Provider, SentAt, Status, ErrorMessage)
            VALUES
                (@VisitorId, @MailType, @RecipientType, @Provider, @SentAt, @Status, @ErrorMessage);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            history.VisitorId,
            MailType = (byte)history.MailType,
            RecipientType = (byte)history.RecipientType,
            Provider = (byte)history.Provider,
            SentAt = history.SentAt == default ? DateTime.UtcNow : history.SentAt,
            Status = (byte)history.Status,
            // nvarchar(500) に収まるよう切り詰める
            ErrorMessage = Truncate(history.ErrorMessage, 500),
        }, s.Transaction)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailHistory>> SearchAsync(MailHistorySearchCondition condition)
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
            sql.AppendLine().Append("  AND mh.SentAt >= @DateFromUtc");
        }
        if (condition.DateTo.HasValue)
        {
            sql.AppendLine().Append("  AND mh.SentAt < @DateToUtc");
        }
        if (condition.SiteId.HasValue)
        {
            sql.AppendLine().Append("  AND v.SiteId = @SiteId");
        }
        if (condition.RecipientType.HasValue)
        {
            sql.AppendLine().Append("  AND mh.RecipientType = @RecipientType");
        }
        if (condition.Status.HasValue)
        {
            sql.AppendLine().Append("  AND mh.Status = @Status");
        }
        // 一般ロールの閲覧範囲（自分が登録した予約の履歴のみ。設計書 11.6節）
        if (condition.CreatedByOperatorId.HasValue)
        {
            sql.AppendLine().Append("  AND v.CreatedByOperatorId = @CreatedByOperatorId");
        }

        sql.AppendLine().Append("ORDER BY mh.SentAt DESC, mh.MailId DESC;");

        var parameters = new DynamicParameters();
        parameters.Add("VisitorNamePattern", $"%{VisitorRepository.EscapeLike(condition.VisitorName)}%");
        parameters.Add("CompanyNamePattern", $"%{VisitorRepository.EscapeLike(condition.CompanyName)}%");
        parameters.Add("DateFromUtc", AccessLogRepository.ToUtcRangeStart(condition.DateFrom));
        parameters.Add("DateToUtc", AccessLogRepository.ToUtcRangeEndExclusive(condition.DateTo));
        parameters.Add("SiteId", condition.SiteId);
        parameters.Add("RecipientType", (byte?)condition.RecipientType);
        parameters.Add("Status", (byte?)condition.Status);
        parameters.Add("CreatedByOperatorId", condition.CreatedByOperatorId);

        var rows = await ExecuteAsync(null, s => s.Connection.QueryAsync<MailHistory>(
            new CommandDefinition(sql.ToString(), parameters, s.Transaction))).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailHistory>> GetForArchiveAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
    {
        var sql = $"""
            {SelectColumns}
            WHERE   mh.SentAt >= @FromUtc AND mh.SentAt < @ToUtc
            ORDER BY mh.SentAt, mh.MailId;
            """;

        var rows = await ExecuteAsync(null, s => s.Connection.QueryAsync<MailHistory>(
            new CommandDefinition(sql, new { FromUtc = fromUtcInclusive, ToUtc = toUtcExclusive }, s.Transaction)))
            .ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public Task<int> DeleteOlderThanAsync(DateTime thresholdUtc)
    {
        const string sql = "DELETE FROM dbo.MailHistory WHERE SentAt < @ThresholdUtc;";

        return ExecuteAsync(null, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { ThresholdUtc = thresholdUtc }, s.Transaction)));
    }

    /// <summary>指定長を超える文字列を切り詰める</summary>
    private static string? Truncate(string? value, int maxLength)
        => value is not null && value.Length > maxLength ? value[..maxLength] : value;
}
