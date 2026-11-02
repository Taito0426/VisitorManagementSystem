namespace VisitorManagement.Core.Settings;

/// <summary>
/// Database セクション（設計書 4.4節・付録B）。
/// Provider の値により IDbConnectionFactory / IDbRetryPolicy / IBackupService の実装を切り替える。
/// </summary>
public sealed class DatabaseSettings
{
    /// <summary>設定セクション名</summary>
    public const string SectionName = "Database";

    /// <summary>"SqlExpress" | "AzureSql"（デモ版も SqlExpress を使用する）</summary>
    public string Provider { get; set; } = DatabaseProviderNames.SqlExpress;

    /// <summary>Provider名をキーとする接続文字列</summary>
    public Dictionary<string, string> ConnectionStrings { get; set; } = [];

    /// <summary>Provider名をキーとするリトライ設定</summary>
    public Dictionary<string, RetryPolicySettings> RetryPolicy { get; set; } = [];
}

/// <summary>
/// リトライ設定（設計書 4.4節・4.6節）。
/// </summary>
public sealed class RetryPolicySettings
{
    /// <summary>最大リトライ回数</summary>
    public int MaxRetryCount { get; set; } = 1;

    /// <summary>最大遅延秒数（指数バックオフの上限）</summary>
    public int MaxRetryDelaySeconds { get; set; } = 1;
}

/// <summary>
/// DB方式の識別子（設計書 4.3.1節 IDbConnectionFactory.ProviderName）。
/// </summary>
public static class DatabaseProviderNames
{
    /// <summary>SQL Server Express（オンプレ版・デモ版）</summary>
    public const string SqlExpress = "SqlExpress";

    /// <summary>Azure SQL Database（クラウド版）</summary>
    public const string AzureSql = "AzureSql";
}
