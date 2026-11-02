namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// リトライ方針（設計書 4.3.1節。Azure SQLの一時的な接続断/スロットリングに対応）。
/// </summary>
public interface IDbRetryPolicy
{
    /// <summary>戻り値のある操作をリトライ方針に従って実行する</summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation);
}
