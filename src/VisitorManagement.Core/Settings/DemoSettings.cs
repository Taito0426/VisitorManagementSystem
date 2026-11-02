namespace VisitorManagement.Core.Settings;

/// <summary>
/// Demo セクション（設計書 19章・付録B）。
/// Enabled=true でもDatabase:Providerは "SqlExpress" のまま変更せず、接続先のみ切り替える。
/// </summary>
public sealed class DemoSettings
{
    /// <summary>設定セクション名</summary>
    public const string SectionName = "Demo";

    /// <summary>デモ版のみ true にする。商用構成では常に false</summary>
    public bool Enabled { get; set; }

    /// <summary>デモ用データベース（VisitorMgmtDemo）への接続文字列</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>初回起動時にサンプルデータを自動投入するか（冪等処理）</summary>
    public bool SeedOnFirstRun { get; set; } = true;

    /// <summary>WinForms管理画面（管理者ロール専用）からのリセットを許可するか</summary>
    public bool AllowResetFromAdminMenu { get; set; } = true;
}
