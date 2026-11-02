using System.Globalization;
using System.Net;
using System.Text;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Infrastructure.Mail;

/*
 * 来館案内メールの定型文（設計書 6.3節・6.12節）。
 *
 * 多言語対応方針（設計書 2.5節・6.12節・17章 確認事項No.5・18章 確認事項No.3）:
 *   - 対象は「受付Web入力画面」と「来館案内メールの定型文」のみ。日英併記方式とする。
 *   - 英訳文言は管理画面からの編集項目とはせず、プログラム上に固定文言として実装する。
 *   - 顧客ごとに文言を調整できるよう、該当行の文頭に「// ★レイアウト調整」を付与する。
 */

/// <summary>
/// メール本文の組み立て。日本語・英語を同一文面内に併記する。
/// </summary>
public static class MailTemplates
{
    // ★レイアウト調整 来館案内メールの件名（日英併記）
    private const string SubjectInvitation = "【ご来館のご案内】QRコードのご送付 / Visit Invitation - Your QR Code";

    // ★レイアウト調整 再送信メールの件名（日英併記）
    private const string SubjectResend = "【再送】ご来館のご案内 / [Resent] Visit Invitation";

    // ★レイアウト調整 リマインドメールの件名（日英併記）
    private const string SubjectReminder = "【リマインド】ご来館予定のお知らせ / Reminder: Your Upcoming Visit";

    // ★レイアウト調整 キャンセル通知メールの件名（日英併記）
    private const string SubjectCancellation = "【ご来館予約キャンセルのお知らせ】/ Visit Reservation Cancelled";

    // ★レイアウト調整 予約登録者（控え）宛の件名接頭辞
    private const string SubjectPrefixForRegistrant = "【控え】";

    /// <summary>QRコード画像の添付ファイル名</summary>
    public const string QrAttachmentFileName = "visit-qrcode.png";

    /// <summary>メールの件名を組み立てる</summary>
    public static string BuildSubject(MailType mailType, MailRecipientType recipientType)
    {
        var subject = mailType switch
        {
            MailType.VisitInvitation => SubjectInvitation,
            MailType.Resend => SubjectResend,
            MailType.Reminder => SubjectReminder,
            MailType.CancellationNotice => SubjectCancellation,
            _ => SubjectInvitation,
        };

        // 予約登録者宛は控えであることが分かるよう接頭辞を付ける（設計書 5.2節）
        return recipientType == MailRecipientType.Registrant ? SubjectPrefixForRegistrant + subject : subject;
    }

