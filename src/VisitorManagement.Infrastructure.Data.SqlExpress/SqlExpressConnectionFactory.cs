using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Data.SqlExpress;

/// <summary>
/// SQL Server Express 用の接続ファクトリ（設計書 4.3節）。
/// デモ版（設計書 19.2節）も本クラスをそのまま使用し、接続先データベース名のみ
/// VisitorMgmtDemo に切り替える（Demo専用の実装は追加しない）。
/// </summary>
public sealed class SqlExpressConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <inheritdoc />
    public string ProviderName => DatabaseProviderNames.SqlExpress;

    public SqlExpressConnectionFactory(IOptions<DatabaseSettings> databaseOptions, IOptions<DemoSettings> demoOptions)
    {
        ArgumentNullException.ThrowIfNull(databaseOptions);
        ArgumentNullException.ThrowIfNull(demoOptions);

        var database = databaseOptions.Value;
        var demo = demoOptions.Value;

        // デモ版は Database:Provider を "SqlExpress" のまま使用し、接続先のみ Demo:ConnectionString に切り替える
        if (demo.Enabled && !string.IsNullOrWhiteSpace(demo.ConnectionString))
        {
            _connectionString = demo.ConnectionString;
            return;
        }

        if (!database.ConnectionStrings.TryGetValue(DatabaseProviderNames.SqlExpress, out var connectionString)
            || string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "接続文字列 Database:ConnectionStrings:SqlExpress が設定されていません。appsettings.json を確認してください。");
        }

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public IDbConnection CreateOpenConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
