using VisitorManagement.Core.Abstractions;

namespace VisitorManagement.Infrastructure.Data;

/// <summary>
/// Repository の共通基底。セッションの生成・破棄とリトライ方針の適用を1か所に集約する。
/// SQL文はDB方式に依らず共通であり（設計書 4.1節・4.5節）、
/// 差分は <see cref="IDbConnectionFactory"/> / <see cref="IDbRetryPolicy"/> の実装に閉じている。
/// </summary>
public abstract class RepositoryBase(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
{
    /// <summary>
    /// セッションを解決して操作を実行する。
    /// 呼び出し側からセッション（トランザクション）が渡された場合は、
    /// 途中でのリトライがトランザクションの整合性を損なうためリトライを適用せずそのまま実行する。
    /// </summary>
    protected async Task<T> ExecuteAsync<T>(IDbSession? session, Func<IDbSession, Task<T>> operation)
    {
        if (session is not null)
        {
            return await operation(session).ConfigureAwait(false);
        }

        return await retryPolicy.ExecuteAsync(async () =>
        {
            using var ownedSession = sessionFactory.CreateSession();
            return await operation(ownedSession).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 戻り値のない操作を実行する。
    /// </summary>
    protected async Task ExecuteAsync(IDbSession? session, Func<IDbSession, Task> operation)
    {
        await ExecuteAsync(session, async s =>
        {
            await operation(s).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }
}
