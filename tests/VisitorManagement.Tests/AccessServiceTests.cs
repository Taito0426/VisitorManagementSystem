using Microsoft.Extensions.Logging.Abstractions;
using VisitorManagement.Application.Exceptions;
using VisitorManagement.Application.Services;
using VisitorManagement.Core;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;
using VisitorManagement.Tests.Fakes;

namespace VisitorManagement.Tests;

/// <summary>
/// QR受付シナリオテスト（設計書 12.3節）。
/// 1回目／2回目／3回目以降の読取、期限切れ・無効化トークン等の境界値を網羅する。
/// </summary>
public sealed class AccessServiceTests
{
    private readonly FakeDbSessionFactory _sessionFactory = new();
    private readonly FakeVisitorRepository _visitorRepository = new();
    private readonly FakeQrTokenRepository _qrTokenRepository = new();
    private readonly FakeAccessLogRepository _accessLogRepository = new();
    private readonly AccessService _service;

    private const int SiteId = 1;

    public AccessServiceTests()
    {
        _service = new AccessService(
            _sessionFactory,
            _visitorRepository,
            _qrTokenRepository,
            _accessLogRepository,
            NullLogger<AccessService>.Instance);
    }

    /// <summary>代表者1名＋同行者を持つ予約グループとQRトークンを用意する</summary>
    private QrToken ArrangeGroup(
        int companionCount = 2,
        VisitorStatus status = VisitorStatus.NotArrived,
        byte usedCount = 0,
        bool isValid = true,
        TimeOnly? visitTime = null)
    {
        var groupId = Guid.NewGuid();
        var visitDate = DateOnly.FromDateTime(DateTime.Today);
        var time = visitTime ?? TimeOnly.FromDateTime(DateTime.Now);

        var representative = new Visitor
        {
            VisitorId = Guid.NewGuid(),
            ReservationGroupId = groupId,
            IsRepresentative = true,
            SiteId = SiteId,
            VisitorName = "山田 太郎",
            CompanyName = "株式会社サンプル商事",
            VisitDate = visitDate,
            VisitTimeFrom = time,
            Status = status,
            SiteName = "本社",
        };
        _visitorRepository.Visitors.Add(representative);

        for (var i = 0; i < companionCount; i++)
        {
            _visitorRepository.Visitors.Add(new Visitor
            {
                VisitorId = Guid.NewGuid(),
                ReservationGroupId = groupId,
                IsRepresentative = false,
                SiteId = SiteId,
                VisitorName = $"同行者{i + 1}",
                CompanyName = "株式会社サンプル商事",
                VisitDate = visitDate,
                VisitTimeFrom = time,
                Status = status,
            });
        }

        var token = new QrToken
        {
            TokenId = Guid.NewGuid(),
            VisitorId = representative.VisitorId,
            TokenValue = Guid.NewGuid(),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = VisitRules.CalculateExpiresAtUtc(visitDate, time),
            UsedCount = usedCount,
            IsValid = isValid,
        };
        _qrTokenRepository.Tokens.Add(token);

        return token;
    }

    [Fact]
    public async Task 端末情報が長い場合はDBの桁数に丸めて記録される()
    {
        var token = ArrangeGroup();

        // 拠点名が長い、IPv6、読取経路の付記などで100文字を超えうる
        var deviceInfo = new string('端', AccessService.DeviceInfoMaxLength + 30) + "／カメラ";

        await _service.ScanAsync(token.TokenValue, deviceInfo);

        Assert.NotEmpty(_accessLogRepository.Logs);
        Assert.All(_accessLogRepository.Logs,
            log => Assert.Equal(AccessService.DeviceInfoMaxLength, log.DeviceInfo!.Length));
    }

    [Fact]
    public async Task 端末情報が桁数以内ならそのまま記録される()
    {
        var token = ArrangeGroup();

        await _service.ScanAsync(token.TokenValue, "受付Web 本社／reception／192.168.0.10／カメラ");

        Assert.All(_accessLogRepository.Logs,
            log => Assert.Equal("受付Web 本社／reception／192.168.0.10／カメラ", log.DeviceInfo));
    }

