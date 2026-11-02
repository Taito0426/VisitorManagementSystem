using Microsoft.Extensions.Logging;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Application.Services;

/// <summary>
/// メール送信と送信履歴記録の統括（設計書 5.2節・6.3節）。
/// 送信先は招待者（代表者）と予約登録者の2者のみ。同行者個人へは送信しない。
/// 各送信の結果はそれぞれ独立したレコードとして MailHistory に記録する。
/// </summary>
public sealed class MailDeliveryService(
    IMailSender mailSender,
    IMailHistoryRepository mailHistoryRepository,
    ILogger<MailDeliveryService> logger)
{
    /// <summary>
    /// 来館案内メールを招待者・予約登録者へ送信する。
    /// 送信失敗は予約登録自体を失敗させず、MailHistory に失敗として記録する。
    /// </summary>
    /// <param name="group">対象の予約グループ</param>
    /// <param name="registeredBy">予約登録者</param>
    /// <param name="qrCodePng">代表者用QRコード画像</param>
    /// <param name="mailType">来館案内（初回）または再送信</param>
    /// <param name="sendToRegistrant">
    /// 予約登録者宛の控えを送信するか。
    /// 当日受付ではその場で全処理が完結するため送信を省略する（設計書 6.6節）。
    /// </param>
    /// <returns>送信を試みた宛先ごとの結果</returns>
    public async Task<IReadOnlyList<MailDeliveryResult>> SendInvitationAsync(
        ReservationGroup group,
        Operator registeredBy,
        byte[] qrCodePng,
        MailType mailType,
        bool sendToRegistrant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(registeredBy);

        var results = new List<MailDeliveryResult>();
        var representative = group.Representative;

        // ① 招待者（代表者）宛。メールアドレス未入力の当日受付では送信自体を行わない（設計書 5.2節）
        if (!string.IsNullOrWhiteSpace(representative.VisitorEmail))
        {
            results.Add(await SendOneAsync(
                group, registeredBy, qrCodePng, mailType, MailRecipientType.Invitee).ConfigureAwait(false));
        }
        else
        {
            logger.LogInformation(
                "招待者のメールアドレスが未設定のため送信をスキップします。VisitorId: {VisitorId}", representative.VisitorId);
        }

        // ② 予約登録者（操作ユーザー）宛の控え。Operators.Email が空の場合も送信対象外として扱う
        if (sendToRegistrant && !string.IsNullOrWhiteSpace(registeredBy.Email))
        {
            results.Add(await SendOneAsync(
                group, registeredBy, qrCodePng, mailType, MailRecipientType.Registrant).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>キャンセル通知メールを招待者へ送信する</summary>
    public async Task<MailDeliveryResult?> SendCancellationNoticeAsync(Visitor representative)
    {
        ArgumentNullException.ThrowIfNull(representative);

        if (string.IsNullOrWhiteSpace(representative.VisitorEmail))
        {
            return null;
        }

        try
        {
            await mailSender.SendCancellationNoticeAsync(representative).ConfigureAwait(false);
            await RecordAsync(representative.VisitorId, MailType.CancellationNotice, MailRecipientType.Invitee, null)
                .ConfigureAwait(false);
            return new MailDeliveryResult(
                MailRecipientType.Invitee, representative.VisitorEmail, true, null, mailSender.IsTestMode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            logger.LogError(ex, "キャンセル通知メールの送信に失敗しました。VisitorId: {VisitorId}", representative.VisitorId);
            await RecordAsync(representative.VisitorId, MailType.CancellationNotice, MailRecipientType.Invitee, ex.Message)
                .ConfigureAwait(false);
            return new MailDeliveryResult(
                MailRecipientType.Invitee, representative.VisitorEmail, false, ex.Message, mailSender.IsTestMode);
        }
    }

    /// <summary>1宛先分の送信と履歴記録を行う</summary>
    private async Task<MailDeliveryResult> SendOneAsync(
        ReservationGroup group,
        Operator registeredBy,
        byte[] qrCodePng,
        MailType mailType,
        MailRecipientType recipientType)
    {
        var representative = group.Representative;
        var address = recipientType == MailRecipientType.Invitee ? representative.VisitorEmail! : registeredBy.Email;

        try
        {
            if (mailType == MailType.Resend)
            {
                await mailSender.ResendInvitationAsync(
                    representative, registeredBy, qrCodePng, recipientType, group.Companions).ConfigureAwait(false);
            }
            else
            {
                await mailSender.SendVisitInvitationAsync(
                    representative, registeredBy, qrCodePng, recipientType, group.Companions).ConfigureAwait(false);
            }

            await RecordAsync(representative.VisitorId, mailType, recipientType, null).ConfigureAwait(false);
            logger.LogInformation(
                "メールを送信しました。宛先区分: {RecipientType} / VisitorId: {VisitorId} / テストモード: {IsTest}",
                recipientType, representative.VisitorId, mailSender.IsTestMode);

            return new MailDeliveryResult(recipientType, address, true, null, mailSender.IsTestMode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            // 送信失敗は予約登録の成否に影響させず、履歴に失敗として残す（設計書 5.2節・5.5節）
            logger.LogError(ex,
                "メールの送信に失敗しました。宛先区分: {RecipientType} / VisitorId: {VisitorId}", recipientType, representative.VisitorId);

            await RecordAsync(representative.VisitorId, mailType, recipientType, ex.Message).ConfigureAwait(false);
            return new MailDeliveryResult(recipientType, address, false, ex.Message, mailSender.IsTestMode);
        }
    }

    /// <summary>送信結果を MailHistory へ記録する</summary>
    private Task RecordAsync(Guid visitorId, MailType mailType, MailRecipientType recipientType, string? errorMessage)
        => mailHistoryRepository.CreateAsync(new MailHistory
        {
            VisitorId = visitorId,
            MailType = mailType,
            RecipientType = recipientType,
            Provider = mailSender.ProviderType,
            SentAt = DateTime.UtcNow,
            Status = errorMessage is null ? MailSendStatus.Succeeded : MailSendStatus.Failed,
            ErrorMessage = errorMessage,
        });
}

/// <summary>
/// メール送信1件分の結果。
/// </summary>
/// <param name="RecipientType">宛先区分</param>
/// <param name="Address">送信先アドレス</param>
/// <param name="IsSuccess">送信に成功したか</param>
/// <param name="ErrorMessage">失敗時のエラー内容</param>
/// <param name="IsTest">
/// テストモード（デモ版・Debugビルド）で、実際の外部送信を行っていないか。
/// 画面の送信結果表示を「テスト」に切り替えるために使用する。
/// </param>
public readonly record struct MailDeliveryResult(
    MailRecipientType RecipientType, string Address, bool IsSuccess, string? ErrorMessage, bool IsTest = false)
{
    /// <summary>送信結果を画面表示用の文言にする</summary>
    public string ToDisplayText()
    {
        if (!IsSuccess)
        {
            return $"送信失敗 - {ErrorMessage}";
        }

        return IsTest ? "テスト（実際には送信していません）" : "送信成功";
    }
}
