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
/// 来館予約機能（設計書 6.1節・6.2節・6.6節・6.7節）。
/// 事前予約・当日受付は同一のロジックを共有し、当日受付に特別な例外フローを設けない。
/// QRトークンは代表者の Visitors 行にのみ1件発行する（グループ単位読取）。
/// </summary>
public sealed class ReservationService(
    IDbSessionFactory sessionFactory,
    IVisitorRepository visitorRepository,
    IQrTokenRepository qrTokenRepository,
    IAccessLogRepository accessLogRepository,
    ISiteRepository siteRepository,
    IOperatorRepository operatorRepository,
    IQrCodeImageGenerator qrCodeImageGenerator,
    MailDeliveryService mailDeliveryService,
    ILogger<ReservationService> logger)
{
    /// <summary>
    /// 来館予約を登録する（事前予約・当日受付の共通処理）。
    /// 登録完了時にQRトークンを1件発行し、来館案内メールを送信する。
    ///
    /// 当日受付（ReservationType=2）は、来館者が受付にいる状態で登録するため、
    /// 登録＝入館とみなして発行時刻で入館登録まで行う。
    /// あわせてQRトークンを「1回読取済み（UsedCount=1）」の状態で発行するため、
    /// 発行したQRの最初の読取が2回目の読取＝退館として処理される。
    /// </summary>
    public async Task<ReservationResult> CreateAsync(ReservationInput input, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(ctx);

        if (!PermissionPolicy.CanCreateReservation(ctx))
        {
            throw PermissionDeniedException.ForOperation("来館予約の登録");
        }
        if (input.ReservationType == ReservationType.WalkIn && !PermissionPolicy.CanUseReception(ctx))
        {
            throw PermissionDeniedException.ForOperation("当日受付登録");
        }

        await ValidateAsync(input).ConfigureAwait(false);

        var groupId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // 当日受付は登録＝入館とみなし、発行時刻で入館済みの状態を作る
        var isWalkIn = input.ReservationType == ReservationType.WalkIn;
        var initialStatus = isWalkIn ? VisitorStatus.CheckedIn : VisitorStatus.NotArrived;

        var representative = BuildVisitor(input, input.Representative, groupId, isRepresentative: true, ctx);
        var companions = input.Companions
            .Select(member => BuildVisitor(input, member, groupId, isRepresentative: false, ctx))
            .ToList();

        representative.Status = initialStatus;
        foreach (var companion in companions)
        {
            companion.Status = initialStatus;
        }

        var token = new QrToken
        {
            TokenId = Guid.NewGuid(),
            VisitorId = representative.VisitorId,
            TokenValue = Guid.NewGuid(),
            IssuedAt = now,
            // 有効期限は来館予定時刻の2時間後まで。当日受付は受付時刻を基準時刻とする（設計書 6.2節・6.5節）
            ExpiresAt = VisitRules.CalculateExpiresAtUtc(input.VisitDate, input.VisitTimeFrom),
            // 当日受付は入館済みとして発行するため、読取回数を1から開始する。
            // これにより発行したQRの最初の読取が「2回目＝退館」として処理される。
            UsedCount = isWalkIn ? (byte)1 : (byte)0,
            ReissueCount = 0,
            IsValid = true,
        };

        // 予約グループ・QRトークン・（当日受付の場合は）入館履歴を1トランザクションで登録する
        using (var session = sessionFactory.CreateSession(beginTransaction: true))
        {
            await visitorRepository.CreateAsync(representative, session).ConfigureAwait(false);
            foreach (var companion in companions)
            {
                await visitorRepository.CreateAsync(companion, session).ConfigureAwait(false);
            }

            await qrTokenRepository.CreateAsync(token, session).ConfigureAwait(false);

            if (isWalkIn)
            {
                // グループ内の来館者ごとに入館履歴を1件ずつ記録する（設計書 9.2.5節と同じ粒度）
                var checkInLogs = new[] { representative }
                    .Concat(companions)
                    .Select(member => new AccessLog
                    {
                        VisitorId = member.VisitorId,
                        SiteId = member.SiteId,
                        TokenId = token.TokenId,
                        AccessType = AccessType.CheckIn,
                        AccessDateTime = now,
                        DeviceInfo = $"当日受付登録 ({ctx.LoginId})",
                        OperatorNote = "当日受付登録時に入館登録",
                    });

                await accessLogRepository.CreateManyAsync(checkInLogs, session).ConfigureAwait(false);
            }

            session.Commit();
        }

        logger.LogInformation(
            "来館予約を登録しました。GroupId: {GroupId} / 種別: {ReservationType} / 人数: {Count}名 / 初期ステータス: {Status}",
            groupId, input.ReservationType, companions.Count + 1, initialStatus);

        // 画面表示・メール本文で使用する拠点名を補完する
        var site = await siteRepository.GetByIdAsync(input.SiteId).ConfigureAwait(false);
        representative.SiteName = site?.SiteName;
        representative.CreatedByDisplayName = ctx.DisplayName;

        var registeredBy = await GetOperatorAsync(ctx.OperatorId).ConfigureAwait(false);

        var group = new ReservationGroup
        {
            ReservationGroupId = groupId,
            Representative = representative,
            Companions = companions,
            Token = token,
            RegisteredBy = registeredBy,
        };

        var qrCodePng = qrCodeImageGenerator.GeneratePng(token.TokenValue);

        // 当日受付は受付でその場で完結するため、予約登録者宛の控え送信を省略する（設計書 6.6節）
        var sendToRegistrant = input.ReservationType == ReservationType.Advance;
        var mailResults = await mailDeliveryService
            .SendInvitationAsync(group, registeredBy, qrCodePng, MailType.VisitInvitation, sendToRegistrant)
            .ConfigureAwait(false);

        return new ReservationResult(group, qrCodePng, mailResults);
    }

    /// <summary>
    /// 来館予約を更新する。代表者・同行者の増減にも対応する。
    /// 予約日時が変更された場合はQRトークンの有効期限を再計算する。
    /// </summary>
    public async Task<ReservationGroup> UpdateAsync(Guid reservationGroupId, ReservationInput input, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(ctx);

        if (!PermissionPolicy.CanEditReservation(ctx))
        {
            throw PermissionDeniedException.ForOperation("来館予約の編集");
        }

        await ValidateAsync(input).ConfigureAwait(false);

        var existing = await GetGroupAsync(reservationGroupId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の来館予約が見つかりません。");

        // 一般ロールは自分が登録した予約のみ編集できる
        if (!PermissionPolicy.CanOperateOnReservation(ctx, existing.Representative))
        {
            throw PermissionDeniedException.ForOperation("他の担当者が登録した来館予約の編集");
        }

        if (existing.Representative.Status is VisitorStatus.CheckedOut or VisitorStatus.Cancelled)
        {
            throw new BusinessRuleException("退館済み、またはキャンセル済みの予約は編集できません。");
        }

        using (var session = sessionFactory.CreateSession(beginTransaction: true))
        {
            // ① 代表者を更新する
            ApplyMember(existing.Representative, input, input.Representative);
            await visitorRepository.UpdateAsync(existing.Representative, session).ConfigureAwait(false);

            // ② 同行者：入力に含まれる既存行は更新し、含まれない既存行は削除する
            var inputCompanionIds = input.Companions
                .Where(c => c.VisitorId.HasValue)
                .Select(c => c.VisitorId!.Value)
                .ToHashSet();

            foreach (var removed in existing.Companions.Where(c => !inputCompanionIds.Contains(c.VisitorId)))
            {
                await visitorRepository.DeleteVisitorAsync(removed.VisitorId, session).ConfigureAwait(false);
            }

            foreach (var member in input.Companions)
            {
                if (member.VisitorId.HasValue
                    && existing.Companions.FirstOrDefault(c => c.VisitorId == member.VisitorId.Value) is { } target)
                {
                    ApplyMember(target, input, member);
                    await visitorRepository.UpdateAsync(target, session).ConfigureAwait(false);
                }
                else
                {
                    var added = BuildVisitor(input, member, reservationGroupId, isRepresentative: false, ctx);
                    added.CreatedByOperatorId = existing.Representative.CreatedByOperatorId;
                    await visitorRepository.CreateAsync(added, session).ConfigureAwait(false);
                }
            }

            // ③ 予約日時が変わった場合はQRトークンの有効期限を追随させる（未使用トークンのみ）
            if (existing.Token is { UsedCount: 0 } token)
            {
                var newExpiresAt = VisitRules.CalculateExpiresAtUtc(input.VisitDate, input.VisitTimeFrom);
                if (newExpiresAt != token.ExpiresAt)
                {
                    await qrTokenRepository.UpdateExpirationAsync(token.TokenId, newExpiresAt, session).ConfigureAwait(false);
                }
            }

            session.Commit();
        }

        logger.LogInformation("来館予約を更新しました。GroupId: {GroupId}", reservationGroupId);

        var updated = await GetGroupAsync(reservationGroupId).ConfigureAwait(false);
        return updated ?? throw new BusinessRuleException("更新後の来館予約を取得できませんでした。");
    }

    /// <summary>
    /// 来館予約をキャンセルする（設計書 7.1節。退館済みからのキャンセルは不可）。
    /// QRトークンを無効化し、招待者へキャンセル通知メールを送信する。
    /// </summary>
    public async Task CancelAsync(Guid reservationGroupId, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!PermissionPolicy.CanEditReservation(ctx))
        {
            throw PermissionDeniedException.ForOperation("来館予約のキャンセル");
        }

        var group = await GetGroupAsync(reservationGroupId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の来館予約が見つかりません。");

        // 一般ロールは自分が登録した予約のみキャンセルできる
        if (!PermissionPolicy.CanOperateOnReservation(ctx, group.Representative))
        {
            throw PermissionDeniedException.ForOperation("他の担当者が登録した来館予約のキャンセル");
        }

        if (!VisitRules.CanCancel(group.Representative.Status))
        {
            throw new BusinessRuleException("退館済み、またはキャンセル済みの予約はキャンセルできません。");
        }

        using (var session = sessionFactory.CreateSession(beginTransaction: true))
        {
            await visitorRepository
                .UpdateGroupStatusAsync(reservationGroupId, VisitorStatus.Cancelled, session).ConfigureAwait(false);

            if (group.Token is { } token)
            {
                await qrTokenRepository.InvalidateAsync(token.TokenId, session).ConfigureAwait(false);
            }

            session.Commit();
        }

        logger.LogInformation("来館予約をキャンセルしました。GroupId: {GroupId}", reservationGroupId);

        await mailDeliveryService.SendCancellationNoticeAsync(group.Representative).ConfigureAwait(false);
    }

    /// <summary>
    /// 来館予約を削除する。
    /// 入退館履歴が存在する予約は履歴保全のため削除できず、キャンセル操作を使用する。
    /// </summary>
    public async Task DeleteAsync(Guid reservationGroupId, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // 削除は履歴ごと消える操作のため、一般ロールには許可しない（キャンセルで代替する）
        if (!PermissionPolicy.CanDeleteReservation(ctx))
        {
            throw PermissionDeniedException.ForOperation("来館予約の削除");
        }

        var group = await GetGroupAsync(reservationGroupId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の来館予約が見つかりません。");

        if (group.AllMembers.Any(m => m.Status is VisitorStatus.CheckedIn or VisitorStatus.CheckedOut))
        {
            throw new BusinessRuleException(
                "入館・退館の記録がある予約は削除できません。履歴を残すためキャンセル操作を使用してください。");
        }

        using var session = sessionFactory.CreateSession(beginTransaction: true);
        await visitorRepository.DeleteGroupAsync(reservationGroupId, session).ConfigureAwait(false);
        session.Commit();

        logger.LogInformation("来館予約を削除しました。GroupId: {GroupId}", reservationGroupId);
    }

    /// <summary>
    /// 来館案内メールを再送信する（設計書 5.3節・6.7節）。
    /// 招待者・予約登録者の双方へ送信し、QRトークンの再発行回数を加算する。
    /// </summary>
    public async Task<IReadOnlyList<MailDeliveryResult>> ResendMailAsync(Guid reservationGroupId, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!PermissionPolicy.CanResendMail(ctx))
        {
            throw PermissionDeniedException.ForOperation("メール再送信");
        }

        var group = await GetGroupAsync(reservationGroupId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の来館予約が見つかりません。");

        var representative = group.Representative;

        // 当日受付分・退館済み・キャンセル済みは再送信の対象外（設計書 5.3節・6.7節）
        if (!VisitRules.CanResendMail(representative.Status, representative.ReservationType))
        {
            throw new BusinessRuleException(
                "当日受付で登録された予約、および退館済み・キャンセル済みの予約は再送信できません。");
        }

        var token = group.Token
            ?? throw new BusinessRuleException("対象の予約にQRコードが発行されていません。");

        using (var session = sessionFactory.CreateSession(beginTransaction: true))
        {
            var isReusable = token.IsValid && token.ExpiresAt > DateTime.UtcNow && token.UsedCount == 0;
            if (isReusable)
            {
                // 有効期限内かつ未使用：既存トークンをそのまま再送し、再発行回数のみ加算する
                await qrTokenRepository.IncrementReissueCountAsync(token.TokenId, session).ConfigureAwait(false);
            }
            else
            {
                // 期限切れ・無効：有効期限を再計算して延長したうえで再発行回数を加算する
                var newExpiresAt = VisitRules.CalculateExpiresAtUtc(
                    representative.VisitDate, representative.VisitTimeFrom);
                await qrTokenRepository.ExtendExpirationAsync(token.TokenId, newExpiresAt, session).ConfigureAwait(false);
                token.ExpiresAt = newExpiresAt;
                token.IsValid = true;
            }

            token.ReissueCount = (byte)Math.Min(token.ReissueCount + 1, byte.MaxValue);
            session.Commit();
        }

        var qrCodePng = qrCodeImageGenerator.GeneratePng(token.TokenValue);
        var registeredBy = await GetOperatorAsync(representative.CreatedByOperatorId).ConfigureAwait(false);

        logger.LogInformation("来館案内メールを再送信します。GroupId: {GroupId}", reservationGroupId);

        return await mailDeliveryService
            .SendInvitationAsync(group, registeredBy, qrCodePng, MailType.Resend, sendToRegistrant: true)
            .ConfigureAwait(false);
    }

    /// <summary>予約グループを取得する（代表者・同行者・QRトークン）</summary>
    public async Task<ReservationGroup?> GetGroupAsync(Guid reservationGroupId)
    {
        var members = await visitorRepository.GetByGroupIdAsync(reservationGroupId).ConfigureAwait(false);
        if (members.Count == 0)
        {
            return null;
        }

        var representative = members.FirstOrDefault(m => m.IsRepresentative) ?? members[0];
        var token = await qrTokenRepository.GetByVisitorIdAsync(representative.VisitorId).ConfigureAwait(false);

        // 予約登録者はメール文面の宛先・問い合わせ先として使用する（取得できない場合も照会自体は成立させる）
        var registeredBy = await operatorRepository
            .GetByIdAsync(representative.CreatedByOperatorId).ConfigureAwait(false);

        return new ReservationGroup
        {
            ReservationGroupId = reservationGroupId,
            Representative = representative,
            Companions = [.. members.Where(m => !m.IsRepresentative)],
            Token = token,
            RegisteredBy = registeredBy,
        };
    }

    /// <summary>
    /// 来館予約を検索する。ログイン中のロールに応じて閲覧範囲を自動的に絞り込む（設計書 11.6節）。
    /// </summary>
    public Task<IReadOnlyList<Visitor>> SearchAsync(VisitorSearchCondition condition, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(ctx);

        PermissionPolicy.ApplyViewScope(ctx, condition);
        return visitorRepository.SearchAsync(condition);
    }

    /// <summary>QRコード画像を取得する（再表示・再印刷用）</summary>
    public byte[] GenerateQrCodePng(QrToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return qrCodeImageGenerator.GeneratePng(token.TokenValue);
    }

    /// <summary>入力値の妥当性を検証する</summary>
    private async Task ValidateAsync(ReservationInput input)
    {
        if (input.SiteId <= 0)
        {
            throw new BusinessRuleException("来館先の拠点を選択してください。");
        }

        var site = await siteRepository.GetByIdAsync(input.SiteId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("指定された拠点が存在しません。");

        if (!site.IsActive)
        {
            throw new BusinessRuleException("廃止された拠点には来館予約を登録できません。");
        }

        if (string.IsNullOrWhiteSpace(input.Representative.VisitorName))
        {
            throw new BusinessRuleException("代表者の氏名を入力してください。");
        }

        // 事前予約の代表者はメールアドレス必須。当日受付は任意（設計書 6.6節・9.2.3節）
        if (input.ReservationType == ReservationType.Advance
            && string.IsNullOrWhiteSpace(input.Representative.VisitorEmail))
        {
            throw new BusinessRuleException("事前予約では代表者のメールアドレスが必須です。");
        }

        if (input.VisitTimeTo.HasValue && input.VisitTimeTo.Value < input.VisitTimeFrom)
        {
            throw new BusinessRuleException("来館終了時刻は開始時刻以降で入力してください。");
        }

        for (var i = 0; i < input.Companions.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(input.Companions[i].VisitorName))
            {
                throw new BusinessRuleException($"同行者{i + 1}人目の氏名を入力してください。");
            }
        }
    }

    /// <summary>入力から Visitors エンティティを生成する</summary>
    private static Visitor BuildVisitor(
        ReservationInput input, ReservationMemberInput member, Guid groupId, bool isRepresentative, OperatorContext ctx)
        => new()
        {
            VisitorId = Guid.NewGuid(),
            ReservationGroupId = groupId,
            IsRepresentative = isRepresentative,
            SiteId = input.SiteId,
            CompanyName = Trim(member.CompanyName),
            VisitorName = member.VisitorName.Trim(),
            VisitorNameKana = Trim(member.VisitorNameKana),
            // 同行者のメールアドレス欄は未使用（設計書 16章 確認事項No.1）
            VisitorEmail = isRepresentative ? Trim(member.VisitorEmail) : null,
            PhoneNumber = Trim(member.PhoneNumber),
            VisitDate = input.VisitDate,
            VisitTimeFrom = input.VisitTimeFrom,
            VisitTimeTo = input.VisitTimeTo,
            HostEmployeeName = Trim(input.HostEmployeeName),
            PurposeOfVisit = Trim(input.PurposeOfVisit),
            ReservationType = input.ReservationType,
            CreatedByOperatorId = ctx.OperatorId,
            Status = VisitorStatus.NotArrived,
        };

    /// <summary>既存の Visitors エンティティへ入力値を反映する</summary>
    private static void ApplyMember(Visitor target, ReservationInput input, ReservationMemberInput member)
    {
        target.SiteId = input.SiteId;
        target.CompanyName = Trim(member.CompanyName);
        target.VisitorName = member.VisitorName.Trim();
        target.VisitorNameKana = Trim(member.VisitorNameKana);
        target.VisitorEmail = target.IsRepresentative ? Trim(member.VisitorEmail) : null;
        target.PhoneNumber = Trim(member.PhoneNumber);
        target.VisitDate = input.VisitDate;
        target.VisitTimeFrom = input.VisitTimeFrom;
        target.VisitTimeTo = input.VisitTimeTo;
        target.HostEmployeeName = Trim(input.HostEmployeeName);
        target.PurposeOfVisit = Trim(input.PurposeOfVisit);
    }

    /// <summary>予約登録者を取得する</summary>
    private async Task<Operator> GetOperatorAsync(int operatorId)
        => await operatorRepository.GetByIdAsync(operatorId).ConfigureAwait(false)
           ?? throw new BusinessRuleException("予約登録者の情報を取得できませんでした。");

    /// <summary>空白のみの入力は null として扱う</summary>
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 来館予約の登録結果。
/// </summary>
/// <param name="Group">登録された予約グループ</param>
/// <param name="QrCodePng">代表者用QRコード画像（PNG）</param>
/// <param name="MailResults">メール送信結果</param>
public sealed record ReservationResult(
    ReservationGroup Group, byte[] QrCodePng, IReadOnlyList<MailDeliveryResult> MailResults);
