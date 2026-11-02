using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Composition;

/// <summary>
/// デモ版の起動時初期化（設計書 19.2節・19.3節）。
/// Demo:Enabled=true のとき、初回起動時にスキーマ作成とサンプルデータ投入を自動実行する。
/// いずれの処理も冪等であり、既に初期化済みの環境では何も行わない。
/// 商用構成（Demo:Enabled=false）では即座に戻る。
/// </summary>
public static class DemoStartup
{
    /// <summary>
    /// デモ版の初期化を実行する。WinForms・受付Web の双方の起動処理から呼び出す。
    /// </summary>
    public static async Task InitializeDemoAsync(this IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var demoSettings = provider.GetRequiredService<IOptions<DemoSettings>>().Value;
        if (!demoSettings.Enabled)
        {
            return;
        }

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoStartup));

        // ① スキーマ作成（デモ用DBは svc_visitor に DDL 権限を付与している）
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        if (!await initializer.IsSchemaCreatedAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("デモ用データベースのスキーマを作成します。");
            await initializer.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        }

        // ② サンプルデータ投入（既にデータが存在する場合は再投入しない）
        var seeder = provider.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedIfEmptyAsync().ConfigureAwait(false);
    }
}
