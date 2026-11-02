using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Data.SqlExpress;

/// <summary>
/// SQL Server Express 用のバックアップサービス（設計書 10.1節）。
/// SQL Server Agent が利用できないため、通常運用では Windows タスクスケジューラから
/// PowerShell スクリプト（scripts/Backup-VisitorMgmt.ps1）を定時実行する。
/// 本クラスは同一の BACKUP DATABASE 文を管理画面から手動実行するための入口を提供する。
/// </summary>
public sealed class SqlExpressBackupService(
    IDbConnectionFactory connectionFactory,
    IOptions<BackupSettings> backupOptions,
    ILogger<SqlExpressBackupService> logger) : IBackupService
{
    /// <inheritdoc />
    public string ProviderName => DatabaseProviderNames.SqlExpress;

    /// <inheritdoc />
    public string DescribeBackupPolicy()
    {
        var settings = backupOptions.Value.SqlExpress;
        return $"Windowsタスクスケジューラ + PowerShell により毎日 {settings.DailyTime} に BACKUP DATABASE を実行。"
             + $" 保存先: {settings.Path} / 保管期間: {settings.RetentionDays}日";
    }

    /// <inheritdoc />
    public async Task<BackupResult> ExecuteBackupAsync(CancellationToken cancellationToken = default)
    {
        var settings = backupOptions.Value.SqlExpress;

        using var connection = (SqlConnection)connectionFactory.CreateOpenConnection();
        var databaseName = connection.Database;

        var fileName = $"{databaseName}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.bak";
        var filePath = Path.Combine(settings.Path, fileName);

        // バックアップ圧縮（COMPRESSION）は Express Edition では未サポートのため、
        // エディションを判定してオプションを切り替える（Express で指定するとエラー 1844 となる）。
        var options = SupportsCompression(connection) ? "INIT, COMPRESSION, CHECKSUM" : "INIT, CHECKSUM";

        // データベース名・ファイルパスはSQL識別子/文字列リテラルとして安全にエスケープする
        var sql = $"""
            BACKUP DATABASE [{EscapeIdentifier(databaseName)}]
            TO DISK = N'{EscapeStringLiteral(filePath)}'
            WITH {options};
            """;

        try
        {
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("バックアップを実行しました。出力先: {FilePath}", filePath);
            return new BackupResult(true, $"バックアップを実行しました。({filePath})", filePath);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "バックアップの実行に失敗しました。出力先: {FilePath}", filePath);

            // 設計書 11.1節により、アプリのサービスアカウント（svc_visitor）には
            // 対象テーブルへのCRUD権限のみを付与しており BACKUP DATABASE 権限は持たない。
            // 通常運用のバックアップは、設計書 10.1節のとおり
            // Windowsタスクスケジューラから scripts\Backup-VisitorMgmt.ps1 を
            // バックアップ権限のあるアカウントで実行する。
            var message = ex.Number == 262
                ? "バックアップを実行する権限がありません。"
                  + "アプリのサービスアカウントには最小権限のみを付与しているため、"
                  + "定時バックアップは scripts\\Backup-VisitorMgmt.ps1 を"
                  + "Windowsタスクスケジューラから実行してください（設計書 10.1節）。"
                  + "アプリから実行する場合は、SQLログインに db_backupoperator ロールを付与してください。"
                : $"バックアップの実行に失敗しました: {ex.Message}";

            return new BackupResult(false, message, null);
        }
    }

    /// <summary>
    /// バックアップ圧縮を利用できるエディションかを判定する。
    /// Express / Web エディションは COMPRESSION をサポートしない。
    /// </summary>
    private static bool SupportsCompression(SqlConnection connection)
    {
        using var command = new SqlCommand("SELECT CONVERT(nvarchar(128), SERVERPROPERTY('Edition'));", connection);
        var edition = command.ExecuteScalar() as string ?? string.Empty;

        return !edition.Contains("Express", StringComparison.OrdinalIgnoreCase)
            && !edition.Contains("Web", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>角括弧で囲む識別子内の "]" をエスケープする</summary>
    private static string EscapeIdentifier(string identifier) => identifier.Replace("]", "]]");

    /// <summary>文字列リテラル内の "'" をエスケープする</summary>
    private static string EscapeStringLiteral(string value) => value.Replace("'", "''");
}
