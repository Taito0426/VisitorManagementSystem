using VisitorManagement.Application.Services;

namespace VisitorManagement.Tests;

/// <summary>
/// ログ保持・アーカイブ設計のテスト（設計書 10.4節）。
/// </summary>
public sealed class LogArchiveServiceTests
{
    [Fact]
    public void 削除しきい値は当月を含む保持月数分を残す()
    {
        // 保持3か月の場合、当月と直前2か月を残し、それ以前を削除する
        // 例）4月実行 → しきい値は2月1日となり、1月以前が削除対象
        var currentMonthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1, 0, 0, 0, DateTimeKind.Local);
        var expected = currentMonthStart.AddMonths(-2).ToUniversalTime();

        var actual = LogArchiveService.CalculateDeleteThresholdUtc(3);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 保持1か月なら当月分のみ残す()
    {
        var currentMonthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1, 0, 0, 0, DateTimeKind.Local);

        var actual = LogArchiveService.CalculateDeleteThresholdUtc(1);

        Assert.Equal(currentMonthStart.ToUniversalTime(), actual);
    }

    [Theory]
    // 2026年6月：1日(月)が第1営業日
    [InlineData(2026, 6, 1, 1, true)]
    [InlineData(2026, 6, 2, 1, false)]
    [InlineData(2026, 6, 2, 2, true)]
    // 2026年8月：1日(土)・2日(日)のため 3日(月)が第1営業日
    [InlineData(2026, 8, 1, 1, false)]
    [InlineData(2026, 8, 2, 1, false)]
    [InlineData(2026, 8, 3, 1, true)]
    [InlineData(2026, 8, 4, 2, true)]
    public void 第N営業日の判定は土日を除いて行われる(int year, int month, int day, int nth, bool expected)
    {
        var date = new DateOnly(year, month, day);

        Assert.Equal(expected, LogArchiveService.IsNthBusinessDayOfMonth(date, nth));
    }

    [Fact]
    public void 第0営業日以下は常に偽となる()
    {
        var date = new DateOnly(2026, 6, 1);

        Assert.False(LogArchiveService.IsNthBusinessDayOfMonth(date, 0));
        Assert.False(LogArchiveService.IsNthBusinessDayOfMonth(date, -1));
    }
}
