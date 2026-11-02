using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Application.Services;

/// <summary>
/// 履歴管理機能（設計書 6.8節）。
/// 入退館履歴・メール送信履歴の検索とCSV出力を行う。
/// DB上の保持期間は直近3か月であり、それ以前はアーカイブ済CSVの案内を表示する。
/// </summary>
public sealed class HistoryService(
    IAccessLogRepository accessLogRepository,
    IMailHistoryRepository mailHistoryRepository,
    IOptions<LogArchiveSettings> logArchiveOptions)
{
    /// <summary>入退館履歴を検索する（閲覧範囲はロールに応じて自動的に絞り込む）</summary>
    public Task<IReadOnlyList<AccessLog>> SearchAccessLogsAsync(AccessLogSearchCondition condition, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(ctx);

        PermissionPolicy.ApplyViewScope(ctx, condition);
        return accessLogRepository.SearchAsync(condition);
    }

    /// <summary>メール送信履歴を検索する（閲覧範囲はロールに応じて自動的に絞り込む）</summary>
    public Task<IReadOnlyList<MailHistory>> SearchMailHistoryAsync(MailHistorySearchCondition condition, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(ctx);

        PermissionPolicy.ApplyViewScope(ctx, condition);
        return mailHistoryRepository.SearchAsync(condition);
    }

    /// <summary>
    /// 検索条件がDBの保持期間（直近3か月）より前を含む場合に、
    /// アーカイブ済CSVの保管場所を案内するメッセージを返す（設計書 6.8節）。該当しない場合は null。
    /// </summary>
    public string? BuildArchiveNotice(DateOnly? dateFrom)
    {
        var settings = logArchiveOptions.Value;
        var retentionStart = DateOnly.FromDateTime(DateTime.Today.AddMonths(-settings.DbRetentionMonths));

        if (dateFrom is null || dateFrom.Value >= retentionStart)
        {
            return null;
        }

        var location = string.Equals(settings.StorageMode, LogArchiveStorageModes.AzureBlob, StringComparison.OrdinalIgnoreCase)
            ? $"Azure Blob Storage のコンテナー「{settings.AzureBlob.ContainerName}」"
            : settings.Local.ExportPath;

        return $"データベース上の履歴保持期間は直近{settings.DbRetentionMonths}か月"
             + $"（{retentionStart:yyyy/MM/dd} 以降）です。"
             + $"それ以前の履歴は月次バッチでCSVへアーカイブ済みです。保管場所: {location}";
    }

    /// <summary>入退館履歴をCSV形式で出力する</summary>
    public static string ToCsv(IReadOnlyList<AccessLog> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);

        var sb = new StringBuilder();
        sb.AppendLine("受付日時,拠点,来館者氏名,会社名,区分,端末情報,備考");

        foreach (var log in logs)
        {
            sb.AppendLine(string.Join(',',
                Escape(ToLocalText(log.AccessDateTime)),
                Escape(log.SiteName),
                Escape(log.VisitorName),
                Escape(log.CompanyName),
                Escape(ToText(log.AccessType)),
                Escape(log.DeviceInfo),
                Escape(log.OperatorNote)));
        }

        return sb.ToString();
    }

    /// <summary>メール送信履歴をCSV形式で出力する</summary>
    public static string ToCsv(IReadOnlyList<MailHistory> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);

        var sb = new StringBuilder();
        sb.AppendLine("送信日時,来館者氏名,会社名,メール種別,宛先区分,送信方式,結果,エラー内容");

        foreach (var history in histories)
        {
            sb.AppendLine(string.Join(',',
                Escape(ToLocalText(history.SentAt)),
                Escape(history.VisitorName),
                Escape(history.CompanyName),
                Escape(ToText(history.MailType)),
                Escape(ToText(history.RecipientType)),
                Escape(history.Provider == MailProviderType.SendGrid ? "SendGrid" : "SMTP"),
                Escape(history.Status == MailSendStatus.Succeeded ? "成功" : "失敗"),
                Escape(history.ErrorMessage)));
        }

        return sb.ToString();
    }

    /// <summary>UTCで保持している日時をローカル時刻の表示文字列へ変換する</summary>
    public static string ToLocalText(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime()
            .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>入退館区分の表示名</summary>
    public static string ToText(AccessType accessType) => accessType switch
    {
        AccessType.CheckIn => "入館",
        AccessType.CheckOut => "退館",
        _ => "エラー",
    };

    /// <summary>メール種別の表示名</summary>
    public static string ToText(MailType mailType) => mailType switch
    {
        MailType.VisitInvitation => "来館案内",
        MailType.Reminder => "リマインド",
        MailType.CancellationNotice => "キャンセル通知",
        _ => "再送信",
    };

    /// <summary>宛先区分の表示名</summary>
    public static string ToText(MailRecipientType recipientType)
        => recipientType == MailRecipientType.Invitee ? "招待者（代表者）" : "予約登録者";

    /// <summary>予約ステータスの表示名</summary>
    public static string ToText(VisitorStatus status) => status switch
    {
        VisitorStatus.NotArrived => "未入館",
        VisitorStatus.CheckedIn => "入館中",
        VisitorStatus.CheckedOut => "退館済",
        _ => "キャンセル",
    };

    /// <summary>権限ロールの表示名</summary>
    public static string ToText(RoleType roleType) => roleType switch
    {
        RoleType.Administrator => "管理者",
        RoleType.Reception => "受付",
        _ => "一般",
    };

    /// <summary>CSVのフィールドをエスケープする（カンマ・改行・引用符を含む場合は引用符で囲む）</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
