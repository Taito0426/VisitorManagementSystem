using Microsoft.Extensions.Logging.Abstractions;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;
using VisitorManagement.Tests.Fakes;

namespace VisitorManagement.Tests;

/// <summary>
/// 来館予約の登録テスト（設計書 6.1節・6.2節・6.6節）。
/// 特に当日受付の「登録＝入館」仕様を検証する。
/// </summary>
public sealed class ReservationServiceTests
{
    private readonly FakeDbSessionFactory _sessionFactory = new();
    private readonly FakeVisitorRepository _visitorRepository = new();
    private readonly FakeQrTokenRepository _qrTokenRepository = new();
    private readonly FakeAccessLogRepository _accessLogRepository = new();
    private readonly FakeSiteRepository _siteRepository = new();
    private readonly FakeOperatorRepository _operatorRepository = new();
    private readonly FakeMailSender _mailSender = new();
    private readonly FakeMailHistoryRepository _mailHistoryRepository = new();

    private readonly ReservationService _service;
    private readonly AccessService _accessService;
    private readonly OperatorContext _ctx;

    private const int SiteId = 1;

    public ReservationServiceTests()
    {
        _siteRepository.Sites.Add(new Site { SiteId = SiteId, SiteName = "本社", IsActive = true });
        _operatorRepository.Operators.Add(new Operator
        {
            OperatorId = 1,
            LoginId = "reception",
            DisplayName = "受付 花子",
            Email = "reception@example.com",
            RoleType = RoleType.Reception,
            SiteId = SiteId,
        });

        var mailDeliveryService = new MailDeliveryService(
            _mailSender, _mailHistoryRepository, NullLogger<MailDeliveryService>.Instance);

        _service = new ReservationService(
            _sessionFactory,
            _visitorRepository,
            _qrTokenRepository,
            _accessLogRepository,
            _siteRepository,
            _operatorRepository,
            new FakeQrCodeImageGenerator(),
            mailDeliveryService,
            NullLogger<ReservationService>.Instance);

        _accessService = new AccessService(
            _sessionFactory,
            _visitorRepository,
            _qrTokenRepository,
            _accessLogRepository,
            NullLogger<AccessService>.Instance);

        _ctx = new OperatorContext(_operatorRepository.Operators[0]);
    }

    /// <summary>予約入力を組み立てる（代表者1名＋同行者2名）</summary>
    private static ReservationInput BuildInput(ReservationType reservationType)
    {
        var now = DateTime.Now;

        return new ReservationInput
        {
            SiteId = SiteId,
            VisitDate = DateOnly.FromDateTime(now),
            VisitTimeFrom = TimeOnly.FromDateTime(now),
            HostEmployeeName = "営業部 田中",
            PurposeOfVisit = "打合せ",
            ReservationType = reservationType,
            Representative = new ReservationMemberInput
            {
                VisitorName = "山田 太郎",
                CompanyName = "株式会社サンプル商事",
                VisitorEmail = "yamada@example.com",
            },
            Companions =
            [
                new ReservationMemberInput { VisitorName = "佐藤 次郎", CompanyName = "株式会社サンプル商事" },
                new ReservationMemberInput { VisitorName = "鈴木 三郎", CompanyName = "株式会社サンプル商事" },
            ],
        };
    }

    // ===== 事前予約 =====

    [Fact]
    public async Task 事前予約は未入館で登録される()
    {
        await _service.CreateAsync(BuildInput(ReservationType.Advance), _ctx);

        Assert.All(_visitorRepository.Visitors, v => Assert.Equal(VisitorStatus.NotArrived, v.Status));
    }

    [Fact]
    public async Task 事前予約のQRトークンは未読取で発行される()
    {
        var result = await _service.CreateAsync(BuildInput(ReservationType.Advance), _ctx);

        Assert.Equal(0, result.Group.Token!.UsedCount);
    }

    [Fact]
    public async Task 事前予約では入館履歴が作成されない()
    {
        await _service.CreateAsync(BuildInput(ReservationType.Advance), _ctx);

        Assert.Empty(_accessLogRepository.Logs);
    }

    [Fact]
    public async Task 事前予約では予約登録者宛の控えメールも送信される()
    {
        await _service.CreateAsync(BuildInput(ReservationType.Advance), _ctx);

        Assert.Contains(MailRecipientType.Invitee, _mailSender.SentRecipients);
        Assert.Contains(MailRecipientType.Registrant, _mailSender.SentRecipients);
    }

    // ===== 当日受付（登録＝入館） =====

    [Fact]
    public async Task 当日受付は登録と同時にグループ全員が入館中になる()
    {
        await _service.CreateAsync(BuildInput(ReservationType.WalkIn), _ctx);

        Assert.Equal(3, _visitorRepository.Visitors.Count);
        Assert.All(_visitorRepository.Visitors, v => Assert.Equal(VisitorStatus.CheckedIn, v.Status));
    }

    [Fact]
    public async Task 当日受付は発行時刻で入館履歴が作成される()
    {
        var before = DateTime.UtcNow;

        var result = await _service.CreateAsync(BuildInput(ReservationType.WalkIn), _ctx);

        var after = DateTime.UtcNow;

        // グループ内の来館者ごとに1件ずつ記録される
        Assert.Equal(3, _accessLogRepository.Logs.Count);
        Assert.All(_accessLogRepository.Logs, log =>
        {
            Assert.Equal(AccessType.CheckIn, log.AccessType);
            // 入館時刻はQRトークンの発行時刻と一致する
            Assert.Equal(result.Group.Token!.IssuedAt, log.AccessDateTime);
            Assert.InRange(log.AccessDateTime, before, after);
            // TokenId は代表者の1トークンを共通参照する
            Assert.Equal(result.Group.Token.TokenId, log.TokenId);
        });
    }

