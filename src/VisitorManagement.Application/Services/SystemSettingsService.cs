using Microsoft.Extensions.Options;
using VisitorManagement.Application.Exceptions;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Security;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Application.Services;

/// <summary>
/// システム設定管理機能（設計書 6.13節。管理者専用・読取専用）。
/// DB接続方式・メール送信方式の現在値を参照できる。
/// 値の変更は設定ファイル編集＋アプリケーション再起動を要し、
/// 非稼働日に切替バッチを実行する運用とする（設計書 13.3節）。稼働中の切替はサポート対象外。
/// </summary>
public sealed class SystemSettingsService(
    IDbConnectionFactory connectionFactory,
    IBackupService backupService,
    IMailSender mailSender,
    IOptions<DatabaseSettings> databaseOptions,
    IOptions<MailSettings> mailOptions,
    IOptions<PrintingSettings> printingOptions,
    IOptions<DemoSettings> demoOptions,
    IOptions<LogArchiveSettings> logArchiveOptions,
    ILabelPresetProvider labelPresetProvider)
{
    /// <summary>現在のシステム設定を取得する（管理者専用）</summary>
    public SystemSettingsView GetCurrentSettings(OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!PermissionPolicy.CanViewSystemSettings(ctx))
        {
            throw PermissionDeniedException.ForOperation("システム設定確認");
        }

        var mail = mailOptions.Value;
        var demo = demoOptions.Value;
        var archive = logArchiveOptions.Value;

        return new SystemSettingsView
        {
            DatabaseProvider = connectionFactory.ProviderName,
            DatabaseProviderDisplayName = ToDatabaseDisplayName(connectionFactory.ProviderName),
            RetryPolicyDescription = DescribeRetryPolicy(),
            BackupPolicyDescription = backupService.DescribeBackupPolicy(),
            MailProvider = mail.Provider,
            MailSenderTypeName = mailSender.GetType().Name,
            MailFromAddress = string.Equals(mail.Provider, MailProviderNames.Smtp, StringComparison.OrdinalIgnoreCase)
                ? mail.Smtp.User
                : mail.SendGrid.FromAddress,
            PrintingProvider = printingOptions.Value.Provider,
            LabelPresets = labelPresetProvider.GetAllPresets(),
            DemoEnabled = demo.Enabled,
            LogArchiveStorageMode = archive.StorageMode,
            LogArchiveLocation = string.Equals(archive.StorageMode, LogArchiveStorageModes.AzureBlob, StringComparison.OrdinalIgnoreCase)
                ? $"Azure Blob Storage / コンテナー: {archive.ContainerNameOrEmpty()}"
                : archive.Local.ExportPath,
            LogArchiveRetentionMonths = archive.DbRetentionMonths,
        };
    }

    /// <summary>現在のリトライ方針を説明する文字列を組み立てる</summary>
    private string DescribeRetryPolicy()
    {
        var provider = connectionFactory.ProviderName;
        if (!databaseOptions.Value.RetryPolicy.TryGetValue(provider, out var retry))
        {
            return "設定なし";
        }

        return provider == DatabaseProviderNames.AzureSql
            ? $"指数バックオフによるリトライ（最大{retry.MaxRetryCount}回 / 最大遅延{retry.MaxRetryDelaySeconds}秒）"
            : "リトライなし（即時に例外を送出）";
    }

    /// <summary>DB方式の表示名</summary>
    private static string ToDatabaseDisplayName(string providerName) => providerName switch
    {
        DatabaseProviderNames.AzureSql => "Azure SQL Database（クラウド版）",
        _ => "SQL Server Express（オンプレ版）",
    };
}

/// <summary>
/// システム設定確認画面の表示内容（設計書 8.1節 画面No.7）。
/// </summary>
public sealed class SystemSettingsView
{
    /// <summary>DB方式（"SqlExpress" | "AzureSql"）</summary>
    public string DatabaseProvider { get; init; } = string.Empty;

    /// <summary>DB方式の表示名</summary>
    public string DatabaseProviderDisplayName { get; init; } = string.Empty;

    /// <summary>リトライ方針の説明</summary>
    public string RetryPolicyDescription { get; init; } = string.Empty;

    /// <summary>バックアップ運用の説明</summary>
    public string BackupPolicyDescription { get; init; } = string.Empty;

    /// <summary>メール送信方式（"SendGrid" | "Smtp"）</summary>
    public string MailProvider { get; init; } = string.Empty;

    /// <summary>実際に使用されているメール送信実装のクラス名</summary>
    public string MailSenderTypeName { get; init; } = string.Empty;

    /// <summary>差出人メールアドレス</summary>
    public string MailFromAddress { get; init; } = string.Empty;

    /// <summary>ラベル印刷方式（"Ql800" | "PdfPreview"）</summary>
    public string PrintingProvider { get; init; } = string.Empty;

    /// <summary>ラベルプリセットの設定状況</summary>
    public IReadOnlyList<Core.Dtos.LabelPreset> LabelPresets { get; init; } = [];

    /// <summary>デモ機能が有効か</summary>
    public bool DemoEnabled { get; init; }

    /// <summary>ログアーカイブの保管先種別</summary>
    public string LogArchiveStorageMode { get; init; } = string.Empty;

    /// <summary>ログアーカイブの保管場所</summary>
    public string LogArchiveLocation { get; init; } = string.Empty;

    /// <summary>DB上のログ保持期間（月）</summary>
    public int LogArchiveRetentionMonths { get; init; }
}

/// <summary>
/// <see cref="LogArchiveSettings"/> の表示補助。
/// </summary>
internal static class LogArchiveSettingsExtensions
{
    /// <summary>Azure Blob のコンテナー名（未設定時は空文字）</summary>
    public static string ContainerNameOrEmpty(this LogArchiveSettings settings)
        => settings.AzureBlob.ContainerName ?? string.Empty;
}