    [Fact]
    public async Task 一回目の読取でグループ全員が入館中になる()
    {
        var token = ArrangeGroup();

        var result = await _service.ScanAsync(token.TokenValue, "テスト端末");

        Assert.True(result.IsSuccess);
        Assert.Equal(AccessType.CheckIn, result.ResultType);
        Assert.Equal(3, result.Members.Count);
        Assert.All(_visitorRepository.Visitors, v => Assert.Equal(VisitorStatus.CheckedIn, v.Status));

        // グループ内の来館者ごとに1件ずつ記録される（設計書 9.2.5節）
        Assert.Equal(3, _accessLogRepository.Logs.Count);
        Assert.All(_accessLogRepository.Logs, l => Assert.Equal(AccessType.CheckIn, l.AccessType));

        // TokenId は代表者の1トークンを共通参照する
        Assert.All(_accessLogRepository.Logs, l => Assert.Equal(token.TokenId, l.TokenId));
        Assert.Equal(1, _qrTokenRepository.Tokens[0].UsedCount);
    }

    [Fact]
    public async Task 二回目の読取でグループ全員が退館済になる()
    {
        var token = ArrangeGroup(status: VisitorStatus.CheckedIn, usedCount: 1);

        var result = await _service.ScanAsync(token.TokenValue, "テスト端末");

        Assert.True(result.IsSuccess);
        Assert.Equal(AccessType.CheckOut, result.ResultType);
        Assert.All(_visitorRepository.Visitors, v => Assert.Equal(VisitorStatus.CheckedOut, v.Status));
        Assert.Equal(2, _qrTokenRepository.Tokens[0].UsedCount);
    }

    [Fact]
    public async Task 三回目以降の読取はエラーになりステータスは変化しない()
    {
        var token = ArrangeGroup(status: VisitorStatus.CheckedOut, usedCount: 2);

        var result = await _service.ScanAsync(token.TokenValue, "テスト端末");

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessType.Error, result.ResultType);
        Assert.Contains("既に退館処理済み", result.Message, StringComparison.Ordinal);

        // 退館済のまま変化しない
        Assert.All(_visitorRepository.Visitors, v => Assert.Equal(VisitorStatus.CheckedOut, v.Status));
        Assert.Equal(2, _qrTokenRepository.Tokens[0].UsedCount);

