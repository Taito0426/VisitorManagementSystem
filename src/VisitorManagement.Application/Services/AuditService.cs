using System.Text.Json;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.Application.Services;

/// <summary>
/// 操作監査ログの記録（設計書 9.2.8節・11.5節）。
/// マスタ変更・運用設定変更を対象とする。
/// DB方式の切替は非稼働日バッチ運用のため、DB接続設定自体の変更監査は行わない。
/// </summary>
public sealed class AuditService(IAuditLogRepository auditLogRepository)
{
    /// <summary>変更前後の値をJSONとして記録するためのシリアライズ設定</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>登録を記録する</summary>
    public Task RecordInsertAsync<T>(string targetEntity, object? targetId, T newValue, OperatorContext ctx)
        => RecordAsync(targetEntity, targetId, ChangeType.Insert, default(T), newValue, ctx);

    /// <summary>更新を記録する</summary>
    public Task RecordUpdateAsync<T>(string targetEntity, object? targetId, T oldValue, T newValue, OperatorContext ctx)
        => RecordAsync(targetEntity, targetId, ChangeType.Update, oldValue, newValue, ctx);

    /// <summary>削除を記録する</summary>
    public Task RecordDeleteAsync<T>(string targetEntity, object? targetId, T oldValue, OperatorContext ctx)
        => RecordAsync(targetEntity, targetId, ChangeType.Delete, oldValue, default(T), ctx);

    /// <summary>監査ログを検索する</summary>
    public Task<IReadOnlyList<AuditLog>> SearchAsync(
        string? targetEntity, ChangeType? changeType, DateOnly? dateFrom, DateOnly? dateTo, int? maxRows = 1000)
        => auditLogRepository.SearchAsync(targetEntity, changeType, dateFrom, dateTo, maxRows);

    /// <summary>監査ログを1件登録する</summary>
    private Task RecordAsync<T>(
        string targetEntity, object? targetId, ChangeType changeType, T? oldValue, T? newValue, OperatorContext ctx)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntity);
        ArgumentNullException.ThrowIfNull(ctx);

        return auditLogRepository.CreateAsync(new AuditLog
        {
            TargetEntity = targetEntity,
            TargetId = targetId?.ToString(),
            ChangeType = changeType,
            OldValue = Serialize(oldValue),
            NewValue = Serialize(newValue),
            ChangedAt = DateTime.UtcNow,
            ChangedByOperatorId = ctx.OperatorId,
        });
    }

    /// <summary>値をJSONへ変換する（null の場合は記録しない）</summary>
    private static string? Serialize<T>(T? value)
        => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}

/// <summary>
/// 監査ログの対象エンティティ名（設計書 9.2.8節の例に準拠）。
/// </summary>
public static class AuditTargets
{
    /// <summary>拠点マスタ</summary>
    public const string Sites = "Sites";

    /// <summary>会社名マスタ</summary>
    public const string Companies = "Companies";

    /// <summary>操作ユーザー</summary>
    public const string Operators = "Operators";

    /// <summary>メール送信方式等の運用設定</summary>
    public const string MailSettings = "MailSettings";

    /// <summary>デモ用サンプルデータ</summary>
    public const string DemoData = "DemoData";
}
