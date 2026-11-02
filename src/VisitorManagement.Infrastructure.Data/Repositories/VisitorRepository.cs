using System.Text;
using Dapper;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Infrastructure.Data.Repositories;

/// <summary>
/// 来館予約の Repository 実装（設計書 9.2.3節）。
/// SQL文はDB方式に依らず共通（設計書 4.1節）。
/// </summary>
public sealed class VisitorRepository(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
    : RepositoryBase(sessionFactory, retryPolicy), IVisitorRepository
{
    /// <summary>照会用の共通SELECT句（拠点名・登録者名を結合する）</summary>
    private const string SelectColumns = """
        SELECT  v.VisitorId, v.ReservationGroupId, v.IsRepresentative, v.SiteId,
                v.CompanyName, v.VisitorName, v.VisitorNameKana, v.VisitorEmail, v.PhoneNumber,
                v.VisitDate, v.VisitTimeFrom, v.VisitTimeTo,
                v.HostEmployeeName, v.PurposeOfVisit, v.ReservationType,
                v.CreatedByOperatorId, v.Status, v.CreatedAt, v.UpdatedAt, v.RowVersion,
                s.SiteName AS SiteName, o.DisplayName AS CreatedByDisplayName
        FROM    dbo.Visitors AS v
                INNER JOIN dbo.Sites     AS s ON s.SiteId = v.SiteId
                INNER JOIN dbo.Operators AS o ON o.OperatorId = v.CreatedByOperatorId
        """;

    /// <inheritdoc />
    public Task<Visitor?> GetByIdAsync(Guid visitorId) => GetByIdAsync(visitorId, null);

    /// <summary>来館者を1件取得する（セッション指定版）</summary>
    public Task<Visitor?> GetByIdAsync(Guid visitorId, IDbSession? session)
        => ExecuteAsync(session, s => s.Connection.QuerySingleOrDefaultAsync<Visitor>(
            new CommandDefinition(
                $"{SelectColumns} WHERE v.VisitorId = @VisitorId;",
                new { VisitorId = visitorId },
                s.Transaction)));

    /// <inheritdoc />
    public Task<Guid> CreateAsync(Visitor visitor) => CreateAsync(visitor, null);

    /// <inheritdoc />
    public Task<Guid> CreateAsync(Visitor visitor, IDbSession? session)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        // 主キー・グループIDはアプリ側で採番し、登録直後に呼び出し側へ返せるようにする
        if (visitor.VisitorId == Guid.Empty)
        {
            visitor.VisitorId = Guid.NewGuid();
        }
        if (visitor.ReservationGroupId == Guid.Empty)
        {
            visitor.ReservationGroupId = Guid.NewGuid();
        }

        const string sql = """
            INSERT INTO dbo.Visitors
                (VisitorId, ReservationGroupId, IsRepresentative, SiteId,
                 CompanyName, VisitorName, VisitorNameKana, VisitorEmail, PhoneNumber,
                 VisitDate, VisitTimeFrom, VisitTimeTo,
                 HostEmployeeName, PurposeOfVisit, ReservationType,
                 CreatedByOperatorId, Status, CreatedAt, UpdatedAt)
            VALUES
                (@VisitorId, @ReservationGroupId, @IsRepresentative, @SiteId,
                 @CompanyName, @VisitorName, @VisitorNameKana, @VisitorEmail, @PhoneNumber,
                 @VisitDate, @VisitTimeFrom, @VisitTimeTo,
                 @HostEmployeeName, @PurposeOfVisit, @ReservationType,
                 @CreatedByOperatorId, @Status, sysutcdatetime(), sysutcdatetime());
            """;

        return ExecuteAsync(session, async s =>
        {
            await s.Connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                visitor.VisitorId,
                visitor.ReservationGroupId,
                visitor.IsRepresentative,
                visitor.SiteId,
                visitor.CompanyName,
                visitor.VisitorName,
                visitor.VisitorNameKana,
                visitor.VisitorEmail,
                visitor.PhoneNumber,
                visitor.VisitDate,
                visitor.VisitTimeFrom,
                visitor.VisitTimeTo,
                visitor.HostEmployeeName,
                visitor.PurposeOfVisit,
                ReservationType = (byte)visitor.ReservationType,
                visitor.CreatedByOperatorId,
                Status = (byte)visitor.Status,
            }, s.Transaction)).ConfigureAwait(false);

            return visitor.VisitorId;
        });
    }

    /// <inheritdoc />
    public Task UpdateStatusAsync(Guid visitorId, VisitorStatus status)
        => UpdateStatusAsync(visitorId, status, null);

    /// <inheritdoc />
    public Task UpdateStatusAsync(Guid visitorId, VisitorStatus status, IDbSession? session)
    {
        const string sql = """
            UPDATE  dbo.Visitors
            SET     Status = @Status,
                    UpdatedAt = sysutcdatetime()
            WHERE   VisitorId = @VisitorId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { VisitorId = visitorId, Status = (byte)status }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task UpdateAsync(Visitor visitor, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        const string sql = """
            UPDATE  dbo.Visitors
            SET     SiteId = @SiteId,
                    CompanyName = @CompanyName,
                    VisitorName = @VisitorName,
                    VisitorNameKana = @VisitorNameKana,
                    VisitorEmail = @VisitorEmail,
                    PhoneNumber = @PhoneNumber,
                    VisitDate = @VisitDate,
                    VisitTimeFrom = @VisitTimeFrom,
                    VisitTimeTo = @VisitTimeTo,
                    HostEmployeeName = @HostEmployeeName,
                    PurposeOfVisit = @PurposeOfVisit,
                    UpdatedAt = sysutcdatetime()
            WHERE   VisitorId = @VisitorId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            visitor.VisitorId,
            visitor.SiteId,
            visitor.CompanyName,
            visitor.VisitorName,
            visitor.VisitorNameKana,
            visitor.VisitorEmail,
            visitor.PhoneNumber,
            visitor.VisitDate,
            visitor.VisitTimeFrom,
            visitor.VisitTimeTo,
            visitor.HostEmployeeName,
            visitor.PurposeOfVisit,
        }, s.Transaction)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Visitor>> SearchAsync(VisitorSearchCondition condition)
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
        if (condition.VisitDateFrom.HasValue)
        {
            sql.AppendLine().Append("  AND v.VisitDate >= @VisitDateFrom");
        }
        if (condition.VisitDateTo.HasValue)
        {
            sql.AppendLine().Append("  AND v.VisitDate <= @VisitDateTo");
        }
        if (condition.SiteId.HasValue)
        {
            sql.AppendLine().Append("  AND v.SiteId = @SiteId");
        }
        if (condition.Status.HasValue)
        {
            sql.AppendLine().Append("  AND v.Status = @Status");
        }
        if (condition.ReservationType.HasValue)
        {
            sql.AppendLine().Append("  AND v.ReservationType = @ReservationType");
        }
        if (condition.ReservationGroupId.HasValue)
        {
            sql.AppendLine().Append("  AND v.ReservationGroupId = @ReservationGroupId");
        }
        if (condition.RepresentativeOnly)
        {
            sql.AppendLine().Append("  AND v.IsRepresentative = 1");
        }
        // 一般ロールの閲覧範囲（自分が登録した予約のみ。設計書 11.6節）
        if (condition.CreatedByOperatorId.HasValue)
        {
            sql.AppendLine().Append("  AND v.CreatedByOperatorId = @CreatedByOperatorId");
        }

        sql.AppendLine().Append("ORDER BY v.VisitDate DESC, v.VisitTimeFrom, v.ReservationGroupId, v.IsRepresentative DESC, v.VisitorName;");

        var parameters = new DynamicParameters();
        parameters.Add("VisitorNamePattern", $"%{EscapeLike(condition.VisitorName)}%");
        parameters.Add("CompanyNamePattern", $"%{EscapeLike(condition.CompanyName)}%");
        parameters.Add("VisitDateFrom", condition.VisitDateFrom);
        parameters.Add("VisitDateTo", condition.VisitDateTo);
        parameters.Add("SiteId", condition.SiteId);
        parameters.Add("Status", (byte?)condition.Status);
        parameters.Add("ReservationType", (byte?)condition.ReservationType);
        parameters.Add("ReservationGroupId", condition.ReservationGroupId);
        parameters.Add("CreatedByOperatorId", condition.CreatedByOperatorId);

        var rows = await ExecuteAsync(null, s => s.Connection.QueryAsync<Visitor>(
            new CommandDefinition(sql.ToString(), parameters, s.Transaction))).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Visitor>> GetByGroupIdAsync(Guid reservationGroupId, IDbSession? session = null)
    {
        // 代表者を先頭に並べる（設計書 6.1節：代表者1名＋同行者）
        var sql = $"{SelectColumns} WHERE v.ReservationGroupId = @ReservationGroupId ORDER BY v.IsRepresentative DESC, v.VisitorName;";

        var rows = await ExecuteAsync(session, s => s.Connection.QueryAsync<Visitor>(
            new CommandDefinition(sql, new { ReservationGroupId = reservationGroupId }, s.Transaction)))
            .ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public Task<int> UpdateGroupStatusAsync(Guid reservationGroupId, VisitorStatus status, IDbSession? session = null)
    {
        // キャンセル済（Status=9）の行は一括更新の対象外とし、誤って復活させない
        const string sql = """
            UPDATE  dbo.Visitors
            SET     Status = @Status,
                    UpdatedAt = sysutcdatetime()
            WHERE   ReservationGroupId = @ReservationGroupId
                    AND Status <> 9;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(new CommandDefinition(
            sql, new { ReservationGroupId = reservationGroupId, Status = (byte)status }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task DeleteGroupAsync(Guid reservationGroupId, IDbSession? session = null)
    {
        // 参照整合性の順に削除する。入退館履歴（AccessLogs）が存在する予約は
        // 外部キー制約により削除が失敗する（履歴を残すためキャンセル操作を使用する運用）。
        const string sql = """
            DELETE  mh
            FROM    dbo.MailHistory AS mh
                    INNER JOIN dbo.Visitors AS v ON v.VisitorId = mh.VisitorId
            WHERE   v.ReservationGroupId = @ReservationGroupId;

            DELETE  qt
            FROM    dbo.QRTokens AS qt
                    INNER JOIN dbo.Visitors AS v ON v.VisitorId = qt.VisitorId
            WHERE   v.ReservationGroupId = @ReservationGroupId;

            DELETE FROM dbo.Visitors
            WHERE   ReservationGroupId = @ReservationGroupId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { ReservationGroupId = reservationGroupId }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task DeleteVisitorAsync(Guid visitorId, IDbSession? session = null)
    {
        // 同行者を予約から外す操作。入退館履歴が存在する場合は外部キー制約により削除が失敗する
        const string sql = """
            DELETE FROM dbo.MailHistory WHERE VisitorId = @VisitorId;
            DELETE FROM dbo.QRTokens    WHERE VisitorId = @VisitorId;
            DELETE FROM dbo.Visitors    WHERE VisitorId = @VisitorId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { VisitorId = visitorId }, s.Transaction)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManualCheckOutTarget>> SearchManualCheckOutTargetsAsync(
        string? visitorName, DateOnly? visitDate, int? siteId)
    {
        // 手動退館処理（設計書 6.4.1節）の対象者。入館時刻を AccessLogs から取得して表示する
        var sql = new StringBuilder("""
            SELECT  v.VisitorId, v.ReservationGroupId, v.IsRepresentative,
                    v.VisitorName, v.CompanyName, v.VisitDate, v.Status,
                    v.SiteId, s.SiteName,
                    (SELECT MAX(al.AccessDateTime)
                     FROM   dbo.AccessLogs AS al
                     WHERE  al.VisitorId = v.VisitorId AND al.AccessType = 1) AS CheckInDateTime
            FROM    dbo.Visitors AS v
                    INNER JOIN dbo.Sites AS s ON s.SiteId = v.SiteId
            WHERE   v.Status = 1
            """);

        if (!string.IsNullOrWhiteSpace(visitorName))
        {
            sql.AppendLine().Append("  AND v.VisitorName LIKE @VisitorNamePattern");
        }
        if (visitDate.HasValue)
        {
            sql.AppendLine().Append("  AND v.VisitDate = @VisitDate");
        }
        if (siteId.HasValue)
        {
            sql.AppendLine().Append("  AND v.SiteId = @SiteId");
        }

        sql.AppendLine().Append("ORDER BY v.VisitDate DESC, v.ReservationGroupId, v.IsRepresentative DESC, v.VisitorName;");

        var parameters = new DynamicParameters();
        parameters.Add("VisitorNamePattern", $"%{EscapeLike(visitorName)}%");
        parameters.Add("VisitDate", visitDate);
        parameters.Add("SiteId", siteId);

        var rows = await ExecuteAsync(null, s => s.Connection.QueryAsync<ManualCheckOutTarget>(
            new CommandDefinition(sql.ToString(), parameters, s.Transaction))).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <summary>LIKE 検索のワイルドカード文字をエスケープする</summary>
    internal static string EscapeLike(string? value)
        => value is null ? string.Empty : value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
