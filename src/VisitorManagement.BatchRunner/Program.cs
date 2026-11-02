using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Application.Services;
using VisitorManagement.Composition;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

// 定時バッチの実行基盤（設計書 10.1節・10.4節）。
// オンプレ版は Windows タスクスケジューラ、クラウド版は Azure Functions／WebJobs から呼び出す。
//
//   VisitorManagement.BatchRunner backup
//   VisitorManagement.BatchRunner log-archive [--month yyyy-MM] [--no-delete] [--only-on-scheduled-day]
//
// 終了コード: 0=正常終了 / 1=引数エラー / 2=実行時エラー

return await BatchRunner.RunAsync(args).ConfigureAwait(false);

internal static class BatchRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        ServiceProvider? provider = null;
        try
        {
            provider = BuildServiceProvider();

            return args[0].ToLowerInvariant() switch
            {
                "backup" => await RunBackupAsync(provider).ConfigureAwait(false),
                "log-archive" => await RunLogArchiveAsync(provider, args).ConfigureAwait(false),
                _ => Fail($"不明なコマンドです: {args[0]}"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"バッチの実行に失敗しました: {ex.Message}");
            Console.Error.WriteLine(ex);
            return 2;
        }
        finally
        {
            if (provider is not null)
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>設定を読み込み、DIコンテナを構築する</summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("VISITORMGMT_")
            .Build();

        // 設定ファイル中の DPAPI 暗号化値（"ENC:" 接頭辞）を復号する（設計書 11.3節）
        DpapiSecretProtector.UnprotectConfiguration(configuration);

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddVisitorManagement(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// バックアップを実行する（設計書 10.1節）。
    /// Azure SQL Database では自動バックアップを利用するため、実行は行わず案内のみ表示する。
    /// </summary>
    private static async Task<int> RunBackupAsync(IServiceProvider provider)
    {
        var backupService = provider.GetRequiredService<IBackupService>();
        Console.WriteLine($"DB方式: {backupService.ProviderName}");
        Console.WriteLine($"運用方針: {backupService.DescribeBackupPolicy()}");

        var result = await backupService.ExecuteBackupAsync().ConfigureAwait(false);
        Console.WriteLine(result.Message);

        if (!result.Executed && backupService.ProviderName == DatabaseProviderNames.SqlExpress)
        {
            // SQL Server Express で実行されなかった場合は失敗とみなす
            return 2;
        }

        if (result.Executed)
        {
            CleanUpOldBackups(provider);
        }

        return 0;
    }

    /// <summary>
    /// ログアーカイブを実行する（設計書 10.4節）。
    /// 前月分を1テーブル・1か月単位でCSV出力し、保持期間を超えたデータをDBから削除する。
    /// </summary>
    private static async Task<int> RunLogArchiveAsync(IServiceProvider provider, string[] args)
    {
        var settings = provider.GetRequiredService<IOptions<LogArchiveSettings>>().Value;

        // 「翌月第1営業日に実行」を実現するため、毎日起動して当日が対象日でなければ何もしない運用に対応する
        if (HasFlag(args, "--only-on-scheduled-day"))
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!LogArchiveService.IsNthBusinessDayOfMonth(today, settings.ExportOnBusinessDayOfMonth))
            {
                Console.WriteLine(
                    $"本日は月内第{settings.ExportOnBusinessDayOfMonth}営業日ではないため、ログアーカイブをスキップします。");
                return 0;
            }
        }

        DateOnly? targetMonth = null;
        if (TryGetOption(args, "--month", out var monthText))
        {
            if (!DateOnly.TryParseExact($"{monthText}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                return Fail($"--month の形式が正しくありません（yyyy-MM）: {monthText}");
            }

            targetMonth = parsed;
        }

        var deleteExpired = !HasFlag(args, "--no-delete");

        var archiveService = provider.GetRequiredService<LogArchiveService>();
        var result = await archiveService.RunAsync(targetMonth, deleteExpired).ConfigureAwait(false);

        Console.WriteLine($"対象月: {result.TargetMonth:yyyy/MM}");
        foreach (var file in result.ExportedFiles)
        {
            Console.WriteLine($"  出力: {file.TableName} {file.RowCount}件 -> {file.Location}");
        }

        if (deleteExpired)
        {
            Console.WriteLine(
                $"  DB削除: AccessLogs {result.DeletedAccessLogs}件 / MailHistory {result.DeletedMailHistories}件");
        }

        return 0;
    }

    /// <summary>保管期間を超えたバックアップファイルを削除する（設計書 10.1節：保管期間30日）</summary>
    private static void CleanUpOldBackups(IServiceProvider provider)
    {
        var settings = provider.GetRequiredService<IOptions<BackupSettings>>().Value.SqlExpress;
        if (!Directory.Exists(settings.Path))
        {
            return;
        }

        var threshold = DateTime.Now.AddDays(-settings.RetentionDays);
        var deleted = 0;

        foreach (var file in Directory.EnumerateFiles(settings.Path, "*.bak"))
        {
            var info = new FileInfo(file);
            if (info.CreationTime >= threshold)
            {
                continue;
            }

            try
            {
                info.Delete();
                deleted++;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"古いバックアップの削除に失敗しました: {file} ({ex.Message})");
            }
        }

        Console.WriteLine($"保管期間（{settings.RetentionDays}日）を超えたバックアップを {deleted} 件削除しました。");
    }

    /// <summary>フラグ引数が指定されているか</summary>
    private static bool HasFlag(string[] args, string name)
        => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>値付きオプションを取得する</summary>
    private static bool TryGetOption(string[] args, string name, out string value)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                value = args[i + 1];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>エラーメッセージを表示して終了コードを返す</summary>
    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }

    /// <summary>使用方法を表示する</summary>
    private static void PrintUsage()
    {
        Console.WriteLine("来館者記録管理システム 定時バッチ");
        Console.WriteLine();
        Console.WriteLine("使用方法:");
        Console.WriteLine("  VisitorManagement.BatchRunner backup");
        Console.WriteLine("      データベースのバックアップを実行し、保管期間を超えたファイルを削除します（設計書 10.1節）。");
        Console.WriteLine();
        Console.WriteLine("  VisitorManagement.BatchRunner log-archive [--month yyyy-MM] [--no-delete] [--only-on-scheduled-day]");
        Console.WriteLine("      入退館履歴・メール送信履歴をCSVへ出力し、保持期間を超えたデータを削除します（設計書 10.4節）。");
        Console.WriteLine("      --month                  出力対象月（既定は前月）");
        Console.WriteLine("      --no-delete              DBからの削除を行わない");
        Console.WriteLine("      --only-on-scheduled-day  当日が設定上の実行営業日でなければ何もしない");
    }
}
