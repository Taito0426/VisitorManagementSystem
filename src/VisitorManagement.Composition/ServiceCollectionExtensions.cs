using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;
using VisitorManagement.Infrastructure.Data;
using VisitorManagement.Infrastructure.Data.AzureSql;
using VisitorManagement.Infrastructure.Data.Repositories;
using VisitorManagement.Infrastructure.Data.SqlExpress;
using VisitorManagement.Infrastructure.Mail;
using VisitorManagement.Infrastructure.Printing;

namespace VisitorManagement.Composition;

/// <summary>
/// 依存性注入の登録（設計書 4.4節）。
/// WinForms・ASP.NET Core の双方から同一の登録ロジックを呼び出す。
///
/// Database:Provider の値に応じて IDbConnectionFactory / IDbRetryPolicy / IBackupService の
/// 具象実装を切り替える。業務ロジック層（Repository実装・サービス層・画面）は
/// インターフェースのみに依存するため、Provider変更時の改修は発生しない。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 来館者記録管理システムの全サービスを登録する。
    /// </summary>
    public static IServiceCollection AddVisitorManagement(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Dapper の型マッピング（DateOnly / TimeOnly / UTC DateTime）を有効化する
        DapperConfiguration.Configure();

        services.AddVisitorManagementSettings(configuration);
        services.AddDatabaseProvider(configuration);
        services.AddRepositories();
        services.AddMailProvider(configuration);
        services.AddPrintingProvider(configuration);
        services.AddArchiveStorage(configuration);
        services.AddApplicationServices();

        return services;
    }

    /// <summary>
    /// ログアーカイブCSVの保管先を登録する（設計書 10.4節）。
    /// オンプレ版はローカルディスク、クラウド版は Azure Blob Storage。
    /// </summary>
    private static IServiceCollection AddArchiveStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var storageMode = configuration[$"{LogArchiveSettings.SectionName}:StorageMode"];

        if (string.Equals(storageMode, LogArchiveStorageModes.AzureBlob, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IArchiveStorage, AzureBlobArchiveStorage>();
        }
        else
        {
            services.AddSingleton<IArchiveStorage, LocalArchiveStorage>();
        }

        return services;
    }

    /// <summary>appsettings.json の各セクションを Options としてバインドする</summary>
    private static IServiceCollection AddVisitorManagementSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<MailSettings>(configuration.GetSection(MailSettings.SectionName));
        services.Configure<PrintingSettings>(configuration.GetSection(PrintingSettings.SectionName));
        services.Configure<DemoSettings>(configuration.GetSection(DemoSettings.SectionName));
        services.Configure<BackupSettings>(configuration.GetSection(BackupSettings.SectionName));
        services.Configure<LogArchiveSettings>(configuration.GetSection(LogArchiveSettings.SectionName));

        return services;
    }

    /// <summary>
    /// DB方式に応じた実装を登録する（設計書 4.4節の中核）。
    /// ビルドの出し分けは行わず、同一バイナリで両対応する。
    /// </summary>
    private static IServiceCollection AddDatabaseProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration[$"{DatabaseSettings.SectionName}:Provider"];

        if (string.Equals(provider, DatabaseProviderNames.AzureSql, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDbConnectionFactory, AzureSqlConnectionFactory>();
            services.AddSingleton<IDbRetryPolicy, AzureSqlRetryPolicy>();
            services.AddSingleton<IBackupService, AzureSqlBackupService>();
        }
        else
        {
            // "SqlExpress"（既定値）。デモ版も本実装をそのまま使用する（設計書 19.2節）
            services.AddSingleton<IDbConnectionFactory, SqlExpressConnectionFactory>();
            services.AddSingleton<IDbRetryPolicy, NoRetryPolicy>();
            services.AddSingleton<IBackupService, SqlExpressBackupService>();
        }

        services.AddSingleton<IDbSessionFactory, DbSessionFactory>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        return services;
    }

    /// <summary>Repository 実装を登録する（SQL文はDB方式に依らず共通）</summary>
    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddSingleton<IVisitorRepository, VisitorRepository>();
        services.AddSingleton<IQrTokenRepository, QrTokenRepository>();
        services.AddSingleton<IAccessLogRepository, AccessLogRepository>();
        services.AddSingleton<IMailHistoryRepository, MailHistoryRepository>();
        services.AddSingleton<ISiteRepository, SiteRepository>();
        services.AddSingleton<IOperatorRepository, OperatorRepository>();
        services.AddSingleton<ICompanyRepository, CompanyRepository>();
        services.AddSingleton<IAuditLogRepository, AuditLogRepository>();

        return services;
    }

    /// <summary>
    /// メール送信方式に応じた実装を登録する（設計書 5.1節）。
    ///
    /// 本番以外（デモ版＝Demo:Enabled=true、またはDebugビルド）ではメモリ内モックを使用し、
    /// 実際の外部送信は行わない（設計書 19.2節）。
    /// Debugビルドで本番のメール設定が入っていても、開発中に実在の宛先へ誤送信しないようにするため。
    /// 送信履歴（MailHistory）へは本番と同じく記録し、画面の送信結果のみ「テスト」と表示する。
    /// </summary>
    private static IServiceCollection AddMailProvider(this IServiceCollection services, IConfiguration configuration)
    {
        // メール定型文の組み立て。送信方式に依らず共通で、PDFプレビューの文面描画からも参照する
        services.AddSingleton<IMailBodyComposer, MailBodyComposer>();

        var demoEnabled = configuration.GetValue<bool>($"{DemoSettings.SectionName}:Enabled");
#if DEBUG
        const bool isDebugBuild = true;
#else
        const bool isDebugBuild = false;
#endif

        if (demoEnabled || isDebugBuild)
        {
            // 履歴照会画面から送信内容を確認できるよう、同一インスタンスを共有する
            services.AddSingleton<DemoMailSender>();
            services.AddSingleton<IMailSender>(sp => sp.GetRequiredService<DemoMailSender>());
            return services;
        }

        var provider = configuration[$"{MailSettings.SectionName}:Provider"];
        if (string.Equals(provider, MailProviderNames.Smtp, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMailSender, SmtpMailSender>();
        }
        else
        {
            services.AddSingleton<IMailSender, SendGridMailSender>();
        }

        return services;
    }

    /// <summary>
    /// ラベル印刷方式に応じた実装を登録する（設計書 19.4節）。
    /// 商用構成では "Ql800"（b-PAC SDK経由で実機印刷）、デモ版では "PdfPreview"（A4 PDF）。
    ///
    /// ILabelPdfPreview（レイアウト確認用のA4 PDF生成）は、デモ版に加えて
    /// Debugビルドでも解決できるようにする。実機プリンタを接続しない開発環境でも
    /// ラベルとメール文面のレイアウトを確認できるようにするため。
    /// Releaseビルドの商用構成では登録せず、確認用UIごと現れないようにする。
    /// </summary>
    private static IServiceCollection AddPrintingProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IQrCodeImageGenerator, QrCodeImageGenerator>();
        services.AddSingleton<ILabelPresetProvider, LabelPresetProvider>();

        var provider = configuration[$"{PrintingSettings.SectionName}:Provider"];
        if (string.Equals(provider, PrintingProviderNames.PdfPreview, StringComparison.OrdinalIgnoreCase))
        {
            // PDFプレビュー取得のため、ILabelPdfPreview としても解決できるようにする
            services.AddSingleton<DemoPdfLabelPrinter>();
            services.AddSingleton<ILabelPrinter>(sp => sp.GetRequiredService<DemoPdfLabelPrinter>());
            services.AddSingleton<ILabelPdfPreview>(sp => sp.GetRequiredService<DemoPdfLabelPrinter>());
        }
        else
        {
            services.AddSingleton<ILabelPrinter, Ql800LabelPrinter>();
#if DEBUG
            // 実機印刷構成でも、開発中はPDFでレイアウトを確認できるようにする
            services.AddSingleton<DemoPdfLabelPrinter>();
            services.AddSingleton<ILabelPdfPreview>(sp => sp.GetRequiredService<DemoPdfLabelPrinter>());
#endif
        }

        return services;
    }

    /// <summary>業務サービス層を登録する（DB方式・メール方式に依存しない）</summary>
    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<MailDeliveryService>();
        services.AddSingleton<ReservationService>();
        services.AddSingleton<AccessService>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<MasterDataService>();
        services.AddSingleton<OperatorService>();
        services.AddSingleton<SystemSettingsService>();
        services.AddSingleton<LogArchiveService>();
        services.AddSingleton<DemoDataSeeder>();

        return services;
    }
}