    /// <summary>
    /// 来館案内メール／再送信メールの本文（プレーンテキスト）を組み立てる。
    /// </summary>
    public static string BuildInvitationBody(
        Visitor representative,
        Operator registeredBy,
        MailRecipientType recipientType,
        IReadOnlyList<Visitor>? companions,
        string? siteName)
    {
        var sb = new StringBuilder();
        var visitDateText = FormatVisitDate(representative);
        var visitTimeText = FormatVisitTime(representative);

        if (recipientType == MailRecipientType.Registrant)
        {
            // ★レイアウト調整 予約登録者（控え）宛の冒頭文
            sb.AppendLine($"予約担当者：{registeredBy.DisplayName}");
            sb.AppendLine();
            // ★レイアウト調整
            sb.AppendLine("以下の内容で来館予約を登録し、招待者様へ来館案内メールを送信しました。");
            // ★レイアウト調整
            sb.AppendLine("本メールは控えです。招待者様がメールを紛失・未着の場合は、本メールをそのまま転送してご対応ください。");
            sb.AppendLine();
            // ★レイアウト調整
            sb.AppendLine("This is a copy for your records. If the visitor did not receive the invitation,");
            // ★レイアウト調整
            sb.AppendLine("you may forward this email directly to them.");
        }
        else
        {
            // ★レイアウト調整 招待者（代表者）宛の冒頭文
            sb.AppendLine($"{representative.VisitorName} 様");
            sb.AppendLine();
            // ★レイアウト調整
            sb.AppendLine($"担当者{registeredBy.DisplayName}より来館予約のご案内を送付いたします。");
            // ★レイアウト調整
            sb.AppendLine("当日は添付のQRコードを受付端末にかざしてください。");
            sb.AppendLine();
            // ★レイアウト調整
            sb.AppendLine("Thank you for scheduling your visit.");
            // ★レイアウト調整
            sb.AppendLine("Please present the attached QR code at the reception terminal on the day of your visit.");
        }

        sb.AppendLine();
        // ★レイアウト調整 予約内容の見出し
        sb.AppendLine("■ ご予約内容 / Reservation Details");
        sb.AppendLine("--------------------------------------------------");
        // ★レイアウト調整
        sb.AppendLine($"来館日 / Date            : {visitDateText}");
        // ★レイアウト調整
        sb.AppendLine($"来館時刻 / Time          : {visitTimeText}");

        if (!string.IsNullOrWhiteSpace(siteName))
        {
            // ★レイアウト調整
            sb.AppendLine($"来館先拠点 / Location    : {siteName}");
        }
        if (!string.IsNullOrWhiteSpace(representative.HostEmployeeName))
        {
            // ★レイアウト調整
            sb.AppendLine($"訪問先担当者 / Host      : {representative.HostEmployeeName}");
        }
        if (!string.IsNullOrWhiteSpace(representative.PurposeOfVisit))
        {
            // ★レイアウト調整
            sb.AppendLine($"来館目的 / Purpose       : {representative.PurposeOfVisit}");
        }

        // ★レイアウト調整
        sb.AppendLine($"代表者 / Representative  : {FormatMemberLine(representative)}");
        sb.AppendLine("--------------------------------------------------");

        // 同行者の氏名・会社名を一覧表示する（設計書 6.3節）
        if (companions is { Count: > 0 })
        {
            sb.AppendLine();
            // ★レイアウト調整 同行者一覧の見出し
            sb.AppendLine($"■ ご同行者 / Accompanying Visitors ({companions.Count} 名)");
            sb.AppendLine("--------------------------------------------------");
            foreach (var companion in companions)
            {
                sb.AppendLine($"  ・{FormatMemberLine(companion)}");
            }
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine();
            // ★レイアウト調整 グループ単位読取の説明（QRは代表者1名分のみ発行される）
            sb.AppendLine("※ QRコードは代表者様お一人にのみ発行されます。受付では代表者様のQRコード1枚で");
            // ★レイアウト調整
            sb.AppendLine("   ご同行者様を含む全員の入館・退館手続きが完了します。");
            // ★レイアウト調整
            sb.AppendLine("* One QR code is issued to the representative only. Scanning it once completes");
            // ★レイアウト調整
            sb.AppendLine("  check-in (and later check-out) for the entire group.");
        }

        sb.AppendLine();
        // ★レイアウト調整 QRコードの有効時間帯の案内（設計書 6.5節）
        sb.AppendLine("※ QRコードは来館予定時刻の30分前から2時間後までご利用いただけます。");
        // ★レイアウト調整
        sb.AppendLine("* The QR code is valid from 30 minutes before to 2 hours after the scheduled time.");
        // ★レイアウト調整
        sb.AppendLine("※ 1回目の読み取りで入館、2回目の読み取りで退館の受付となります。");
        // ★レイアウト調整
        sb.AppendLine("* The first scan checks you in; the second scan checks you out.");

        sb.AppendLine();
        // ★レイアウト調整 フッター（問い合わせ先は予約登録者）
        sb.AppendLine("--------------------------------------------------");
        // ★レイアウト調整
        sb.AppendLine($"ご不明な点は下記までお問い合わせください。 / For inquiries:");
        sb.AppendLine($"  {registeredBy.DisplayName} <{registeredBy.Email}>");
        // ★レイアウト調整
        sb.AppendLine("本メールは送信専用です。ご返信いただいてもお答えできません。");
        // ★レイアウト調整
        sb.AppendLine("This message was sent from a send-only address. Replies are not monitored.");

        return sb.ToString();
    }

