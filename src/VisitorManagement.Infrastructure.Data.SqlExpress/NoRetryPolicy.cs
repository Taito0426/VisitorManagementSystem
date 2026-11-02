using VisitorManagement.Core.Abstractions;

namespace VisitorManagement.Infrastructure.Data.SqlExpress;

/// <summary>
/// SQL Server Express 用のリトライ方針（設計書 4.6節）。
/// 社内LAN接続のため一時的な接続断は稀と想定し、リトライを行わず即時に例外を送出する。
/// </summary>
public sealed class NoRetryPolicy : IDbRetryPolicy
{
    /// <inheritdoc />
    public Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation();
    }
}
