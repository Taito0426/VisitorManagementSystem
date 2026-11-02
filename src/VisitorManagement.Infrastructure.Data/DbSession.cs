using System.Data;
using VisitorManagement.Core.Abstractions;

namespace VisitorManagement.Infrastructure.Data;

/// <summary>
/// <see cref="IDbSession"/> の実装。接続は必ず <see cref="IDbConnectionFactory"/> 経由で取得する
/// （設計書 4.3節：Repository層はDB方式を意識しない）。
/// </summary>
internal sealed class DbSession : IDbSession
{
    private bool _completed;
    private bool _disposed;

    /// <inheritdoc />
    public IDbConnection Connection { get; }

    /// <inheritdoc />
    public IDbTransaction? Transaction { get; private set; }

    public DbSession(IDbConnectionFactory connectionFactory, bool beginTransaction)
    {
        Connection = connectionFactory.CreateOpenConnection();
        if (beginTransaction)
        {
            Transaction = Connection.BeginTransaction();
        }
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (Transaction is null)
        {
            throw new InvalidOperationException("トランザクションが開始されていません。");
        }

        Transaction.Commit();
        _completed = true;
    }

    /// <inheritdoc />
    public void Rollback()
    {
        if (Transaction is null || _completed)
        {
            return;
        }

        Transaction.Rollback();
        _completed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Commit されずに破棄された場合はロールバックして未確定の更新を破棄する
        if (Transaction is not null && !_completed)
        {
            try
            {
                Transaction.Rollback();
            }
            catch (InvalidOperationException)
            {
                // 接続が既に切断されている場合は何もしない
            }
        }

        Transaction?.Dispose();
        Transaction = null;
        Connection.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// <see cref="IDbSessionFactory"/> の実装。
/// </summary>
public sealed class DbSessionFactory(IDbConnectionFactory connectionFactory) : IDbSessionFactory
{
    /// <inheritdoc />
    public IDbSession CreateSession(bool beginTransaction = false)
        => new DbSession(connectionFactory, beginTransaction);
}
