using Dapper;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Infrastructure.Data.Repositories;

/// <summary>
/// 会社名マスタの Repository 実装（設計書 9.2.7節）。
/// </summary>
public sealed class CompanyRepository(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
    : RepositoryBase(sessionFactory, retryPolicy), ICompanyRepository
{
    private const string SelectColumns = """
        SELECT  CompanyId, CompanyName, Address, Tel
        FROM    dbo.Companies
        """;

    /// <inheritdoc />
    public Task<Company?> GetByIdAsync(int companyId, IDbSession? session = null)
        => ExecuteAsync(session, s => s.Connection.QuerySingleOrDefaultAsync<Company>(
            new CommandDefinition($"{SelectColumns} WHERE CompanyId = @CompanyId;",
                new { CompanyId = companyId }, s.Transaction)));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Company>> GetAllAsync(IDbSession? session = null)
    {
        var rows = await ExecuteAsync(session, s => s.Connection.QueryAsync<Company>(
            new CommandDefinition($"{SelectColumns} ORDER BY CompanyName;", transaction: s.Transaction)))
            .ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public Task<int> CreateAsync(Company company, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        const string sql = """
            INSERT INTO dbo.Companies (CompanyName, Address, Tel)
            VALUES (@CompanyName, @Address, @Tel);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { company.CompanyName, company.Address, company.Tel }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task UpdateAsync(Company company, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        const string sql = """
            UPDATE  dbo.Companies
            SET     CompanyName = @CompanyName, Address = @Address, Tel = @Tel
            WHERE   CompanyId = @CompanyId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(new CommandDefinition(
            sql, new { company.CompanyId, company.CompanyName, company.Address, company.Tel }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task DeleteAsync(int companyId, IDbSession? session = null)
        => ExecuteAsync(session, s => s.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.Companies WHERE CompanyId = @CompanyId;", new { CompanyId = companyId }, s.Transaction)));
}
