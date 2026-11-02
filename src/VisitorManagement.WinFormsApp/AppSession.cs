using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp;

/// <summary>
/// ログイン中の操作ユーザーを保持するセッション。
/// 各画面はこのコンテキストを用いて権限判定・閲覧範囲の絞り込みを行う（設計書 8.2節・11.6節）。
/// </summary>
public sealed class AppSession
{
    private OperatorContext? _current;

    /// <summary>ログイン中の操作ユーザー（未ログイン時は例外）</summary>
    public OperatorContext Current => _current
        ?? throw new InvalidOperationException("ログインしていません。");

    /// <summary>ログイン済みかどうか</summary>
    public bool IsAuthenticated => _current is not null;

    /// <summary>ログイン成功時に呼び出す</summary>
    public void SignIn(OperatorContext context) => _current = context;

    /// <summary>ログアウト時に呼び出す</summary>
    public void SignOut() => _current = null;
}