    [Fact]
    public async Task 当日受付のQRトークンは読取済みとして発行される()
    {
        var result = await _service.CreateAsync(BuildInput(ReservationType.WalkIn), _ctx);

        // 1回読取済みの状態で発行するため、次の読取が2回目＝退館になる
        Assert.Equal(1, result.Group.Token!.UsedCount);
    }

    [Fact]
    public async Task 当日受付で発行したQRの最初の読取は退館になる()
    {
        var result = await _service.CreateAsync(BuildInput(ReservationType.WalkIn), _ctx);
        var logsAfterRegistration = _accessLogRepository.Logs.Count;

        var scan = await _accessService.ScanAsync(result.Group.Token!.TokenValue, "テスト端末");

        Assert.True(scan.IsSuccess);
        Assert.Equal(AccessType.CheckOut, scan.ResultType);
        Assert.All(_visitorRepository.Visitors, v => Assert.Equal(VisitorStatus.CheckedOut, v.Status));

        // 退館履歴がグループ全員分追加される
        Assert.Equal(logsAfterRegistration + 3, _accessLogRepository.Logs.Count);
        Assert.Equal(2, _qrTokenRepository.Tokens[0].UsedCount);
    }

    [Fact]
    public async Task 当日受付で発行したQRを二度読み取るとエラーになる()
    {
        var result = await _service.CreateAsync(BuildInput(ReservationType.WalkIn), _ctx);
        var tokenValue = result.Group.Token!.TokenValue;

        await _accessService.ScanAsync(tokenValue, "テスト端末");
        var second = await _accessService.ScanAsync(tokenValue, "テスト端末");

        Assert.False(second.IsSuccess);
        Assert.Equal(AccessType.Error, second.ResultType);
        Assert.Contains("既に退館処理済み", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 当日受付では予約登録者宛の控えメールを送信しない()
    {
        await _service.CreateAsync(BuildInput(ReservationType.WalkIn), _ctx);

        Assert.Contains(MailRecipientType.Invitee, _mailSender.SentRecipients);
        Assert.DoesNotContain(MailRecipientType.Registrant, _mailSender.SentRecipients);
    }

    [Fact]
    public async Task 当日受付でメールアドレス未入力なら送信しない()
    {
        var input = BuildInput(ReservationType.WalkIn);
        input.Representative.VisitorEmail = null;

        await _service.CreateAsync(input, _ctx);

        Assert.Empty(_mailSender.SentRecipients);
        // 入館登録自体は行われる
        Assert.All(_visitorRepository.Visitors, v => Assert.Equal(VisitorStatus.CheckedIn, v.Status));
    }

    [Fact]
    public async Task 当日受付の来館者は手動退館処理の対象になる()
    {
        await _service.CreateAsync(BuildInput(ReservationType.WalkIn), _ctx);

        var targets = await _accessService.SearchManualCheckOutTargetsAsync(null, null, null, _ctx);

        // 登録直後から入館中のため、はぐれ対応の対象として検索できる
        Assert.Equal(3, targets.Count);
    }

    // ===== 共通 =====

    [Fact]
    public async Task QRトークンは代表者にのみ1件発行される()
    {
        var result = await _service.CreateAsync(BuildInput(ReservationType.WalkIn), _ctx);

        Assert.Single(_qrTokenRepository.Tokens);
        Assert.Equal(result.Group.Representative.VisitorId, _qrTokenRepository.Tokens[0].VisitorId);

        foreach (var companion in result.Group.Companions)
        {
            Assert.Null(await _qrTokenRepository.GetByVisitorIdAsync(companion.VisitorId));
        }
    }

    [Fact]
    public async Task 同行者のメールアドレスは保持されない()
    {
        var input = BuildInput(ReservationType.WalkIn);
        input.Companions[0].VisitorEmail = "companion@example.com";

        var result = await _service.CreateAsync(input, _ctx);

        Assert.All(result.Group.Companions, c => Assert.Null(c.VisitorEmail));
    }

    [Fact]
    public async Task 本番構成では送信結果が送信成功と表示される()
    {
        _mailSender.IsTestMode = false;

        var result = await _service.CreateAsync(BuildInput(ReservationType.Advance), _ctx);

        Assert.All(result.MailResults, r => Assert.False(r.IsTest));
        Assert.All(result.MailResults, r => Assert.Equal("送信成功", r.ToDisplayText()));
    }

    [Fact]
    public async Task テストモードでは送信結果がテストと表示される()
    {
        // デモ版・Debugビルドでは外部送信を行わないモックが使われる
        _mailSender.IsTestMode = true;

        var result = await _service.CreateAsync(BuildInput(ReservationType.Advance), _ctx);

        Assert.NotEmpty(result.MailResults);
        Assert.All(result.MailResults, r => Assert.True(r.IsTest));
        Assert.All(result.MailResults, r => Assert.Contains("テスト", r.ToDisplayText(), StringComparison.Ordinal));

        // 送信履歴は本番と同じく記録する
        Assert.Equal(result.MailResults.Count, _mailHistoryRepository.Histories.Count);
    }
}
