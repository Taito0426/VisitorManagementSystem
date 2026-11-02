using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VisitorManagement.Composition;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.WinFormsApp.Forms;
using VisitorManagement.WinFormsApp.Logging;

namespace VisitorManagement.WinFormsApp;

/// <summary>
/// 管理アプリのエントリポイント（設計書 4.4節：ASP.NET Core と共通の DI 登録ロジックを使用する）。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 名前空間 VisitorManagement.Application と衝突するため System.Windows.Forms.Application を明示する
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        ServiceProvider? provider = null;
        try
        {
            provider = BuildServiceProvider();
            RunApplication(provider);
        }
        catch (Exception ex)
        {
            // 起動時の致命的な失敗（設定不備・DB接続不可など）はメッセージを表示して終了する
            MessageBox.Show(
                $"アプリケーションの起動に失敗しました。{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                UiTheme.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            provider?.Dispose();
        }
    }

    /// <summary>設定を読み込み、DIコンテナを構築する</summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            // 導入先ごとの上書き設定（接続文字列等）。リポジトリには含めない
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("VISITORMGMT_")
            .Build();

        // 設定ファイル中の DPAPI 暗号化値（"ENC:" 接頭辞）を復号する（設計書 11.3節）
        DpapiSecretProtector.UnprotectConfiguration(configuration);

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(
                Path.Combine(AppContext.BaseDirectory, "logs"), LogLevel.Information));
        });

        // DB方式・メール方式・印刷方式の切替を含む全サービスを登録する
        services.AddVisitorManagement(configuration);

        services.AddSingleton<AppSession>();
        services.AddSingleton<IConfiguration>(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>初期セットアップ → ログイン → メイン画面の順に進める</summary>
    private static void RunApplication(IServiceProvider provider)
    {
        // デモ版（Demo:Enabled=true）は初回起動時にスキーマ作成とサンプルデータ投入を自動実行する（設計書 19.2節）
        provider.InitializeDemoAsync().GetAwaiter().GetResult();

        // 初回起動時（操作ユーザーが1件も存在しない）は、拠点と初期管理者の登録から始める（設計書 13.1節 ⑤⑥）
        if (!HasAnyOperator(provider))
        {
            using var setupForm = new InitialAdminSetupForm(provider);
            if (setupForm.ShowDialog() != DialogResult.OK)
            {
                return;
            }
        }

        using var loginForm = new LoginForm(provider);
        if (loginForm.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        System.Windows.Forms.Application.Run(new MainForm(provider));
    }

    /// <summary>操作ユーザーが登録済みかを確認する</summary>
    private static bool HasAnyOperator(IServiceProvider provider)
    {
        var operatorRepository = provider.GetRequiredService<IOperatorRepository>();
        var operators = operatorRepository.GetAllAsync(activeOnly: false).GetAwaiter().GetResult();
        return operators.Count > 0;
    }
}
