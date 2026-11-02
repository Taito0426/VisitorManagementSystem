using Microsoft.Extensions.Logging;
using VisitorManagement.Application.Exceptions;
using VisitorManagement.Core;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.Application.Services;

/// <summary>
/// 入退館受付機能（設計書 6.4節・6.4.1節・6.5節）。
///
/// 代表者QRの読取回数により処理を切り替える（グループ単位読取）。
///   1回目   : 入館受付。グループ全員を「入館中」へ遷移し、全員分の入館ログを記録する。
///   2回目   : 退館受付。グループ全員を「退館済」へ遷移し、全員分の退館ログを記録する。
///   3回目以降: エラー表示のみ。AccessLogs へエラーを記録する。
/// </summary>
public sealed class AccessService(
    IDbSessionFactory sessionFactory,
    IVisitorRepository visitorRepository,
    IQrTokenRepository qrTokenRepository,
    IAccessLogRepository accessLogRepository,
    ILogger<AccessService> logger)
{
    // --- 読取結果メッセージ（受付画面は日本語のみ。設計書 6.12節・8.3節） ---
    private const string MessageCheckedIn = "入館の受付が完了しました。";
    private const string MessageCheckedOut = "退館の受付が完了しました。ご来館ありがとうございました。";
    private const string MessageAlreadyCheckedOut = "このQRコードは既に退館処理済みです。受付担当者にお申し出ください。";
    private const string MessageUnknownToken = "QRコードを読み取れませんでした。受付担当者にお申し出ください。";
    private const string MessageInvalidToken = "このQRコードは無効です。予約がキャンセルされている可能性があります。";
    private const string MessageExpired = "このQRコードの有効期限が切れています。受付担当者にお申し出ください。";
    private const string MessageBeforeWindow = "受付可能時間の前です。来館予定時刻の30分前から受付できます。";
    private const string MessageCancelled = "この予約はキャンセルされています。受付担当者にお申し出ください。";
    private const string MessageConflict = "他の受付端末で処理中です。しばらくしてからもう一度読み取ってください。";

    /// <summary>
    /// AccessLogs.DeviceInfo の最大長（db/01_CreateSchema.sql の nvarchar(100)）。
    /// 拠点名・IPv6アドレス・読取経路（スキャナー／カメラ）の付記で超過しうるため、
    /// 記録前にこの長さへ丸めて桁あふれエラーを防ぐ。
    /// </summary>
    public const int DeviceInfoMaxLength = 100;

    /// <summary>DBの桁数に収まるよう端末情報を丸める</summary>
    public static string? TruncateDeviceInfo(string? deviceInfo)
        => deviceInfo is not null && deviceInfo.Length > DeviceInfoMaxLength
            ? deviceInfo[..DeviceInfoMaxLength]
            : deviceInfo;

    /// <summary>
    /// QRコードを読み取り、入退館受付を行う（受付Web画面の /api/access/scan から呼び出す）。
    /// </summary>
    /// <param name="tokenValue">QRコードに埋め込まれた値</param>
    /// <param name="deviceInfo">受付端末の識別情報</param>
    public async Task<ScanResult> ScanAsync(Guid tokenValue, string? deviceInfo)
    {
        // 受付端末が送る識別情報は長さが可変のため、DBの桁数に収まるよう丸める
        deviceInfo = TruncateDeviceInfo(deviceInfo);

        var nowUtc = DateTime.UtcNow;

        using var session = sessionFactory.CreateSession(beginTransaction: true);

        var token = await qrTokenRepository.GetByTokenValueAsync(tokenValue, session).ConfigureAwait(false);
        if (token is null)
        {
            // トークンが存在しない場合は対象の VisitorId が特定できないため AccessLogs へは記録できない
            logger.LogWarning("未登録のQRコードが読み取られました。TokenValue: {TokenValue}", tokenValue);
            return Failure(MessageUnknownToken);
        }

        var members = await visitorRepository.GetByGroupIdAsync(
            await GetGroupIdAsync(token.VisitorId, session).ConfigureAwait(false), session).ConfigureAwait(false);

        if (members.Count == 0)
        {
            return Failure(MessageUnknownToken);
        }

        var representative = members.FirstOrDefault(m => m.IsRepresentative) ?? members[0];

        // --- トークンの有効性検証（設計書 7.2節・11.4節） ---
        if (!token.IsValid)
        {
            return await RecordErrorAsync(session, representative, token, MessageInvalidToken, deviceInfo, nowUtc)
                .ConfigureAwait(false);
        }
        if (representative.Status == VisitorStatus.Cancelled)
        {
            return await RecordErrorAsync(session, representative, token, MessageCancelled, deviceInfo, nowUtc)
                .ConfigureAwait(false);
        }
        if (token.UsedCount >= 2)
        {
            return await RecordErrorAsync(session, representative, token, MessageAlreadyCheckedOut, deviceInfo, nowUtc)
                .ConfigureAwait(false);
        }
        if (token.ExpiresAt <= nowUtc)
        {
            return await RecordErrorAsync(session, representative, token, MessageExpired, deviceInfo, nowUtc)
                .ConfigureAwait(false);
        }

        // 受付時間帯（予約時刻の30分前〜2時間後）の下限チェック（設計書 6.5節）
        var validFromUtc = VisitRules.CalculateValidFromUtc(representative.VisitDate, representative.VisitTimeFrom);
        if (nowUtc < validFromUtc)
        {
            return await RecordErrorAsync(session, representative, token, MessageBeforeWindow, deviceInfo, nowUtc)
                .ConfigureAwait(false);
        }

        // --- 読取回数に応じた処理（同時読取は TryConsumeAsync で排他する） ---
        var expectedUsedCount = token.UsedCount;
        if (!await qrTokenRepository.TryConsumeAsync(token.TokenId, expectedUsedCount, session).ConfigureAwait(false))
        {
            logger.LogWarning("QRコードの同時読取を検出しました。TokenId: {TokenId}", token.TokenId);
            return Failure(MessageConflict);
        }

        var accessType = expectedUsedCount == 0 ? AccessType.CheckIn : AccessType.CheckOut;
        var newStatus = expectedUsedCount == 0 ? VisitorStatus.CheckedIn : VisitorStatus.CheckedOut;

        await visitorRepository
            .UpdateGroupStatusAsync(representative.ReservationGroupId, newStatus, session).ConfigureAwait(false);

        // グループ内の来館者ごとに AccessLogs を1件ずつ記録する（設計書 9.2.5節）
        var logs = members
            .Where(m => m.Status != VisitorStatus.Cancelled)
            .Select(m => new AccessLog
            {
                VisitorId = m.VisitorId,
                SiteId = m.SiteId,
                TokenId = token.TokenId,
                AccessType = accessType,
                AccessDateTime = nowUtc,
                DeviceInfo = deviceInfo,
            })
            .ToList();

        await accessLogRepository.CreateManyAsync(logs, session).ConfigureAwait(false);
        session.Commit();

        logger.LogInformation(
            "入退館受付を処理しました。区分: {AccessType} / GroupId: {GroupId} / 人数: {Count}名",
            accessType, representative.ReservationGroupId, logs.Count);

        return new ScanResult
        {
            IsSuccess = true,
            ResultType = accessType,
            Message = accessType == AccessType.CheckIn ? MessageCheckedIn : MessageCheckedOut,
            SiteName = representative.SiteName,
            Members = [.. members
                .Where(m => m.Status != VisitorStatus.Cancelled)
                .Select(m => ToResultMember(m, newStatus))],
        };
    }

    /// <summary>
    /// 手動退館処理（設計書 6.4.1節。グループはぐれ対応）。
    /// 対象者を個人単位で「退館済」に更新し、AccessLogs に備考付きで退館を記録する。
    /// 本機能は退館のみを対象とし、入館側の個別対応は設けない。
    /// </summary>
    /// <param name="visitorId">対象の来館者</param>
    /// <param name="operatorNote">備考（例:「グループはぐれのため手動退館処理」）</param>
    /// <param name="ctx">操作ユーザー（受付・管理者ロールのみ実行可）</param>
    public async Task ManualCheckOutAsync(Guid visitorId, string? operatorNote, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!PermissionPolicy.CanManualCheckOut(ctx))
        {
            throw PermissionDeniedException.ForOperation("手動退館処理");
        }

        using var session = sessionFactory.CreateSession(beginTransaction: true);

        var visitor = await visitorRepository.GetByIdAsync(visitorId, session).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の来館者が見つかりません。");

        if (visitor.Status != VisitorStatus.CheckedIn)
        {
            throw new BusinessRuleException("入館中の来館者のみ手動で退館処理できます。");
        }

        // 選択した来館者の Status のみを更新する（グループ全員は更新しない）
        await visitorRepository.UpdateStatusAsync(visitorId, VisitorStatus.CheckedOut, session).ConfigureAwait(false);

        // TokenId は代表者の共通QRトークンを参照する（同行者にはトークンが存在しないため）
        var representativeToken = await GetGroupTokenAsync(visitor.ReservationGroupId, session).ConfigureAwait(false);

        await accessLogRepository.CreateAsync(new AccessLog
        {
            VisitorId = visitorId,
            SiteId = visitor.SiteId,
            TokenId = representativeToken?.TokenId,
            AccessType = AccessType.CheckOut,
            AccessDateTime = DateTime.UtcNow,
            DeviceInfo = $"手動退館処理 ({ctx.LoginId})",
            OperatorNote = string.IsNullOrWhiteSpace(operatorNote)
                ? "グループはぐれのため手動退館処理"
                : operatorNote.Trim(),
        }, session).ConfigureAwait(false);

        session.Commit();

        logger.LogInformation(
            "手動退館処理を実行しました。VisitorId: {VisitorId} / 操作者: {LoginId}", visitorId, ctx.LoginId);
    }

    /// <summary>
    /// 手動退館処理の対象者を検索する（設計書 6.4.1節。氏名・来館日での検索に対応）。
    /// 受付ロールは自身の所属拠点に限定する。
    /// </summary>
    public Task<IReadOnlyList<ManualCheckOutTarget>> SearchManualCheckOutTargetsAsync(
        string? visitorName, DateOnly? visitDate, int? siteId, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!PermissionPolicy.CanManualCheckOut(ctx))
        {
            throw PermissionDeniedException.ForOperation("手動退館処理");
        }

        var (scopedSiteId, _) = PermissionPolicy.ResolveViewScope(ctx, siteId);
        return visitorRepository.SearchManualCheckOutTargetsAsync(visitorName, visitDate, scopedSiteId);
    }

    /// <summary>代表者の VisitorId から予約グループIDを取得する</summary>
    private async Task<Guid> GetGroupIdAsync(Guid representativeVisitorId, IDbSession session)
    {
        var visitor = await visitorRepository.GetByIdAsync(representativeVisitorId, session).ConfigureAwait(false);
        return visitor?.ReservationGroupId ?? Guid.Empty;
    }

    /// <summary>予約グループの代表者に紐づくQRトークンを取得する</summary>
    private async Task<QrToken?> GetGroupTokenAsync(Guid reservationGroupId, IDbSession session)
    {
        var members = await visitorRepository.GetByGroupIdAsync(reservationGroupId, session).ConfigureAwait(false);
        var representative = members.FirstOrDefault(m => m.IsRepresentative);
        return representative is null
            ? null
            : await qrTokenRepository.GetByVisitorIdAsync(representative.VisitorId, session).ConfigureAwait(false);
    }

    /// <summary>
    /// エラーを AccessLogs へ記録し、結果を返す（設計書 7.2節：DB更新はエラー記録に限る）。
    /// エラーは読取1回につき1件（代表者行）を記録する。
    /// </summary>
    private async Task<ScanResult> RecordErrorAsync(
        IDbSession session, Visitor representative, QrToken token, string message, string? deviceInfo, DateTime nowUtc)
    {
        await accessLogRepository.CreateAsync(new AccessLog
        {
            VisitorId = representative.VisitorId,
            SiteId = representative.SiteId,
            TokenId = token.TokenId,
            AccessType = AccessType.Error,
            AccessDateTime = nowUtc,
            DeviceInfo = deviceInfo,
            OperatorNote = message,
        }, session).ConfigureAwait(false);

        session.Commit();

        logger.LogInformation(
            "QRコード読取をエラーとして記録しました。TokenId: {TokenId} / 理由: {Message}", token.TokenId, message);

        return Failure(message);
    }

    /// <summary>エラー結果を生成する</summary>
    private static ScanResult Failure(string message) => new()
    {
        IsSuccess = false,
        ResultType = AccessType.Error,
        Message = message,
        Members = [],
    };

    /// <summary>読取結果に表示する来館者情報へ変換する</summary>
    private static ScanResultMember ToResultMember(Visitor visitor, VisitorStatus newStatus) => new()
    {
        VisitorId = visitor.VisitorId,
        VisitorName = visitor.VisitorName,
        CompanyName = visitor.CompanyName,
        IsRepresentative = visitor.IsRepresentative,
        Status = newStatus,
    };
}