        // DB更新は AccessLogs へのエラー記録に限る（設計書 7.2節）
        Assert.Single(_accessLogRepository.Logs);
        Assert.Equal(AccessType.Error, _accessLogRepository.Logs[0].AccessType);
    }

    [Fact]
    public async Task 期限切れトークンはエラーとして記録される()
    {
        // 3時間前の予約 → 有効期限（+2時間）を過ぎている
        var token = ArrangeGroup(visitTime: TimeOnly.FromDateTime(DateTime.Now.AddHours(-3)));

        var result = await _service.ScanAsync(token.TokenValue, "テスト端末");

        Assert.False(result.IsSuccess);
        Assert.Contains("有効期限", result.Message, StringComparison.Ordinal);
        Assert.Single(_accessLogRepository.Logs);
        Assert.Equal(AccessType.Error, _accessLogRepository.Logs[0].AccessType);
        Assert.All(_visitorRepository.Visitors, v => Assert.Equal(VisitorStatus.NotArrived, v.Status));
    }

    [Fact]
    public async Task 受付可能時間の前はエラーになる()
    {
        // 2時間後の予約 → 有効開始（30分前）にまだ達していない
        var token = ArrangeGroup(visitTime: TimeOnly.FromDateTime(DateTime.Now.AddHours(2)));

        var result = await _service.ScanAsync(token.TokenValue, "テスト端末");

        Assert.False(result.IsSuccess);
        Assert.Contains("受付可能時間の前", result.Message, StringComparison.Ordinal);
        Assert.Single(_accessLogRepository.Logs);
    }

    [Fact]
    public async Task 無効化されたトークンはエラーになる()
    {
        var token = ArrangeGroup(isValid: false);

        var result = await _service.ScanAsync(token.TokenValue, "テスト端末");

        Assert.False(result.IsSuccess);
        Assert.Contains("無効", result.Message, StringComparison.Ordinal);
        Assert.Single(_accessLogRepository.Logs);
    }

    [Fact]
    public async Task キャンセル済み予約はエラーになる()
    {
        var token = ArrangeGroup(status: VisitorStatus.Cancelled);

        var result = await _service.ScanAsync(token.TokenValue, "テスト端末");

        Assert.False(result.IsSuccess);
        Assert.Contains("キャンセル", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 未登録トークンは履歴を残さずエラーになる()
    {
        ArrangeGroup();

        var result = await _service.ScanAsync(Guid.NewGuid(), "テスト端末");

        Assert.False(result.IsSuccess);
        Assert.Contains("読み取れませんでした", result.Message, StringComparison.Ordinal);

        // 対象の VisitorId を特定できないため AccessLogs には記録できない
        Assert.Empty(_accessLogRepository.Logs);
    }

    [Fact]
    public async Task 同時読取では後発の処理が拒否される()
    {
        var token = ArrangeGroup();

        // 別端末が先に読み取った状態を再現する
        await _qrTokenRepository.TryConsumeAsync(token.TokenId, 0, new FakeDbSession());
        // 読取直前に取得した古い値（UsedCount=0）で処理が進むことを模擬する
        var stale = _qrTokenRepository.Tokens[0];
        var currentUsedCount = stale.UsedCount;

        var result = await _service.ScanAsync(token.TokenValue, "テスト端末");

        // 現在値（1）を前提とした2回目の読取として成立する
        Assert.True(result.IsSuccess);
        Assert.Equal(AccessType.CheckOut, result.ResultType);
        Assert.Equal(currentUsedCount + 1, _qrTokenRepository.Tokens[0].UsedCount);
    }

    [Fact]
    public async Task キャンセル済みの同行者は一括更新の対象外となる()
    {
        var token = ArrangeGroup();
        _visitorRepository.Visitors[2].Status = VisitorStatus.Cancelled;

        await _service.ScanAsync(token.TokenValue, "テスト端末");

        Assert.Equal(VisitorStatus.CheckedIn, _visitorRepository.Visitors[0].Status);
        Assert.Equal(VisitorStatus.CheckedIn, _visitorRepository.Visitors[1].Status);
        Assert.Equal(VisitorStatus.Cancelled, _visitorRepository.Visitors[2].Status);

        // 履歴もキャンセル済みを除いた2名分のみ
        Assert.Equal(2, _accessLogRepository.Logs.Count);
    }

    [Fact]
    public async Task 手動退館処理は選択した個人のみを退館済にする()
    {
        var token = ArrangeGroup(status: VisitorStatus.CheckedIn, usedCount: 1);
        var companion = _visitorRepository.Visitors[1];

        await _service.ManualCheckOutAsync(companion.VisitorId, null, ReceptionContext());

        Assert.Equal(VisitorStatus.CheckedOut, companion.Status);
        Assert.Equal(VisitorStatus.CheckedIn, _visitorRepository.Visitors[0].Status);
        Assert.Equal(VisitorStatus.CheckedIn, _visitorRepository.Visitors[2].Status);

        var log = Assert.Single(_accessLogRepository.Logs);
        Assert.Equal(AccessType.CheckOut, log.AccessType);
        Assert.Contains("グループはぐれ", log.OperatorNote!, StringComparison.Ordinal);

        // TokenId は代表者の共通QRトークンを参照する（設計書 6.4.1節）
        Assert.Equal(token.TokenId, log.TokenId);
    }

    [Fact]
    public async Task 入館中でない来館者は手動退館できない()
    {
        ArrangeGroup();
        var companion = _visitorRepository.Visitors[1];

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _service.ManualCheckOutAsync(companion.VisitorId, null, ReceptionContext()));

        Assert.Contains("入館中の来館者のみ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 一般ロールは手動退館処理を実行できない()
    {
        ArrangeGroup(status: VisitorStatus.CheckedIn, usedCount: 1);
        var companion = _visitorRepository.Visitors[1];

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => _service.ManualCheckOutAsync(companion.VisitorId, null, GeneralContext()));
    }

    /// <summary>受付ロールの操作ユーザー</summary>
    private static OperatorContext ReceptionContext() => new(new Operator
    {
        OperatorId = 2,
        LoginId = "reception",
        DisplayName = "受付 花子",
        Email = "reception@example.com",
        RoleType = RoleType.Reception,
        SiteId = SiteId,
    });

    /// <summary>一般ロールの操作ユーザー</summary>
    private static OperatorContext GeneralContext() => new(new Operator
    {
        OperatorId = 3,
        LoginId = "general",
        DisplayName = "一般 次郎",
        Email = "general@example.com",
        RoleType = RoleType.General,
        SiteId = SiteId,
    });
}
