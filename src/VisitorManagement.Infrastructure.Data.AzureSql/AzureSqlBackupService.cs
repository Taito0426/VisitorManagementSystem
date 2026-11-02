using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Data.AzureSql;

/// <summary>
/// Azure SQL Database 用のバックアップサービス（設計書 10.2節）。
/// 自動バックアップ（フル/差分/ログ）は標準機能として提供されるため、追加の実装は不要である。
/// 本クラスは設定値の参照と、管理画面での運用方針表示のみを担当する。
/// </summary>
public sealed class AzureSqlBackupService(
    IOptions<BackupSettings> backupOptions,
    ILogger<AzureSqlBackupService> logger) : IBackupService
{
    /// <inheritdoc />
    public string ProviderName => DatabaseProviderNames.AzureSql;

    /// <inheritdoc />
    public string DescribeBackupPolicy()
    {
        var settings = backupOptions.Value.AzureSql;
        var ltr = settings.LongTermRetentionEnabled ? "有効" : "無効";
        return "Azure SQL Database の自動バックアップ（Microsoft管理）を利用。"
             + $" ポイントインタイムリストア保持期間: {settings.PitrRetentionDays}日 / 長期保存(LTR): {ltr}";
    }

    /// <inheritdoc />
    public Task<BackupResult> ExecuteBackupAsync(CancellationToken cancellationToken = default)
    {
        // 独自のバックアップ処理は実装しない（設計書 10章冒頭）。
        // リストアは Azure Portal / Azure CLI からポイントインタイムリストアを実行する。
        logger.LogInformation("Azure SQL Database では自動バックアップを利用するため、アプリからのバックアップ実行は行いません。");

        return Task.FromResult(new BackupResult(
            false,
            "Azure SQL Database は自動バックアップ（標準機能）を利用するため、アプリケーションからのバックアップ実行は行いません。"
            + "リストアは Azure Portal または Azure CLI からポイントインタイムリストアを実行してください。",
            null));
    }
}
