using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core;

/// <summary>
/// 業務ルールのうち、画面・サービス・テストで共通利用する計算ロジック。
/// </summary>
public static class VisitRules
{
    /// <summary>QRコードの有効開始オフセット（予約時刻の30分前。設計書 6.2節・6.5節）</summary>
    public static readonly TimeSpan ValidFromOffset = TimeSpan.FromMinutes(-30);

    /// <summary>QRコードの有効終了オフセット（予約時刻の2時間後。設計書 6.2節・6.5節）</summary>
    public static readonly TimeSpan ValidToOffset = TimeSpan.FromHours(2);

    /// <summary>
    /// 予約日時（ローカル）からQRトークンの有効期限（UTC）を算出する。
    /// 当日受付（6.6節）では受付時点を基準時刻として呼び出す。
    /// </summary>
    public static DateTime CalculateExpiresAtUtc(DateOnly visitDate, TimeOnly visitTimeFrom)
    {
        var baseLocal = visitDate.ToDateTime(visitTimeFrom, DateTimeKind.Local);
        return baseLocal.Add(ValidToOffset).ToUniversalTime();
    }

    /// <summary>
    /// 予約日時（ローカル）からQRトークンの有効開始時刻（UTC）を算出する。
    /// </summary>
    public static DateTime CalculateValidFromUtc(DateOnly visitDate, TimeOnly visitTimeFrom)
    {
        var baseLocal = visitDate.ToDateTime(visitTimeFrom, DateTimeKind.Local);
        return baseLocal.Add(ValidFromOffset).ToUniversalTime();
    }

    /// <summary>
    /// 受付時間帯（予約時刻の30分前〜2時間後）内かを判定する（設計書 6.5節）。
    /// </summary>
    public static bool IsWithinReceptionWindow(DateOnly visitDate, TimeOnly visitTimeFrom, DateTime nowUtc)
    {
        var from = CalculateValidFromUtc(visitDate, visitTimeFrom);
        var to = CalculateExpiresAtUtc(visitDate, visitTimeFrom);
        return nowUtc >= from && nowUtc <= to;
    }

    /// <summary>
    /// 予約ステータスがキャンセル可能かを判定する（設計書 7.1節。退館済からのキャンセルは不可）。
    /// </summary>
    public static bool CanCancel(VisitorStatus status) =>
        status is VisitorStatus.NotArrived or VisitorStatus.CheckedIn;

    /// <summary>
    /// メール再送信の対象かを判定する（設計書 5.3節。退館済・キャンセルは対象外）。
    /// 当日受付（ReservationType=2）も性質上対象外とする（設計書 6.7節）。
    /// </summary>
    public static bool CanResendMail(VisitorStatus status, ReservationType reservationType) =>
        reservationType == ReservationType.Advance
        && status is VisitorStatus.NotArrived or VisitorStatus.CheckedIn;
}
