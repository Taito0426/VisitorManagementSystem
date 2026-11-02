using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Data.AzureSql;

/// <summary>
/// Azure SQL Database 用の接続ファクトリ（設計書 4.3節）。
/// 認証は SQL認証に統一する（Entra ID認証は将来拡張のため実装しない。設計書 4.1節・14章）。
/// </summary>
public sealed class AzureSqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <inheritdoc />
    public string ProviderName => DatabaseProviderNames.AzureSql;

    public AzureSqlConnectionFactory(IOptions<DatabaseSettings> databaseOptions)
    {
        ArgumentNullException.ThrowIfNull(databaseOptions);

        var database = databaseOptions.Value;
        if (!database.ConnectionStrings.TryGetValue(DatabaseProviderNames.AzureSql, out var connectionString)
            || string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "接続文字列 Database:ConnectionStrings:AzureSql が設定されていません。appsettings.json を確認してください。");
        }

        // 設計書 11.2節：Azure SQL Database は暗号化必須（Encrypt=True / TrustServerCertificate=False）
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Encrypt = true,
            TrustServerCertificate = false,
        };

        _connectionString = builder.ConnectionString;
    }

    /// <inheritdoc />
    public IDbConnection CreateOpenConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
