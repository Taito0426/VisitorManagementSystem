using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Data.AzureSql;

/// <summary>
/// Azure SQL Database 用のリトライ方針（設計書 4.6節）。
/// ネットワーク越しの接続断・スロットリング（DTU/vCore上限）による一時エラーに対し、
/// Polly を用いた指数バックオフ方式のリトライ（既定5回、最大遅延10秒）を行う。
/// リトライ上限を超えた場合はアプリケーション層へ例外を伝播する。
/// </summary>
public sealed class AzureSqlRetryPolicy : IDbRetryPolicy
{
    /// <summary>
    /// Azure SQL Database の一時的なエラーを示すエラー番号。
    /// （接続断・スロットリング・フェイルオーバー中など。恒久的なエラーはリトライしない）
    /// </summary>
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        -2,     // タイムアウト
        20,     // インスタンスへの接続確立エラー
        64,     // 接続確立中のトランスポートレベルエラー
        233,    // 共有メモリプロバイダのパイプ切断
        10053,  // トランスポートレベルエラー（送信中）
        10054,  // 既存の接続がリモートホストにより強制切断
        10060,  // ネットワーク関連またはインスタンス固有のエラー
        40197,  // サービスによる要求処理エラー（アップグレード/フェイルオーバー）
        40501,  // サービスがビジー（スロットリング）
        40613,  // データベースが現在利用不可
        49918,  // 要求処理に十分なリソースがない
        49919,  // 作成/更新要求が多すぎる
        49920,  // 処理中の要求が多すぎる
        4060,   // 要求されたデータベースを開けない
        4221,   // セカンダリレプリカのリカバリ待ち
    ];

    private readonly ResiliencePipeline _pipeline;

    public AzureSqlRetryPolicy(IOptions<DatabaseSettings> databaseOptions, ILogger<AzureSqlRetryPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(databaseOptions);

        var settings = databaseOptions.Value.RetryPolicy.TryGetValue(DatabaseProviderNames.AzureSql, out var retry)
            ? retry
            : new RetryPolicySettings { MaxRetryCount = 5, MaxRetryDelaySeconds = 10 };

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SqlException>(IsTransient)
                                                     .Handle<TimeoutException>(),
                MaxRetryAttempts = settings.MaxRetryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(settings.MaxRetryDelaySeconds),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Azure SQL Database への操作が一時的に失敗しました。リトライします（{Attempt}回目 / 待機 {Delay}）。",
                        args.AttemptNumber + 1,
                        args.RetryDelay);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return await _pipeline.ExecuteAsync(
            static async (state, _) => await state().ConfigureAwait(false),
            operation).ConfigureAwait(false);
    }

    /// <summary>一時的なエラーかどうかを判定する</summary>
    private static bool IsTransient(SqlException exception)
        => exception.Errors.Cast<SqlError>().Any(e => TransientErrorNumbers.Contains(e.Number));
}
