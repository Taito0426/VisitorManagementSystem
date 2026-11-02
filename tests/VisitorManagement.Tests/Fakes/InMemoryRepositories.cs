using System.Data;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Tests.Fakes;

/// <summary>
/// テスト用のインメモリDBセッション。実際の接続・トランザクションは伴わない。
/// </summary>
internal sealed class FakeDbSession : IDbSession
{
    public IDbConnection Connection => throw new NotSupportedException("インメモリのテストでは接続を使用しません。");

    public IDbTransaction? Transaction => null;

    /// <summary>Commit が呼ばれた回数</summary>
    public int CommitCount { get; private set; }

    public void Commit() => CommitCount++;

    public void Rollback()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// テスト用の DBセッションファクトリ。生成したセッションを検証用に保持する。
/// </summary>
internal sealed class FakeDbSessionFactory : IDbSessionFactory
{
    /// <summary>生成されたセッション</summary>
    public List<FakeDbSession> Sessions { get; } = [];

    public IDbSession CreateSession(bool beginTransaction = false)
    {
        var session = new FakeDbSession();
        Sessions.Add(session);
        return session;
    }
}

/// <summary>
/// テスト用のリトライ方針（リトライせずそのまま実行する）。
/// </summary>
internal sealed class FakeRetryPolicy : IDbRetryPolicy
{
    public Task<T> ExecuteAsync<T>(Func<Task<T>> operation) => operation();
}

/// <summary>
/// テスト用の来館者リポジトリ。
/// </summary>
internal sealed class FakeVisitorRepository : IVisitorRepository
{
    /// <summary>登録済みの来館者</summary>
    public List<Visitor> Visitors { get; } = [];

    public Task<Visitor?> GetByIdAsync(Guid visitorId) => GetByIdAsync(visitorId, null);

    public Task<Visitor?> GetByIdAsync(Guid visitorId, IDbSession? session)
        => Task.FromResult(Visitors.FirstOrDefault(v => v.VisitorId == visitorId));

    public Task<Guid> CreateAsync(Visitor visitor) => CreateAsync(visitor, null);

    public Task<Guid> CreateAsync(Visitor visitor, IDbSession? session)
    {
        if (visitor.VisitorId == Guid.Empty)
        {
            visitor.VisitorId = Guid.NewGuid();
        }

        Visitors.Add(visitor);
        return Task.FromResult(visitor.VisitorId);
    }

    public Task UpdateStatusAsync(Guid visitorId, VisitorStatus status) => UpdateStatusAsync(visitorId, status, null);

