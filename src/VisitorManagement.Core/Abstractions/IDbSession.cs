using System.Data;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// 複数テーブルにまたがる更新を1トランザクションで扱うためのDBセッション。
/// 接続の取得は必ず <see cref="IDbConnectionFactory"/> 経由で行う（設計書 4.3節）。
/// </summary>
public interface IDbSession : IDisposable
{
    /// <summary>開いた状態の接続</summary>
    IDbConnection Connection { get; }

    /// <summary>トランザクション（開始していない場合は null）</summary>
    IDbTransaction? Transaction { get; }

    /// <summary>トランザクションをコミットする</summary>
    void Commit();

    /// <summary>トランザクションをロールバックする</summary>
    void Rollback();
}

/// <summary>
/// DBセッションの生成ファクトリ。
/// </summary>
public interface IDbSessionFactory
{
    /// <summary>セッションを生成する</summary>
    /// <param name="beginTransaction">true のときトランザクションを開始する</param>
    IDbSession CreateSession(bool beginTransaction = false);
}
