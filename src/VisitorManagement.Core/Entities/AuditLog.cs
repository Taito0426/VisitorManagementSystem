using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Entities;

/// <summary>
/// 操作監査ログ（設計書 9.2.8節 AuditLog）。
/// マスタ・運用設定の変更を記録する汎用ログ。DB切替設定専用の SystemConfigHistory は廃止済み。
/// </summary>
public sealed class AuditLog
{
    /// <summary>主キー（IDENTITY）</summary>
    public long AuditLogId { get; set; }

    /// <summary>変更対象（例: Sites, Companies, Operators, MailSettings）</summary>
    public string TargetEntity { get; set; } = string.Empty;

    /// <summary>変更対象レコードのキー値</summary>
    public string? TargetId { get; set; }

    /// <summary>1:登録 2:更新 3:削除</summary>
    public ChangeType ChangeType { get; set; }

    /// <summary>変更前の値（JSON等）</summary>
    public string? OldValue { get; set; }

    /// <summary>変更後の値（JSON等）</summary>
    public string? NewValue { get; set; }

    /// <summary>変更日時（UTC）</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>変更を行った操作ユーザー（外部キー→Operators）</summary>
    public int ChangedByOperatorId { get; set; }
}