    public Task UpdateStatusAsync(Guid visitorId, VisitorStatus status, IDbSession? session)
    {
        var visitor = Visitors.FirstOrDefault(v => v.VisitorId == visitorId);
        if (visitor is not null)
        {
            visitor.Status = status;
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Visitor visitor, IDbSession? session = null) => Task.CompletedTask;

    public Task<IReadOnlyList<Visitor>> SearchAsync(VisitorSearchCondition condition)
        => Task.FromResult<IReadOnlyList<Visitor>>([.. Visitors]);

    public Task<IReadOnlyList<Visitor>> GetByGroupIdAsync(Guid reservationGroupId, IDbSession? session = null)
        => Task.FromResult<IReadOnlyList<Visitor>>(
            [.. Visitors.Where(v => v.ReservationGroupId == reservationGroupId)
                        .OrderByDescending(v => v.IsRepresentative)]);

    public Task<int> UpdateGroupStatusAsync(Guid reservationGroupId, VisitorStatus status, IDbSession? session = null)
    {
        // 実装と同様にキャンセル済みは対象外とする
        var targets = Visitors
            .Where(v => v.ReservationGroupId == reservationGroupId && v.Status != VisitorStatus.Cancelled)
            .ToList();

        foreach (var visitor in targets)
        {
            visitor.Status = status;
        }

        return Task.FromResult(targets.Count);
    }

    public Task DeleteGroupAsync(Guid reservationGroupId, IDbSession? session = null)
    {
        Visitors.RemoveAll(v => v.ReservationGroupId == reservationGroupId);
        return Task.CompletedTask;
    }

    public Task DeleteVisitorAsync(Guid visitorId, IDbSession? session = null)
    {
        Visitors.RemoveAll(v => v.VisitorId == visitorId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ManualCheckOutTarget>> SearchManualCheckOutTargetsAsync(
        string? visitorName, DateOnly? visitDate, int? siteId)
    {
        var results = Visitors
            .Where(v => v.Status == VisitorStatus.CheckedIn)
            .Where(v => string.IsNullOrWhiteSpace(visitorName) || v.VisitorName.Contains(visitorName, StringComparison.Ordinal))
            .Where(v => !visitDate.HasValue || v.VisitDate == visitDate.Value)
            .Where(v => !siteId.HasValue || v.SiteId == siteId.Value)
            .Select(v => new ManualCheckOutTarget
            {
                VisitorId = v.VisitorId,
                ReservationGroupId = v.ReservationGroupId,
                IsRepresentative = v.IsRepresentative,
                VisitorName = v.VisitorName,
                CompanyName = v.CompanyName,
                VisitDate = v.VisitDate,
                Status = v.Status,
                SiteId = v.SiteId,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ManualCheckOutTarget>>(results);
    }
}

/// <summary>
/// テスト用のQRトークンリポジトリ。
/// </summary>
internal sealed class FakeQrTokenRepository : IQrTokenRepository
{
    /// <summary>発行済みのトークン</summary>
    public List<QrToken> Tokens { get; } = [];

    public Task<Guid> CreateAsync(QrToken token, IDbSession? session = null)
    {
        if (token.TokenId == Guid.Empty)
        {
            token.TokenId = Guid.NewGuid();
        }

        Tokens.Add(token);
        return Task.FromResult(token.TokenId);
    }

    public Task<QrToken?> GetByTokenValueAsync(Guid tokenValue, IDbSession? session = null)
        => Task.FromResult(Tokens.FirstOrDefault(t => t.TokenValue == tokenValue));

    public Task<QrToken?> GetByVisitorIdAsync(Guid visitorId, IDbSession? session = null)
        => Task.FromResult(Tokens.FirstOrDefault(t => t.VisitorId == visitorId));

    public Task UpdateUsedCountAsync(Guid tokenId, byte usedCount, IDbSession? session = null)
    {
        var token = Tokens.FirstOrDefault(t => t.TokenId == tokenId);
        if (token is not null)
        {
            token.UsedCount = usedCount;
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryConsumeAsync(Guid tokenId, byte expectedUsedCount, IDbSession session)
    {
        var token = Tokens.FirstOrDefault(t => t.TokenId == tokenId);
        if (token is null || token.UsedCount != expectedUsedCount)
        {
            return Task.FromResult(false);
        }

        token.UsedCount++;
        return Task.FromResult(true);
    }

    public Task ExtendExpirationAsync(Guid tokenId, DateTime expiresAt, IDbSession? session = null)
    {
        var token = Tokens.FirstOrDefault(t => t.TokenId == tokenId);
        if (token is not null)
        {
            token.ExpiresAt = expiresAt;
            token.IsValid = true;
            token.ReissueCount++;
        }

        return Task.CompletedTask;
    }

    public Task UpdateExpirationAsync(Guid tokenId, DateTime expiresAt, IDbSession? session = null)
    {
        var token = Tokens.FirstOrDefault(t => t.TokenId == tokenId);
        if (token is not null)
        {
            token.ExpiresAt = expiresAt;
        }

        return Task.CompletedTask;
    }

    public Task IncrementReissueCountAsync(Guid tokenId, IDbSession? session = null)
    {
        var token = Tokens.FirstOrDefault(t => t.TokenId == tokenId);
        if (token is not null)
        {
            token.ReissueCount++;
        }

        return Task.CompletedTask;
    }

    public Task InvalidateAsync(Guid tokenId, IDbSession? session = null)
    {
        var token = Tokens.FirstOrDefault(t => t.TokenId == tokenId);
        if (token is not null)
        {
            token.IsValid = false;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// テスト用の入退館履歴リポジトリ。
/// </summary>
internal sealed class FakeAccessLogRepository : IAccessLogRepository
{
    /// <summary>登録された履歴</summary>
    public List<AccessLog> Logs { get; } = [];

    public Task<long> CreateAsync(AccessLog log, IDbSession? session = null)
    {
        log.LogId = Logs.Count + 1;
        Logs.Add(log);
        return Task.FromResult(log.LogId);
    }

    public Task CreateManyAsync(IEnumerable<AccessLog> logs, IDbSession? session = null)
    {
        foreach (var log in logs)
        {
            log.LogId = Logs.Count + 1;
            Logs.Add(log);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccessLog>> SearchAsync(AccessLogSearchCondition condition)
        => Task.FromResult<IReadOnlyList<AccessLog>>([.. Logs]);

    public Task<IReadOnlyList<AccessLog>> GetForArchiveAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
        => Task.FromResult<IReadOnlyList<AccessLog>>(
            [.. Logs.Where(l => l.AccessDateTime >= fromUtcInclusive && l.AccessDateTime < toUtcExclusive)]);

    public Task<int> DeleteOlderThanAsync(DateTime thresholdUtc)
        => Task.FromResult(Logs.RemoveAll(l => l.AccessDateTime < thresholdUtc));
}

/// <summary>
/// テスト用の拠点マスタリポジトリ。
/// </summary>
internal sealed class FakeSiteRepository : ISiteRepository
{
    /// <summary>登録済みの拠点</summary>
    public List<Site> Sites { get; } = [];

    public Task<Site?> GetByIdAsync(int siteId, IDbSession? session = null)
        => Task.FromResult(Sites.FirstOrDefault(s => s.SiteId == siteId));

    public Task<IReadOnlyList<Site>> GetAllAsync(bool activeOnly = true, IDbSession? session = null)
        => Task.FromResult<IReadOnlyList<Site>>([.. Sites.Where(s => !activeOnly || s.IsActive)]);

    public Task<int> CreateAsync(Site site, IDbSession? session = null)
    {
        site.SiteId = Sites.Count + 1;
        Sites.Add(site);
        return Task.FromResult(site.SiteId);
    }

    public Task UpdateAsync(Site site, IDbSession? session = null) => Task.CompletedTask;

    public Task<int> CountAsync(IDbSession? session = null)
        => Task.FromResult(Sites.Count(s => s.IsActive));
}

/// <summary>
/// テスト用の操作ユーザーリポジトリ。
/// </summary>
internal sealed class FakeOperatorRepository : IOperatorRepository
{
    /// <summary>登録済みの操作ユーザー</summary>
    public List<Operator> Operators { get; } = [];

    public Task<Operator?> GetByIdAsync(int operatorId, IDbSession? session = null)
        => Task.FromResult(Operators.FirstOrDefault(o => o.OperatorId == operatorId));

    public Task<Operator?> GetByLoginIdAsync(string loginId, IDbSession? session = null)
        => Task.FromResult(Operators.FirstOrDefault(o => o.LoginId == loginId));

    public Task<IReadOnlyList<Operator>> GetAllAsync(bool activeOnly = false, IDbSession? session = null)
        => Task.FromResult<IReadOnlyList<Operator>>([.. Operators.Where(o => !activeOnly || o.IsActive)]);

    public Task<int> CreateAsync(Operator op, IDbSession? session = null)
    {
        op.OperatorId = Operators.Count + 1;
        Operators.Add(op);
        return Task.FromResult(op.OperatorId);
    }

    public Task UpdateAsync(Operator op, IDbSession? session = null) => Task.CompletedTask;

    public Task UpdatePasswordHashAsync(int operatorId, string passwordHash, IDbSession? session = null)
        => Task.CompletedTask;

    public Task<bool> ExistsLoginIdAsync(string loginId, int? excludeOperatorId = null, IDbSession? session = null)
        => Task.FromResult(Operators.Any(o => o.LoginId == loginId && o.OperatorId != excludeOperatorId));
}

/// <summary>
/// テスト用のメール送信履歴リポジトリ。
/// </summary>
internal sealed class FakeMailHistoryRepository : IMailHistoryRepository
{
    /// <summary>記録された送信履歴</summary>
    public List<MailHistory> Histories { get; } = [];

    public Task<long> CreateAsync(MailHistory history, IDbSession? session = null)
    {
        history.MailId = Histories.Count + 1;
        Histories.Add(history);
        return Task.FromResult(history.MailId);
    }

    public Task<IReadOnlyList<MailHistory>> SearchAsync(MailHistorySearchCondition condition)
        => Task.FromResult<IReadOnlyList<MailHistory>>([.. Histories]);

    public Task<IReadOnlyList<MailHistory>> GetForArchiveAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
        => Task.FromResult<IReadOnlyList<MailHistory>>(
            [.. Histories.Where(h => h.SentAt >= fromUtcInclusive && h.SentAt < toUtcExclusive)]);

    public Task<int> DeleteOlderThanAsync(DateTime thresholdUtc)
        => Task.FromResult(Histories.RemoveAll(h => h.SentAt < thresholdUtc));
}

/// <summary>
/// テスト用のメール送信。実送信は行わず、呼び出し内容を記録する。
/// </summary>
internal sealed class FakeMailSender : IMailSender
{
    /// <summary>送信を試みた宛先区分の記録</summary>
    public List<MailRecipientType> SentRecipients { get; } = [];

    public MailProviderType ProviderType => MailProviderType.SendGrid;

    /// <summary>本番相当の送信としてテストするため false（テストモード表示の検証時のみ切り替える）</summary>
    public bool IsTestMode { get; set; }

    public Task SendVisitInvitationAsync(
        Visitor visitor, Operator registeredBy, byte[] qrCodePng,
        MailRecipientType recipientType, IReadOnlyList<Visitor>? companions = null)
    {
        SentRecipients.Add(recipientType);
        return Task.CompletedTask;
    }

    public Task SendReminderAsync(Visitor visitor) => Task.CompletedTask;

    public Task SendCancellationNoticeAsync(Visitor visitor) => Task.CompletedTask;

    public Task ResendInvitationAsync(
        Visitor visitor, Operator registeredBy, byte[] qrCodePng,
        MailRecipientType recipientType = MailRecipientType.Invitee, IReadOnlyList<Visitor>? companions = null)
    {
        SentRecipients.Add(recipientType);
        return Task.CompletedTask;
    }
}

/// <summary>
/// テスト用のQRコード画像生成。実際の画像生成は行わない。
/// </summary>
internal sealed class FakeQrCodeImageGenerator : IQrCodeImageGenerator
{
    public byte[] GeneratePng(Guid tokenValue, int pixelsPerModule = 10) => [0x89, 0x50, 0x4E, 0x47];
}
