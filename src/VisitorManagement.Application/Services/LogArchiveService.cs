using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Application.Services;

/// <summary>
/// ログ保持・アーカイブ機能（設計書 10.4節）。
///
/// 入退館履歴（AccessLogs）とメール送信履歴（MailHistory）はDB上に直近3か月分のみを保持する。
/// 翌月の第1営業日に前月分をCSVへ出力し、CSV出力から3か月経過したデータをDBから削除する。
/// 実行はオンプレ版が Windows タスクスケジューラ、クラウド版が Azure Functions／WebJobs による定時バッチ。
/// </summary>
public sealed class LogArchiveService(
    IAccessLogRepository accessLogRepository,
    IMailHistoryRepository mailHistoryRepository,
    IArchiveStorage archiveStorage,
    IOptions<LogArchiveSettings> logArchiveOptions,
    ILogger<LogArchiveService> logger)
{
    /// <summary>
    /// 指定した月のログをCSVへ出力し、保持期間を超えたデータをDBから削除する。
    /// </summary>
    /// <param name="targetMonth">出力対象の月（未指定の場合は前月）</param>
    /// <param name="deleteExpired">保持期間を超えたデータをDBから削除するか</param>
    public async Task<LogArchiveResult> RunAsync(
        DateOnly? targetMonth = null, bool deleteExpired = true, CancellationToken cancellationToken = default)
    {
        var settings = logArchiveOptions.Value;

        // 対象月（既定は前月）のローカル日付範囲をUTCへ変換する
        var monthStartLocal = targetMonth.HasValue
            ? new DateTime(targetMonth.Value.Year, targetMonth.Value.Month, 1, 0, 0, 0, DateTimeKind.Local)
            : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1, 0, 0, 0, DateTimeKind.Local).AddMonths(-1);
        var monthEndLocal = monthStartLocal.AddMonths(1);

        var fromUtc = monthStartLocal.ToUniversalTime();
        var toUtc = monthEndLocal.ToUniversalTime();
        var suffix = monthStartLocal.ToString("yyyyMM", CultureInfo.InvariantCulture);

        var exported = new List<ArchiveFileInfo>();

        // --- ① 対象テーブルを1テーブル・1か月単位でCSVへ出力する ---
        if (settings.TargetTables.Contains("AccessLogs", StringComparer.OrdinalIgnoreCase))
        {
            var logs = await accessLogRepository.GetForArchiveAsync(fromUtc, toUtc).ConfigureAwait(false);
            var location = await archiveStorage
                .SaveAsync($"AccessLogs_{suffix}.csv", HistoryService.ToCsv(logs), cancellationToken).ConfigureAwait(false);
            exported.Add(new ArchiveFileInfo("AccessLogs", location, logs.Count));
        }

        if (settings.TargetTables.Contains("MailHistory", StringComparer.OrdinalIgnoreCase))
        {
            var histories = await mailHistoryRepository.GetForArchiveAsync(fromUtc, toUtc).ConfigureAwait(false);
            var location = await archiveStorage
                .SaveAsync($"MailHistory_{suffix}.csv", HistoryService.ToCsv(histories), cancellationToken).ConfigureAwait(false);
            exported.Add(new ArchiveFileInfo("MailHistory", location, histories.Count));
        }

        // --- ② 保持期間を超えたデータをDBから削除する ---
        var deletedAccessLogs = 0;
        var deletedMailHistories = 0;

        if (deleteExpired)
        {
            var thresholdUtc = CalculateDeleteThresholdUtc(settings.DbRetentionMonths);
            deletedAccessLogs = await accessLogRepository.DeleteOlderThanAsync(thresholdUtc).ConfigureAwait(false);
            deletedMailHistories = await mailHistoryRepository.DeleteOlderThanAsync(thresholdUtc).ConfigureAwait(false);

            logger.LogInformation(
                "保持期間（{Months}か月）を超えた履歴を削除しました。AccessLogs: {AccessCount}件 / MailHistory: {MailCount}件",
                settings.DbRetentionMonths, deletedAccessLogs, deletedMailHistories);
        }

        foreach (var file in exported)
        {
            logger.LogInformation(
                "ログアーカイブを出力しました。テーブル: {Table} / 件数: {Count} / 保管先: {Location}",
                file.TableName, file.RowCount, file.Location);
        }

        return new LogArchiveResult(
            DateOnly.FromDateTime(monthStartLocal), exported, deletedAccessLogs, deletedMailHistories);
    }

    /// <summary>
    /// DBからの削除しきい値（UTC）を算出する。
    /// 当月を含めて <paramref name="retentionMonths"/> か月分をDB上に残す。
    /// 例）保持3か月・4月実行時のしきい値は2月1日となり、1月以前のデータが削除される。
    /// </summary>
    internal static DateTime CalculateDeleteThresholdUtc(int retentionMonths)
    {
        var currentMonthStartLocal = new DateTime(
            DateTime.Today.Year, DateTime.Today.Month, 1, 0, 0, 0, DateTimeKind.Local);

        return currentMonthStartLocal.AddMonths(-(retentionMonths - 1)).ToUniversalTime();
    }

    /// <summary>
    /// 指定日が月内の第N営業日（土日を除く）かを判定する（設計書 10.4節：翌月第1営業日に実行）。
    /// 祝日は判定に含めないため、必要に応じて実行スケジュール側で調整する。
    /// </summary>
    public static bool IsNthBusinessDayOfMonth(DateOnly date, int nth)
    {
        if (nth <= 0)
        {
            return false;
        }

        var businessDayCount = 0;
        for (var day = new DateOnly(date.Year, date.Month, 1); day <= date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            businessDayCount++;
            if (day == date)
            {
                return businessDayCount == nth;
            }
        }

        return false;
    }
}

/// <summary>
/// ログアーカイブの実行結果。
/// </summary>
/// <param name="TargetMonth">出力対象月</param>
/// <param name="ExportedFiles">出力したファイル</param>
/// <param name="DeletedAccessLogs">削除した入退館履歴の件数</param>
/// <param name="DeletedMailHistories">削除したメール送信履歴の件数</param>
public sealed record LogArchiveResult(
    DateOnly TargetMonth,
    IReadOnlyList<ArchiveFileInfo> ExportedFiles,
    int DeletedAccessLogs,
    int DeletedMailHistories);

/// <summary>
/// 出力したアーカイブファイルの情報。
/// </summary>
/// <param name="TableName">対象テーブル</param>
/// <param name="Location">保管先パスまたはURI</param>
/// <param name="RowCount">出力件数</param>
public sealed record ArchiveFileInfo(string TableName, string Location, int RowCount);
