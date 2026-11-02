namespace VisitorManagement.Core.Enums;

/// <summary>
/// 監査ログの変更区分（設計書 9.2.8節 / AuditLog.ChangeType）。
/// </summary>
public enum ChangeType : byte
{
    /// <summary>登録</summary>
    Insert = 1,

    /// <summary>更新</summary>
    Update = 2,

    /// <summary>削除</summary>
    Delete = 3,
}
