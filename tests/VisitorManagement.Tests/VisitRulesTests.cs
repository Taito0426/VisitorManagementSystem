using VisitorManagement.Core;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Tests;

/// <summary>
/// 受付時間ルールと状態遷移ルールのテスト（設計書 6.2節・6.5節・7.1節）。
/// </summary>
public sealed class VisitRulesTests
{
    private static readonly DateOnly VisitDate = new(2026, 8, 25);
    private static readonly TimeOnly VisitTime = new(14, 0);

    [Fact]
    public void QRの有効期限は予約時刻の2時間後になる()
    {
        var expected = VisitDate.ToDateTime(VisitTime, DateTimeKind.Local).AddHours(2).ToUniversalTime();

        var actual = VisitRules.CalculateExpiresAtUtc(VisitDate, VisitTime);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void QRの有効開始時刻は予約時刻の30分前になる()
    {
        var expected = VisitDate.ToDateTime(VisitTime, DateTimeKind.Local).AddMinutes(-30).ToUniversalTime();

        var actual = VisitRules.CalculateValidFromUtc(VisitDate, VisitTime);

        Assert.Equal(expected, actual);
    }

    [Theory]
    // 開始境界（30分前ちょうど）は受付可能
    [InlineData(-30, true)]
    // 開始境界の1分前は受付不可
    [InlineData(-31, false)]
    [InlineData(0, true)]
    [InlineData(60, true)]
    // 終了境界（2時間後ちょうど）は受付可能
    [InlineData(120, true)]
    // 終了境界の1分後は受付不可
    [InlineData(121, false)]
    public void 受付可能時間帯は予約時刻の30分前から2時間後までである(int offsetMinutes, bool expected)
    {
        var nowUtc = VisitDate.ToDateTime(VisitTime, DateTimeKind.Local).AddMinutes(offsetMinutes).ToUniversalTime();

        var actual = VisitRules.IsWithinReceptionWindow(VisitDate, VisitTime, nowUtc);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(VisitorStatus.NotArrived, true)]
    [InlineData(VisitorStatus.CheckedIn, true)]
    // 退館済からのキャンセルは不可（設計書 7.1節）
    [InlineData(VisitorStatus.CheckedOut, false)]
    [InlineData(VisitorStatus.Cancelled, false)]
    public void キャンセルは未入館と入館中からのみ可能である(VisitorStatus status, bool expected)
        => Assert.Equal(expected, VisitRules.CanCancel(status));

    [Theory]
    [InlineData(VisitorStatus.NotArrived, ReservationType.Advance, true)]
    [InlineData(VisitorStatus.CheckedIn, ReservationType.Advance, true)]
    [InlineData(VisitorStatus.CheckedOut, ReservationType.Advance, false)]
    [InlineData(VisitorStatus.Cancelled, ReservationType.Advance, false)]
    // 当日受付分は性質上、再送信の対象外（設計書 6.7節）
    [InlineData(VisitorStatus.NotArrived, ReservationType.WalkIn, false)]
    [InlineData(VisitorStatus.CheckedIn, ReservationType.WalkIn, false)]
    public void メール再送信は事前予約かつ未入館入館中のみ可能である(
        VisitorStatus status, ReservationType reservationType, bool expected)
        => Assert.Equal(expected, VisitRules.CanResendMail(status, reservationType));
}
