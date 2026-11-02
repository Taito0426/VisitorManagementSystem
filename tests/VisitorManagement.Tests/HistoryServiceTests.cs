using VisitorManagement.Application.Services;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Tests;

/// <summary>
/// 履歴管理機能（CSV出力・表示変換）のテスト（設計書 6.8節）。
/// </summary>
public sealed class HistoryServiceTests
{
    [Theory]
    [InlineData("普通の値", "普通の値")]
    [InlineData("カンマ,入り", "\"カンマ,入り\"")]
    [InlineData("引用符\"入り", "\"引用符\"\"入り\"")]
    [InlineData("改行\n入り", "\"改行\n入り\"")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void CSVフィールドは必要な場合のみ引用符で囲まれる(string? value, string expected)
        => Assert.Equal(expected, HistoryService.Escape(value));

    [Fact]
    public void 入退館履歴CSVにヘッダーと明細が出力される()
    {
        var logs = new List<AccessLog>
        {
            new()
            {
                AccessDateTime = new DateTime(2026, 8, 25, 5, 0, 0, DateTimeKind.Utc),
                SiteName = "本社",
                VisitorName = "山田 太郎",
                CompanyName = "株式会社サンプル,商事",
                AccessType = AccessType.CheckIn,
                DeviceInfo = "受付端末01",
            },
        };

        var csv = HistoryService.ToCsv(logs);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("受付日時,拠点,来館者氏名,会社名,区分,端末情報,備考", lines[0]);
        Assert.Contains("入館", lines[1], StringComparison.Ordinal);
        // カンマを含む会社名は引用符で囲まれる
        Assert.Contains("\"株式会社サンプル,商事\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void メール送信履歴CSVに宛先区分と結果が出力される()
    {
        var histories = new List<MailHistory>
        {
            new()
            {
                SentAt = new DateTime(2026, 8, 25, 5, 0, 0, DateTimeKind.Utc),
                VisitorName = "山田 太郎",
                MailType = MailType.VisitInvitation,
                RecipientType = MailRecipientType.Registrant,
                Provider = MailProviderType.SendGrid,
                Status = MailSendStatus.Failed,
                ErrorMessage = "APIキー未設定",
            },
        };

        var csv = HistoryService.ToCsv(histories);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("送信日時,来館者氏名,会社名,メール種別,宛先区分,送信方式,結果,エラー内容", lines[0]);
        Assert.Contains("来館案内", lines[1], StringComparison.Ordinal);
        Assert.Contains("予約登録者", lines[1], StringComparison.Ordinal);
        Assert.Contains("SendGrid", lines[1], StringComparison.Ordinal);
        Assert.Contains("失敗", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void UTCで保持した日時はローカル時刻で表示される()
    {
        var utc = new DateTime(2026, 8, 25, 5, 0, 0, DateTimeKind.Utc);

        var text = HistoryService.ToLocalText(utc);

        Assert.Equal(utc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss"), text);
    }

    [Theory]
    [InlineData(VisitorStatus.NotArrived, "未入館")]
    [InlineData(VisitorStatus.CheckedIn, "入館中")]
    [InlineData(VisitorStatus.CheckedOut, "退館済")]
    [InlineData(VisitorStatus.Cancelled, "キャンセル")]
    public void 予約ステータスの表示名が設計書どおりである(VisitorStatus status, string expected)
        => Assert.Equal(expected, HistoryService.ToText(status));

    [Theory]
    [InlineData(RoleType.Administrator, "管理者")]
    [InlineData(RoleType.Reception, "受付")]
    [InlineData(RoleType.General, "一般")]
    public void 権限ロールの表示名が設計書どおりである(RoleType role, string expected)
        => Assert.Equal(expected, HistoryService.ToText(role));
}
