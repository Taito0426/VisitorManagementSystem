namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// バックアップ運用の抽象化（設計書 4.4節のDI登録・10章）。
/// バックアップは各DB方式の標準機能を活用する方針であり、独自のバックアップ処理は実装しない。
/// SQL Server Express はタスクスケジューラ＋PowerShell、Azure SQL Database は自動バックアップに委ねる。
/// </summary>
public interface IBackupService
{
    /// <summary>"SqlExpress" or "AzureSql"</summary>
    string ProviderName { get; }

    /// <summary>現在のバックアップ運用方式の説明（システム設定確認画面での表示用）</summary>
    string DescribeBackupPolicy();

    /// <summary>
    /// バックアップの実行を要求する。
    /// SQL Server Express 版はタスクスケジューラ登録済みのスクリプトと同等の BACKUP DATABASE を実行し、
    /// Azure SQL Database 版は自動バックアップに委ねるため何も行わない。
    /// </summary>
    Task<BackupResult> ExecuteBackupAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// バックアップ実行結果。
/// </summary>
/// <param name="Executed">実際にバックアップ処理を実行したか（Azure SQL は常に false）</param>
/// <param name="Message">結果メッセージ</param>
/// <param name="BackupFilePath">出力したバックアップファイルのパス（該当しない場合は null）</param>
public readonly record struct BackupResult(bool Executed, string Message, string? BackupFilePath);
