using Dapper;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Infrastructure.Data.Repositories;

/// <summary>
/// 操作ユーザーの Repository 実装（設計書 9.2.2節）。
/// RoleType・SiteId はDB制約上もNULL不可であり、未設定状態は発生しない。
/// </summary>
public sealed class OperatorRepository(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
    : RepositoryBase(sessionFactory, retryPolicy), IOperatorRepository
{
    private const string SelectColumns = """
        SELECT  o.OperatorId, o.LoginId, o.PasswordHash, o.DisplayName, o.Email,
                o.RoleType, o.SiteId, o.IsActive, o.CreatedAt,
                s.SiteName AS SiteName
        FROM    dbo.Operators AS o
                INNER JOIN dbo.Sites AS s ON s.SiteId = o.SiteId
        """;

    /// <inheritdoc />
    public Task<Operator?> GetByIdAsync(int operatorId, IDbSession? session = null)
        => ExecuteAsync(session, s => s.Connection.QuerySingleOrDefaultAsync<Operator>(
            new CommandDefinition($"{SelectColumns} WHERE o.OperatorId = @OperatorId;",
                new { OperatorId = operatorId }, s.Transaction)));

    /// <inheritdoc />
    public Task<Operator?> GetByLoginIdAsync(string loginId, IDbSession? session = null)
        => ExecuteAsync(session, s => s.Connection.QuerySingleOrDefaultAsync<Operator>(
            new CommandDefinition($"{SelectColumns} WHERE o.LoginId = @LoginId;",
                new { LoginId = loginId }, s.Transaction)));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Operator>> GetAllAsync(bool activeOnly = false, IDbSession? session = null)
    {
        var sql = activeOnly
            ? $"{SelectColumns} WHERE o.IsActive = 1 ORDER BY o.OperatorId;"
            : $"{SelectColumns} ORDER BY o.OperatorId;";

        var rows = await ExecuteAsync(session, s => s.Connection.QueryAsync<Operator>(
            new CommandDefinition(sql, transaction: s.Transaction))).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public Task<int> CreateAsync(Operator op, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(op);

        const string sql = """
            INSERT INTO dbo.Operators
                (LoginId, PasswordHash, DisplayName, Email, RoleType, SiteId, IsActive)
            VALUES
                (@LoginId, @PasswordHash, @DisplayName, @Email, @RoleType, @SiteId, @IsActive);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            op.LoginId,
            op.PasswordHash,
            op.DisplayName,
            op.Email,
            RoleType = (byte)op.RoleType,
            op.SiteId,
            op.IsActive,
        }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task UpdateAsync(Operator op, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(op);

        // パスワードは UpdatePasswordHashAsync で別途更新する
        const string sql = """
            UPDATE  dbo.Operators
            SET     LoginId = @LoginId,
                    DisplayName = @DisplayName,
                    Email = @Email,
                    RoleType = @RoleType,
                    SiteId = @SiteId,
                    IsActive = @IsActive
            WHERE   OperatorId = @OperatorId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            op.OperatorId,
            op.LoginId,
            op.DisplayName,
            op.Email,
            RoleType = (byte)op.RoleType,
            op.SiteId,
            op.IsActive,
        }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task UpdatePasswordHashAsync(int operatorId, string passwordHash, IDbSession? session = null)
    {
        const string sql = "UPDATE dbo.Operators SET PasswordHash = @PasswordHash WHERE OperatorId = @OperatorId;";

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { OperatorId = operatorId, PasswordHash = passwordHash }, s.Transaction)));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsLoginIdAsync(string loginId, int? excludeOperatorId = null, IDbSession? session = null)
    {
        const string sql = """
            SELECT  COUNT(*)
            FROM    dbo.Operators
            WHERE   LoginId = @LoginId
                    AND (@ExcludeOperatorId IS NULL OR OperatorId <> @ExcludeOperatorId);
            """;

        var count = await ExecuteAsync(session, s => s.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { LoginId = loginId, ExcludeOperatorId = excludeOperatorId }, s.Transaction)))
            .ConfigureAwait(false);

        return count > 0;
    }
}
