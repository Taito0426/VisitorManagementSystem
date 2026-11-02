using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Infrastructure.Mail;

namespace VisitorManagement.Tests;

/// <summary>
/// 来館案内メールの文面テスト（設計書 5.2節・6.3節・6.12節）。
/// </summary>
public sealed class MailTemplatesTests
{
    private static Visitor Representative() => new()
    {
        VisitorId = Guid.NewGuid(),
        IsRepresentative = true,
        VisitorName = "山田 太郎",
        CompanyName = "株式会社サンプル商事",
        VisitorEmail = "yamada@example.com",
        VisitDate = new DateOnly(2026, 8, 25),
        VisitTimeFrom = new TimeOnly(14, 0),
        VisitTimeTo = new TimeOnly(15, 0),
        HostEmployeeName = "営業部 田中",
        PurposeOfVisit = "打合せ",
        SiteName = "本社",
    };

    private static Operator Registrant() => new()
    {
        OperatorId = 1,
        LoginId = "reception",
        DisplayName = "受付 花子",
        Email = "reception@example.com",
        RoleType = RoleType.Reception,
        SiteId = 1,
    };

    private static List<Visitor> Companions() =>
    [
        new() { VisitorName = "佐藤 次郎", CompanyName = "株式会社サンプル商事" },
        new() { VisitorName = "鈴木 三郎", CompanyName = "株式会社サンプル商事" },
    ];

    [Fact]
    public void 招待者宛の本文に同行者の氏名と会社名が一覧表示される()
    {
        var body = MailTemplates.BuildInvitationBody(
            Representative(), Registrant(), MailRecipientType.Invitee, Companions(), "本社");

        Assert.Contains("佐藤 次郎", body, StringComparison.Ordinal);
        Assert.Contains("鈴木 三郎", body, StringComparison.Ordinal);
        Assert.Contains("株式会社サンプル商事", body, StringComparison.Ordinal);
        // QRは代表者のみに発行される旨の案内が含まれる
        Assert.Contains("代表者様お一人にのみ発行", body, StringComparison.Ordinal);
    }

    [Fact]
    public void 同行者がいない場合は同行者欄が出力されない()
    {
        var body = MailTemplates.BuildInvitationBody(
            Representative(), Registrant(), MailRecipientType.Invitee, [], "本社");

        Assert.DoesNotContain("ご同行者", body, StringComparison.Ordinal);
    }

    [Fact]
    public void 予約登録者宛は控えであることと転送案内が含まれる()
    {
        var body = MailTemplates.BuildInvitationBody(
            Representative(), Registrant(), MailRecipientType.Registrant, Companions(), "本社");

        Assert.Contains("本メールは控えです", body, StringComparison.Ordinal);
        Assert.Contains("転送", body, StringComparison.Ordinal);
        Assert.Contains("受付 花子", body, StringComparison.Ordinal);
    }

    [Fact]
    public void 本文は日本語と英語を併記する()
    {
        var body = MailTemplates.BuildInvitationBody(
            Representative(), Registrant(), MailRecipientType.Invitee, Companions(), "本社");

        Assert.Contains("ご予約内容", body, StringComparison.Ordinal);
        Assert.Contains("Reservation Details", body, StringComparison.Ordinal);
        Assert.Contains("来館日", body, StringComparison.Ordinal);
        Assert.Contains("Date", body, StringComparison.Ordinal);
    }

    [Fact]
    public void 本文にQRコードの有効時間帯と読取ルールが記載される()
    {
        var body = MailTemplates.BuildInvitationBody(
            Representative(), Registrant(), MailRecipientType.Invitee, [], "本社");

        Assert.Contains("30分前から2時間後", body, StringComparison.Ordinal);
        Assert.Contains("1回目の読み取りで入館、2回目の読み取りで退館", body, StringComparison.Ordinal);
    }

    [Fact]
    public void 予約登録者宛の件名には控えの接頭辞が付く()
    {
        var inviteeSubject = MailTemplates.BuildSubject(MailType.VisitInvitation, MailRecipientType.Invitee);
        var registrantSubject = MailTemplates.BuildSubject(MailType.VisitInvitation, MailRecipientType.Registrant);

        Assert.DoesNotContain("【控え】", inviteeSubject, StringComparison.Ordinal);
        Assert.StartsWith("【控え】", registrantSubject, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MailType.VisitInvitation, "ご来館のご案内")]
    [InlineData(MailType.Resend, "【再送】")]
    [InlineData(MailType.Reminder, "リマインド")]
    [InlineData(MailType.CancellationNotice, "キャンセル")]
    public void メール種別ごとに件名が切り替わる(MailType mailType, string expectedFragment)
    {
        var subject = MailTemplates.BuildSubject(mailType, MailRecipientType.Invitee);

        Assert.Contains(expectedFragment, subject, StringComparison.Ordinal);
    }

    [Fact]
    public void HTML本文では特殊文字がエスケープされる()
    {
        var html = MailTemplates.ToHtmlBody("<script>alert('x')</script>");

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void キャンセル通知にQRコードが無効になる旨が記載される()
    {
        var body = MailTemplates.BuildCancellationBody(Representative());

        Assert.Contains("キャンセル", body, StringComparison.Ordinal);
        Assert.Contains("無効", body, StringComparison.Ordinal);
        Assert.Contains("no longer valid", body, StringComparison.Ordinal);
    }
}
