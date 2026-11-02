using Dapper;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Infrastructure.Data.Repositories;

/// <summary>
/// 拠点マスタの Repository 実装（設計書 9.2.1節）。
/// </summary>
public sealed class SiteRepository(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
    : RepositoryBase(sessionFactory, retryPolicy), ISiteRepository
{
    private const string SelectColumns = """
        SELECT  SiteId, SiteName, Address, LabelCompanyName, LabelLogoImage,
                LabelSizeType, IsActive, CreatedAt
        FROM    dbo.Sites
        """;

    /// <inheritdoc />
    public Task<Site?> GetByIdAsync(int siteId, IDbSession? session = null)
        => ExecuteAsync(session, s => s.Connection.QuerySingleOrDefaultAsync<Site>(
            new CommandDefinition($"{SelectColumns} WHERE SiteId = @SiteId;", new { SiteId = siteId }, s.Transaction)));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Site>> GetAllAsync(bool activeOnly = true, IDbSession? session = null)
    {
        var sql = activeOnly
            ? $"{SelectColumns} WHERE IsActive = 1 ORDER BY SiteId;"
            : $"{SelectColumns} ORDER BY SiteId;";

        var rows = await ExecuteAsync(session, s => s.Connection.QueryAsync<Site>(
            new CommandDefinition(sql, transaction: s.Transaction))).ConfigureAwait(false);

        return rows.ToList();
    }

    /// <inheritdoc />
    public Task<int> CreateAsync(Site site, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(site);

        const string sql = """
            INSERT INTO dbo.Sites
                (SiteName, Address, LabelCompanyName, LabelLogoImage, LabelSizeType, IsActive)
            VALUES
                (@SiteName, @Address, @LabelCompanyName, @LabelLogoImage, @LabelSizeType, @IsActive);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            site.SiteName,
            site.Address,
            site.LabelCompanyName,
            site.LabelLogoImage,
            LabelSizeType = (byte)site.LabelSizeType,
            site.IsActive,
        }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task UpdateAsync(Site site, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(site);

        const string sql = """
            UPDATE  dbo.Sites
            SET     SiteName = @SiteName,
                    Address = @Address,
                    LabelCompanyName = @LabelCompanyName,
                    LabelLogoImage = @LabelLogoImage,
                    LabelSizeType = @LabelSizeType,
                    IsActive = @IsActive
            WHERE   SiteId = @SiteId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            site.SiteId,
            site.SiteName,
            site.Address,
            site.LabelCompanyName,
            site.LabelLogoImage,
            LabelSizeType = (byte)site.LabelSizeType,
            site.IsActive,
        }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task<int> CountAsync(IDbSession? session = null)
        => ExecuteAsync(session, s => s.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM dbo.Sites WHERE IsActive = 1;", transaction: s.Transaction)));
}