    /// <summary>リマインドメールの本文を組み立てる</summary>
    public static string BuildReminderBody(Visitor representative)
    {
        var sb = new StringBuilder();
        // ★レイアウト調整
        sb.AppendLine($"{representative.VisitorName} 様");
        sb.AppendLine();
        // ★レイアウト調整
        sb.AppendLine("ご来館予定日が近づいてまいりましたのでお知らせいたします。");
        // ★レイアウト調整
        sb.AppendLine("This is a reminder of your upcoming visit.");
        sb.AppendLine();
        // ★レイアウト調整
        sb.AppendLine($"来館日 / Date   : {FormatVisitDate(representative)}");
        // ★レイアウト調整
        sb.AppendLine($"来館時刻 / Time : {FormatVisitTime(representative)}");
        sb.AppendLine();
        // ★レイアウト調整
        sb.AppendLine("当日は先にお送りしたQRコードをご持参ください。");
        // ★レイアウト調整
        sb.AppendLine("Please bring the QR code we sent you earlier.");

        return sb.ToString();
    }

    /// <summary>キャンセル通知メールの本文を組み立てる</summary>
    public static string BuildCancellationBody(Visitor representative)
    {
        var sb = new StringBuilder();
        // ★レイアウト調整
        sb.AppendLine($"{representative.VisitorName} 様");
        sb.AppendLine();
        // ★レイアウト調整
        sb.AppendLine("下記のご来館予約はキャンセルされました。");
        // ★レイアウト調整
        sb.AppendLine("The following visit reservation has been cancelled.");
        sb.AppendLine();
        // ★レイアウト調整
        sb.AppendLine($"来館日 / Date   : {FormatVisitDate(representative)}");
        // ★レイアウト調整
        sb.AppendLine($"来館時刻 / Time : {FormatVisitTime(representative)}");
        sb.AppendLine();
        // ★レイアウト調整
        sb.AppendLine("既にお送りしたQRコードは無効となります。");
        // ★レイアウト調整
        sb.AppendLine("The QR code previously sent to you is no longer valid.");

        return sb.ToString();
    }

    /// <summary>プレーンテキスト本文をHTMLメール用に変換する（等幅フォントで整形を保つ）</summary>
    public static string ToHtmlBody(string plainText)
    {
        var encoded = WebUtility.HtmlEncode(plainText).Replace("\r\n", "\n").Replace("\n", "<br />");
        // ★レイアウト調整 HTMLメールの外枠スタイル
        return $"""
            <html><body style="font-family:'Meiryo','Segoe UI',sans-serif; font-size:14px; line-height:1.7; color:#222;">
            <div style="white-space:pre-wrap;">{encoded}</div>
            </body></html>
            """;
    }

    /// <summary>来館日を日英で表記する</summary>
    private static string FormatVisitDate(Visitor visitor)
        => visitor.VisitDate.ToString("yyyy年M月d日 (ddd)", CultureInfo.GetCultureInfo("ja-JP"))
         + " / " + visitor.VisitDate.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));

    /// <summary>来館時刻を表記する（終了時刻が未設定の場合は開始時刻のみ）</summary>
    private static string FormatVisitTime(Visitor visitor)
        => visitor.VisitTimeTo.HasValue
            ? $"{visitor.VisitTimeFrom:HH\\:mm} - {visitor.VisitTimeTo.Value:HH\\:mm}"
            : $"{visitor.VisitTimeFrom:HH\\:mm}";

    /// <summary>来館者1名の表記（氏名＋会社名）</summary>
    private static string FormatMemberLine(Visitor visitor)
        => string.IsNullOrWhiteSpace(visitor.CompanyName)
            ? visitor.VisitorName
            : $"{visitor.VisitorName}（{visitor.CompanyName}）";
}
