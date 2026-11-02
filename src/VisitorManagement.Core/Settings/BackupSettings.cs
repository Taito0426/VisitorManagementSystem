namespace VisitorManagement.Core.Settings;

/// <summary>
/// Backup セクション（設計書 10.1節・10.2節・付録B）。
/// </summary>
public sealed class BackupSettings
{
    /// <summary>設定セクション名</summary>
    public const string SectionName = "Backup";

    /// <summary>SQL Server Express のバックアップ設定</summary>
    public SqlExpressBackupSettings SqlExpress { get; set; } = new();

    /// <summary>Azure SQL Database のバックアップ設定（自動バックアップの確認用）</summary>
    public AzureSqlBackupSettings AzureSql { get; set; } = new();
}

/// <summary>
/// SQL Server Express のバックアップ設定（タスクスケジューラ＋PowerShell運用）。
/// </summary>
public sealed class SqlExpressBackupSettings
{
    /// <summary>実行時刻（"HH:mm"）</summary>
    public string DailyTime { get; set; } = "23:00";

    /// <summary>保管期間（日）</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>バックアップファイルの出力先</summary>
    public string Path { get; set; } = @"D:\Backup\";
}

/// <summary>
/// Azure SQL Database のバックアップ設定（標準機能をそのまま利用するため参照専用）。
/// </summary>
public sealed class AzureSqlBackupSettings
{
    /// <summary>ポイントインタイムリストアの保持日数</summary>
    public int PitrRetentionDays { get; set; } = 7;

    /// <summary>長期保存（LTR）を有効にするか</summary>
    public bool LongTermRetentionEnabled { get; set; }
}
