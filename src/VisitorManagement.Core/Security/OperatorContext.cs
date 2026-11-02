using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Security;

/// <summary>
/// ログイン中の操作ユーザー情報。認証後の認可判定（設計書 11.6節）に使用する。
/// </summary>
public sealed class OperatorContext
{
    /// <summary>操作ユーザーID</summary>
    public int OperatorId { get; }

    /// <summary>ログインID</summary>
    public string LoginId { get; }

    /// <summary>表示名</summary>
    public string DisplayName { get; }

    /// <summary>メールアドレス</summary>
    public string Email { get; }

    /// <summary>権限ロール</summary>
    public RoleType RoleType { get; }

    /// <summary>所属拠点（登録時必須のため常に有効値）</summary>
    public int SiteId { get; }

    /// <summary>所属拠点名</summary>
    public string? SiteName { get; }

    /// <summary>Operators エンティティからコンテキストを生成する</summary>
    public OperatorContext(Operator op)
    {
        ArgumentNullException.ThrowIfNull(op);

        OperatorId = op.OperatorId;
        LoginId = op.LoginId;
        DisplayName = op.DisplayName;
        Email = op.Email;
        RoleType = op.RoleType;
        SiteId = op.SiteId;
        SiteName = op.SiteName;
    }

    /// <summary>管理者ロールか</summary>
    public bool IsAdministrator => RoleType == RoleType.Administrator;

    /// <summary>受付ロールか</summary>
    public bool IsReception => RoleType == RoleType.Reception;

    /// <summary>一般ロールか</summary>
    public bool IsGeneral => RoleType == RoleType.General;
}
