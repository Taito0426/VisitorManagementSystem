using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// メール送信の共通契約（設計書 5.4節）。
/// 設計書に定義された4メソッドのシグネチャをそのまま維持し、
/// 6.3節の「同行者の氏名・会社名を本文に一覧記載する」要件のため、
/// 同行者一覧を既定値付きの省略可能引数として追加している（既存の呼び出し形式はそのまま有効）。
/// 送信失敗時は例外を送出し、呼び出し側（サービス層）が MailHistory へ結果を記録する。
/// </summary>
public interface IMailSender
{
    /// <summary>使用中のメール送信プロバイダ（MailHistory.Provider の記録に使用）</summary>
    MailProviderType ProviderType { get; }

    /// <summary>
    /// テストモード（実際の外部送信を行わない）か。
    /// デモ版・Debugビルドで true となり、画面の送信結果表示を「テスト」に切り替えるために使用する。
    /// 送信履歴（MailHistory）へは本番と同じく記録する。
    /// </summary>
    bool IsTestMode { get; }

    /// <summary>来館案内メールを送信する</summary>
    /// <param name="visitor">代表者の来館予約</param>
    /// <param name="registeredBy">予約登録者（操作ユーザー）</param>
    /// <param name="qrCodePng">代表者用QRコード画像（PNG）</param>
    /// <param name="recipientType">1=招待者(代表者のみ) 2=予約登録者(Operators.Email)</param>
    /// <param name="companions">同行者一覧（本文に氏名・会社名を記載する）</param>
    Task SendVisitInvitationAsync(
        Visitor visitor,
        Operator registeredBy,
        byte[] qrCodePng,
        MailRecipientType recipientType,
        IReadOnlyList<Visitor>? companions = null);

    /// <summary>リマインドメールを送信する</summary>
    Task SendReminderAsync(Visitor visitor);

    /// <summary>キャンセル通知メールを送信する</summary>
    Task SendCancellationNoticeAsync(Visitor visitor);

    /// <summary>来館案内メールを再送信する（設計書 5.3節・6.7節）</summary>
    Task ResendInvitationAsync(
        Visitor visitor,
        Operator registeredBy,
        byte[] qrCodePng,
        MailRecipientType recipientType = MailRecipientType.Invitee,
        IReadOnlyList<Visitor>? companions = null);
}
