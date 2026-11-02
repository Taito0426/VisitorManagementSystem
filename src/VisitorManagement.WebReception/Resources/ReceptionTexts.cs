namespace VisitorManagement.WebReception.Resources;

/*
 * 受付Web画面の表示文言（設計書 2.5節・6.12節）。
 *
 * 多言語対応方針（17章 確認事項No.5・18章 確認事項No.3）:
 *   - 対象は「受付Web入力画面（入力エラーメッセージを含む）」と「来館案内メールの定型文」のみ。
 *   - 言語切替UIではなく、同一画面内に日本語と英語を併記する。
 *   - 英訳文言は管理画面からの編集項目とはせず、プログラム上に固定文言として実装する。
 *   - 顧客ごとに文言を調整できるよう、該当行の文頭に「// ★レイアウト調整」を付与し検索しやすくする。
 *
 * QRコード読取結果表示画面（8.3節）は多言語対応の対象外であり、日本語のみとする。
 * その文言は ReceptionResultTexts に分けて定義している。
 */

/// <summary>
/// 当日受付登録画面など、日英併記の対象となる画面の文言。
/// </summary>
public static class ReceptionTexts
{
    // ===== 画面見出し =====

    // ★レイアウト調整 当日受付登録画面のタイトル
    public const string WalkInTitle = "当日受付登録 / Walk-in Registration";

    // ★レイアウト調整 当日受付登録画面の案内文
    public const string WalkInDescription =
        "ご来館ありがとうございます。以下の項目をご入力ください。 / Thank you for visiting. Please fill in the form below.";

    // ★レイアウト調整 代表者セクションの見出し
    public const string SectionRepresentative = "代表者 / Representative";

    // ★レイアウト調整 同行者セクションの見出し
    public const string SectionCompanions = "ご同行者 / Accompanying Visitors";

    // ★レイアウト調整 同行者セクションの補足
    public const string CompanionNote =
        "QRコードは代表者様に1枚のみ発行され、グループ全員の受付が完了します。"
        + " / One QR code is issued to the representative and covers the whole group.";

    // ===== 入力項目 =====

    // ★レイアウト調整
    public const string LabelCompanyName = "会社名 / Company";

    // ★レイアウト調整
    public const string LabelVisitorName = "お名前 / Name";

    // ★レイアウト調整
    public const string LabelVisitorNameKana = "フリガナ / Name (Kana)";

    // ★レイアウト調整
    public const string LabelEmail = "メールアドレス（任意） / Email (optional)";

    // ★レイアウト調整
    public const string LabelPhoneNumber = "電話番号 / Phone";

    // ★レイアウト調整
    public const string LabelHostEmployee = "訪問先担当者 / Person to Visit";

    // ★レイアウト調整
    public const string LabelPurpose = "ご用件 / Purpose of Visit";

    // ★レイアウト調整
    public const string LabelSite = "来館先拠点 / Location";

    // ===== ボタン =====

    // ★レイアウト調整
    public const string ButtonAddCompanion = "同行者を追加 / Add Companion";

    // ★レイアウト調整
    public const string ButtonRemoveCompanion = "削除 / Remove";

    // ★レイアウト調整
    public const string ButtonSubmit = "登録してQRコードを発行 / Register and Issue QR Code";

    // ★レイアウト調整
    public const string ButtonBackToReception = "受付画面へ戻る / Back to Reception";

    // ★レイアウト調整
    public const string ButtonPrintLabel = "ラベルを印刷 / Print Label";

    // ===== 入力エラーメッセージ（多言語対応の対象。設計書 2.5節） =====

    // ★レイアウト調整
    public const string ErrorVisitorNameRequired = "お名前を入力してください。 / Please enter your name.";

    // ★レイアウト調整
    public const string ErrorSiteRequired = "来館先の拠点を選択してください。 / Please select the location.";

    // ★レイアウト調整
    public const string ErrorEmailFormat = "メールアドレスの形式が正しくありません。 / The email address format is invalid.";

    // ★レイアウト調整
    public const string ErrorCompanionNameRequired =
        "同行者のお名前を入力してください。 / Please enter the accompanying visitor's name.";

    // ★レイアウト調整
    public const string ErrorGeneric = "登録に失敗しました。受付担当者にお申し出ください。 / Registration failed. Please contact the receptionist.";

    // ===== 登録完了 =====
    // 当日受付は登録した時点で入館登録まで完了する。
    // 発行するQRコードは退館時に使用するため、その旨を明示する文言にしている。

    // ★レイアウト調整 当日受付の完了タイトル
    public const string CompletedTitle = "入館の受付が完了しました / Check-in Completed";

    // ★レイアウト調整 当日受付の完了案内（QRコードは退館時に使用する）
    public const string CompletedDescription =
        "ご入館の手続きが完了しました。QRコードラベルをお受け取りのうえ、"
        + "ご退館の際に受付端末にかざしてください。"
        + " / You are now checked in. Please take the printed QR code label and scan it"
        + " at the reception terminal when you leave.";

    // ★レイアウト調整 入館登録が完了した旨（登録時刻を差し込む）
    public const string CheckedInAtFormat = "入館時刻: {0} / Checked in at {0}";

    // ★レイアウト調整
    public const string LabelPrintSucceeded = "QRコードラベルを印刷しました。 / The QR code label has been printed.";

    // ★レイアウト調整
    public const string LabelPrintFailed =
        "ラベルの印刷に失敗しました。受付担当者にお申し出ください。 / Failed to print the label. Please contact the receptionist.";

    // ★レイアウト調整
    public const string MailSent = "ご入力のメールアドレス宛に来館案内をお送りしました。 / A visit invitation has been sent to your email address.";

    // ★レイアウト調整 テストモード（デモ版・Debugビルド）で実送信を行わなかった場合の表示
    public const string MailTestMode =
        "【テスト】テストモードのため、来館案内メールは実際には送信されていません。 / [Test] Test mode: the invitation email was not actually sent.";
}

/// <summary>
/// QRコード読取結果表示画面の文言（設計書 8.3節・16章 確認事項No.6）。
/// 本画面は多言語対応の対象外のため日本語のみとする。
/// </summary>
public static class ReceptionResultTexts
{
    /// <summary>受付画面のタイトル</summary>
    public const string ReceptionTitle = "入退館受付";

    /// <summary>QRコード入力欄の案内</summary>
    public const string ScanPrompt = "QRコードを読み取り機にかざしてください";

    /// <summary>
    /// 読取待機中の補足。
    /// 当日受付で発行したQRは登録時点で入館済みのため、最初の読取が退館になる。
    /// </summary>
    public const string ScanHint =
        "読み取ると自動的に受付処理を行います。"
        + "事前予約のQRコードは 1回目＝入館 / 2回目＝退館、"
        + "当日受付で発行したQRコードは退館用です。";

    /// <summary>当日受付への導線</summary>
    public const string WalkInLink = "予約がない方はこちら（当日受付）";

    /// <summary>通信エラー時のメッセージ</summary>
    public const string CommunicationError = "受付処理に失敗しました。受付担当者にお申し出ください。";
}
