namespace VisitorManagement.Core.Settings;

/// <summary>
/// LogArchive セクション（設計書 10.4節・付録B）。
/// AccessLogs・MailHistory はDB上3か月保持し、翌月第1営業日に前月分をCSV出力後、
/// 出力から3か月経過時にDBから削除する。
/// </summary>
public sealed class LogArchiveSettings
{
    /// <summary>設定セクション名</summary>
    public const string SectionName = "LogArchive";

    /// <summary>DB上の保持期間（月）</summary>
    public int DbRetentionMonths { get; set; } = 3;

    /// <summary>CSV出力を行う営業日（月内の第N営業日）</summary>
    public int ExportOnBusinessDayOfMonth { get; set; } = 1;

    /// <summary>出力形式</summary>
    public string ExportFormat { get; set; } = "csv";

    /// <summary>"Local"（オンプレ版） | "AzureBlob"（クラウド版）</summary>
    public string StorageMode { get; set; } = LogArchiveStorageModes.Local;

    /// <summary>ローカル保存の設定</summary>
    public LocalArchiveSettings Local { get; set; } = new();

    /// <summary>Azure Blob Storage の設定</summary>
    public AzureBlobArchiveSettings AzureBlob { get; set; } = new();

    /// <summary>アーカイブ対象テーブル</summary>
    public List<string> TargetTables { get; set; } = ["AccessLogs", "MailHistory"];
}

/// <summary>ローカルディスクへのアーカイブ設定（オンプレ版）。</summary>
public sealed class LocalArchiveSettings
{
    /// <summary>CSV出力先フォルダ</summary>
    public string ExportPath { get; set; } = @"D:\LogArchive\";
}

/// <summary>Azure Blob Storage へのアーカイブ設定（クラウド版）。</summary>
public sealed class AzureBlobArchiveSettings
{
    /// <summary>接続文字列</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>コンテナ名</summary>
    public string ContainerName { get; set; } = "log-archive";
}

/// <summary>
/// アーカイブ保管先の識別子。
/// </summary>
public static class LogArchiveStorageModes
{
    /// <summary>ローカルディスク（オンプレ版）</summary>
    public const string Local = "Local";

    /// <summary>Azure Blob Storage（クラウド版）</summary>
    public const string AzureBlob = "AzureBlob";
}
