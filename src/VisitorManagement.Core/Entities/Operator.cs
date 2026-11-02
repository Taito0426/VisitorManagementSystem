using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Entities;

/// <summary>
/// 操作ユーザー（設計書 9.2.2節 Operators）。
/// RoleType・SiteId はいずれも登録時必須項目であり、未設定状態を発生させない。
/// </summary>
public sealed class Operator
{
    /// <summary>主キー（IDENTITY）</summary>
    public int OperatorId { get; set; }

    /// <summary>ログインID（一意制約あり）</summary>
    public string LoginId { get; set; } = string.Empty;

    /// <summary>パスワードのハッシュ値（ソルト付きPBKDF2。平文保存は行わない）</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>表示名</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>予約登録者宛メールの送信先（必須項目）</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>権限ロール（登録時必須）</summary>
    public RoleType RoleType { get; set; } = RoleType.General;

    /// <summary>所属拠点（登録時必須。NULL不可）</summary>
    public int SiteId { get; set; }

    /// <summary>false のとき無効化されたアカウント</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>登録日時（UTC）</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>所属拠点名（照会用の非永続項目。JOIN結果を格納する）</summary>
    public string? SiteName { get; set; }
}
