using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Printing;

/// <summary>
/// デモ版のラベル印刷（設計書 19.4節）。
/// 物理プリンタを用意しない前提のため、印刷の代わりにレイアウト確認用のA4 PDFを生成する。
///
/// A4 1枚を上下に分割し、
///   上半分: Sites.LabelSizeType のラベル実寸を点線枠で示し、枠内に本番と同一のQRコード・社名・ロゴを配置する
///   下半分: 実際に送信される来館案内メールの文面を、左＝予約登録者（控え）宛／右＝来訪者代表（招待者）宛で並べる
/// 構成とし、ラベルとメールのレイアウトを1枚で確認できるようにする。
/// </summary>
public sealed class DemoPdfLabelPrinter(
    IOptions<PrintingSettings> printingOptions,
    ILabelPresetProvider presetProvider,
    IMailBodyComposer mailBodyComposer,
    ILogger<DemoPdfLabelPrinter> logger) : ILabelPrinter, ILabelPdfPreview
{
    /// <summary>左右の余白（mm）</summary>
    private const double MarginLeftMm = 15;
    private const double MarginRightMm = 15;

    /// <summary>A4の用紙幅（mm）</summary>
    private const double PageWidthMm = 210;

    /// <summary>ラベル枠を配置する上端の余白（mm。見出しの下）</summary>
    private const double LabelTopMm = 33;

    /// <summary>プリセット枠どうしの間隔（mm）</summary>
    private const double GapMm = 12;

    /// <summary>上半分（ラベル）と下半分（メール文）の境界（mm）</summary>
    private const double HalfLineMm = 148.5;

    /// <summary>メール文カラムどうしの間隔（mm）</summary>
    private const double ColumnGapMm = 8;

    /// <summary>下半分の見出し・カラムヘッダの位置（mm）</summary>
    private const double MailSectionTitleMm = 154.5;
    private const double ColumnHeaderTopMm = 157.5;
    private const double ColumnHeaderHeightMm = 6;

    /// <summary>宛先・件名・添付などのメタ情報を描画する領域（mm）</summary>
    private const double MetaFirstBaselineMm = 165.5;
    private const double MetaLineHeightMm = 2.9;
    private const int MetaMaxLines = 5;
    private const double MetaSeparatorMm = 178;

    /// <summary>メール本文を描画する領域（mm）</summary>
    private const double BodyFirstBaselineMm = 180.5;
    private const double BodyBottomMm = 285;

    /// <summary>メール本文の行送り（フォントサイズに対する倍率）</summary>
    private const double BodyLineSpacing = 1.25;

    /// <summary>メール本文のフォントサイズ候補（収まるまで順に縮小する）</summary>
    private static readonly double[] BodyFontSizes = [6.5, 6.0, 5.5, 5.0];

    /// <summary>本文が収まりきらない場合に末尾へ付ける表示</summary>
    private const string TruncationMark = "…（以下省略 / truncated）";

    static DemoPdfLabelPrinter()
    {
        // PDFsharp のコアパッケージはシステムフォントを自動解決しないため、初回に一度だけ登録する
        GlobalFontSettings.FontResolver ??= new JapaneseFontResolver();
    }

    /// <inheritdoc />
    public async Task PrintQrLabelAsync(ReservationGroup group, byte[] qrCodePng, Site site)
    {
        ArgumentNullException.ThrowIfNull(group);

        var pdf = await GeneratePreviewPdfAsync(group, qrCodePng, site).ConfigureAwait(false);

        var outputDirectory = string.IsNullOrWhiteSpace(printingOptions.Value.PdfOutputPath)
            ? Path.Combine(Path.GetTempPath(), "VisitorManagementDemoLabels")
            : printingOptions.Value.PdfOutputPath;

        Directory.CreateDirectory(outputDirectory);

        var representative = group.Representative;
        var fileName = $"label-{representative.VisitDate:yyyyMMdd}-{representative.VisitorId:N}.pdf";
        var filePath = Path.Combine(outputDirectory, fileName);
        await File.WriteAllBytesAsync(filePath, pdf).ConfigureAwait(false);

        logger.LogInformation(
            "デモ版のため実機印刷は行わず、ラベルレイアウトをPDFとして出力しました: {FilePath}", filePath);
    }

    /// <inheritdoc />
    public Task<byte[]> GeneratePreviewPdfAsync(ReservationGroup group, byte[] qrCodePng, Site site)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(qrCodePng);
        ArgumentNullException.ThrowIfNull(site);

        using var document = new PdfDocument();
        document.Info.Title = "来館者記録管理システム ラベル・メールレイアウトプレビュー（デモ版）";

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;

        using var gfx = XGraphics.FromPdfPage(page);
        using var qrImage = XImage.FromStream(new MemoryStream(qrCodePng));

        DrawHeader(gfx, group.Representative, site);
        DrawLabelArea(gfx, group.Representative, site, qrImage);
        DrawMailArea(gfx, group, site);
        DrawFooter(gfx);

        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return Task.FromResult(stream.ToArray());
    }

    // ------------------------------------------------------------------
    // 上半分：ラベル（QRコード）レイアウト
    // ------------------------------------------------------------------

    /// <summary>ページ上部の見出しを描画する</summary>
    private static void DrawHeader(XGraphics gfx, Visitor visitor, Site site)
    {
        var titleFont = new XFont("Japanese", 14, XFontStyleEx.Bold);
        var textFont = new XFont("Japanese", 9);

        gfx.DrawString("ラベル・メールレイアウトプレビュー（デモ版）", titleFont, XBrushes.Black, Mm(MarginLeftMm), Mm(15));

        var info = $"拠点: {site.SiteName}　/　来館者: {visitor.VisitorName}　/　来館日: "
                 + visitor.VisitDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        gfx.DrawString(info, textFont, XBrushes.Black, Mm(MarginLeftMm), Mm(22));

        gfx.DrawString(
            "点線枠は実際のラベル寸法（実寸）です。太枠が本拠点に設定されているプリセットです。",
            textFont, XBrushes.DimGray, Mm(MarginLeftMm), Mm(27));
    }

    /// <summary>上半分にラベルプリセットを並べて描画する</summary>
    private void DrawLabelArea(XGraphics gfx, Visitor visitor, Site site, XImage qrImage)
    {
        // ラベル高さが上半分を超える設定でも下半分へはみ出さないよう、描画領域を切り取る
        var state = gfx.Save();
        gfx.IntersectClip(new XRect(0, Mm(LabelTopMm - 6), Mm(PageWidthMm), Mm(HalfLineMm - LabelTopMm + 3)));

        var offsetXMm = MarginLeftMm;
        foreach (var preset in presetProvider.GetAllPresets())
        {
            if (!preset.IsDefined)
            {
                // プリセット2・3は導入時ヒアリングにより個別設定する空の設定項目（設計書 18章 確認事項No.2）
                DrawUndefinedPreset(gfx, preset, offsetXMm);
                offsetXMm += 45 + GapMm;
                continue;
            }

            var isActive = preset.SizeType == site.LabelSizeType;
            DrawLabelPreview(gfx, preset, offsetXMm, visitor, site, qrImage, isActive);
            offsetXMm += preset.WidthMm + GapMm;
        }

        gfx.Restore(state);
    }

    /// <summary>1つのプリセットのラベルレイアウトを実寸で描画する</summary>
    private static void DrawLabelPreview(
        XGraphics gfx, LabelPreset preset, double offsetXMm, Visitor visitor, Site site, XImage qrImage, bool isActive)
    {
        var x = Mm(offsetXMm);
        var y = Mm(LabelTopMm);
        var width = Mm(preset.WidthMm);
        var height = Mm(preset.HeightMm);

        // 実寸のラベル外形を点線で示す
        var borderPen = new XPen(isActive ? XColors.Black : XColors.Gray, isActive ? 1.4 : 0.7)
        {
            DashStyle = XDashStyle.Dash,
        };
        gfx.DrawRectangle(borderPen, x, y, width, height);

        // プリセット名と寸法（枠の上に表示）
        var captionFont = new XFont("Japanese", 8, isActive ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        var caption = $"プリセット{(int)preset.SizeType}: 幅{preset.WidthMm}mm × 高さ{preset.HeightMm}mm"
                    + (isActive ? "（適用中）" : string.Empty);
        gfx.DrawString(caption, captionFont, XBrushes.Black, x, y - 4);

        var innerMargin = Mm(4);
        var contentX = x + innerMargin;
        var contentWidth = width - (innerMargin * 2);
        var cursorY = y + innerMargin;

        // ① ロゴ画像（登録されている場合のみ）
        if (site.LabelLogoImage is { Length: > 0 } logoBytes)
        {
            try
            {
                using var logo = XImage.FromStream(new MemoryStream(logoBytes));
                var logoHeight = Mm(10);
                var logoWidth = Math.Min(contentWidth, logoHeight * logo.PixelWidth / logo.PixelHeight);
                gfx.DrawImage(logo, contentX, cursorY, logoWidth, logoHeight);
                cursorY += logoHeight + Mm(2);
            }
            catch (InvalidOperationException)
            {
                // 画像として読み込めない場合はロゴなしで続行する
            }
        }

        // ② 表示社名
        var companyFont = new XFont("Japanese", 10, XFontStyleEx.Bold);
        var companyName = string.IsNullOrWhiteSpace(site.LabelCompanyName) ? site.SiteName : site.LabelCompanyName;
        gfx.DrawString(companyName, companyFont, XBrushes.Black,
            new XRect(contentX, cursorY, contentWidth, Mm(6)), XStringFormats.TopLeft);
        cursorY += Mm(8);

        // ③ QRコード（枠幅いっぱいの正方形）
        var qrSize = Math.Min(contentWidth, height - (cursorY - y) - Mm(18));
        gfx.DrawImage(qrImage, contentX + ((contentWidth - qrSize) / 2), cursorY, qrSize, qrSize);
        cursorY += qrSize + Mm(3);

        // ④ 来館者氏名・来館日
        var nameFont = new XFont("Japanese", 11, XFontStyleEx.Bold);
        var dateFont = new XFont("Japanese", 8);
        gfx.DrawString(visitor.VisitorName, nameFont, XBrushes.Black,
            new XRect(contentX, cursorY, contentWidth, Mm(6)), XStringFormats.TopCenter);
        cursorY += Mm(6);
        gfx.DrawString(visitor.VisitDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture), dateFont, XBrushes.Black,
            new XRect(contentX, cursorY, contentWidth, Mm(5)), XStringFormats.TopCenter);
    }

    /// <summary>未定義のプリセット枠（導入時ヒアリングで設定する枠）を描画する</summary>
    private static void DrawUndefinedPreset(XGraphics gfx, LabelPreset preset, double offsetXMm)
    {
        var x = Mm(offsetXMm);
        var y = Mm(LabelTopMm);
        var width = Mm(45);
        var height = Mm(60);

        var pen = new XPen(XColors.LightGray, 0.7) { DashStyle = XDashStyle.Dot };
        gfx.DrawRectangle(pen, x, y, width, height);

        var font = new XFont("Japanese", 8);
        gfx.DrawString($"プリセット{(int)preset.SizeType}", font, XBrushes.Gray, x, y - 4);
        gfx.DrawString("未設定", font, XBrushes.Gray,
            new XRect(x, y + (height / 2) - Mm(6), width, Mm(5)), XStringFormats.TopCenter);
        gfx.DrawString("（導入時ヒアリングで", font, XBrushes.Gray,
            new XRect(x, y + (height / 2), width, Mm(5)), XStringFormats.TopCenter);
        gfx.DrawString("個別設定します）", font, XBrushes.Gray,
            new XRect(x, y + (height / 2) + Mm(5), width, Mm(5)), XStringFormats.TopCenter);
    }

    // ------------------------------------------------------------------
    // 下半分：メール文面レイアウト
    // ------------------------------------------------------------------

    /// <summary>下半分に送信メール2通の文面を左右に並べて描画する</summary>
    private void DrawMailArea(XGraphics gfx, ReservationGroup group, Site site)
    {
        // 上下の区切り線
        gfx.DrawLine(new XPen(XColors.Black, 0.8), Mm(MarginLeftMm), Mm(HalfLineMm),
            Mm(PageWidthMm - MarginRightMm), Mm(HalfLineMm));

        gfx.DrawString("■ 送信メール文面プレビュー / Outgoing e-mail preview",
            new XFont("Japanese", 10, XFontStyleEx.Bold), XBrushes.Black, Mm(MarginLeftMm), Mm(MailSectionTitleMm));

        var representative = group.Representative;

        // 予約登録者が取得できない場合でもレイアウトを確認できるよう、表示用のダミーを立てる
        var registrant = group.RegisteredBy ?? new Operator
        {
            DisplayName = representative.CreatedByDisplayName ?? "（予約登録者）",
            Email = "（未取得）",
        };

        var leftBody = mailBodyComposer.BuildInvitationBody(
            representative, registrant, MailRecipientType.Registrant, group.Companions, site.SiteName);
        var rightBody = mailBodyComposer.BuildInvitationBody(
            representative, registrant, MailRecipientType.Invitee, group.Companions, site.SiteName);

        var columnWidthMm = (PageWidthMm - MarginLeftMm - MarginRightMm - ColumnGapMm) / 2;
        var columnWidth = Mm(columnWidthMm);

        // 2カラムのうち行数が多いほうを基準に、下端へ収まるフォントサイズを選ぶ
        var layout = LayoutBodies(gfx, leftBody, rightBody, columnWidth);

        logger.LogDebug(
            "メール文面を {FontSize}pt で描画します。左: {LeftLines}行 / 右: {RightLines}行 / 収容可能: {Capacity}行",
            layout.BodyFont.Size, layout.LeftLines.Count, layout.RightLines.Count,
            (int)((BodyBottomMm - BodyFirstBaselineMm) / layout.LineHeightMm));

        DrawMailColumn(gfx, MarginLeftMm, columnWidthMm, layout,
            title: "【左】予約登録者（控え）宛 / Copy for the registrant",
            recipient: $"{registrant.DisplayName} <{registrant.Email}>",
            subject: mailBodyComposer.BuildSubject(MailType.VisitInvitation, MailRecipientType.Registrant),
            note: BuildRegistrantNote(group),
            lines: layout.LeftLines);

        DrawMailColumn(gfx, MarginLeftMm + columnWidthMm + ColumnGapMm, columnWidthMm, layout,
            title: "【右】来訪者代表（招待者）宛 / Invitation to the visitor",
            recipient: string.IsNullOrWhiteSpace(representative.VisitorEmail)
                ? $"{representative.VisitorName} <（メールアドレス未登録）>"
                : $"{representative.VisitorName} <{representative.VisitorEmail}>",
            subject: mailBodyComposer.BuildSubject(MailType.VisitInvitation, MailRecipientType.Invitee),
            note: string.IsNullOrWhiteSpace(representative.VisitorEmail)
                ? "※ メールアドレス未登録のため実際には送信されません（レイアウト見本）。"
                : null,
            lines: layout.RightLines);
    }

    /// <summary>予約登録者（控え）宛カラムに添える注記を組み立てる</summary>
    private static string? BuildRegistrantNote(ReservationGroup group)
    {
        // 当日受付は受付でその場で完結するため、控えメールは送信しない（設計書 6.6節）
        if (group.Representative.ReservationType == ReservationType.WalkIn)
        {
            return "※ 当日受付では控えメールは送信されません（レイアウト見本）。";
        }

        return group.RegisteredBy is null
            ? "※ 予約登録者の情報を取得できないため、宛先はレイアウト見本です。"
            : null;
    }

    /// <summary>メール1通分のカラムを描画する</summary>
    private void DrawMailColumn(
        XGraphics gfx, double xMm, double widthMm, BodyLayout layout,
        string title, string recipient, string subject, string? note, IReadOnlyList<string> lines)
    {
        var x = Mm(xMm);
        var width = Mm(widthMm);

        // カラム見出し（帯）
        gfx.DrawRectangle(XBrushes.LightGray, x, Mm(ColumnHeaderTopMm), width, Mm(ColumnHeaderHeightMm));
        gfx.DrawString(title, new XFont("Japanese", 8, XFontStyleEx.Bold), XBrushes.Black,
            new XRect(x + Mm(1.5), Mm(ColumnHeaderTopMm), width - Mm(3), Mm(ColumnHeaderHeightMm)),
            XStringFormats.CenterLeft);

        // 宛先・件名・添付・注記（領域内に収まる行数のみ表示する）
        var metaFont = new XFont("Japanese", 6);
        var headLines = new List<string>();
        headLines.AddRange(WrapText(gfx, $"宛先 / To      : {recipient}", metaFont, width));
        headLines.AddRange(WrapText(gfx, $"件名 / Subject : {subject}", metaFont, width));
        headLines.AddRange(WrapText(gfx, $"添付 / Attach  : {mailBodyComposer.QrAttachmentFileName}", metaFont, width));

        // 注記は送信可否を示す重要情報のため、行数が足りない場合は宛先側を削って必ず表示する
        var noteLines = note is null ? [] : WrapText(gfx, note, metaFont, width);
        var headCapacity = Math.Min(headLines.Count, Math.Max(MetaMaxLines - noteLines.Count, 1));
        var metaLines = headLines.Take(headCapacity).Concat(noteLines).Take(MetaMaxLines).ToList();

        for (var i = 0; i < metaLines.Count; i++)
        {
            // 注記は目に留まるよう色を変える
            var brush = i >= headCapacity ? XBrushes.Firebrick : XBrushes.Black;
            gfx.DrawString(metaLines[i], metaFont, brush, x, Mm(MetaFirstBaselineMm + (i * MetaLineHeightMm)));
        }

        gfx.DrawLine(new XPen(XColors.Gray, 0.4), x, Mm(MetaSeparatorMm), x + width, Mm(MetaSeparatorMm));

        // 本文
        for (var i = 0; i < lines.Count; i++)
        {
            gfx.DrawString(lines[i], layout.BodyFont, XBrushes.Black,
                x, Mm(BodyFirstBaselineMm + (i * layout.LineHeightMm)));
        }
    }

    /// <summary>
    /// 2カラム分の本文を折り返し、下端へ収まる最大のフォントサイズを選ぶ。
    /// 最小サイズでも収まらない場合は末尾を省略する。
    /// </summary>
    private static BodyLayout LayoutBodies(XGraphics gfx, string leftBody, string rightBody, double columnWidth)
    {
        var availableHeightMm = BodyBottomMm - BodyFirstBaselineMm;

        XFont font = null!;
        var lineHeightMm = 0d;
        List<string> left = [];
        List<string> right = [];

        foreach (var size in BodyFontSizes)
        {
            font = new XFont("Japanese", size);
            lineHeightMm = XUnit.FromPoint(size * BodyLineSpacing).Millimeter;
            left = WrapText(gfx, leftBody, font, columnWidth);
            right = WrapText(gfx, rightBody, font, columnWidth);

            var requiredMm = Math.Max(left.Count, right.Count) * lineHeightMm;
            if (requiredMm <= availableHeightMm)
            {
                return new BodyLayout(font, lineHeightMm, left, right);
            }
        }

        // 最小サイズでも収まらないため、表示できる行数で打ち切る
        var maxLines = Math.Max((int)(availableHeightMm / lineHeightMm), 1);
        return new BodyLayout(font, lineHeightMm, Truncate(left, maxLines), Truncate(right, maxLines));
    }

    /// <summary>指定行数を超える分を省略表示に置き換える</summary>
    private static List<string> Truncate(List<string> lines, int maxLines)
    {
        if (lines.Count <= maxLines)
        {
            return lines;
        }

        var truncated = lines.GetRange(0, maxLines);
        truncated[^1] = TruncationMark;
        return truncated;
    }

    /// <summary>
    /// プレーンテキストを指定幅で折り返す。
    /// 半角スペースがあればそこで、無ければ（日本語など）文字単位で折り返す。
    /// </summary>
    private static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var result = new List<string>();

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (rawLine.Length == 0)
            {
                result.Add(string.Empty);
                continue;
            }

            var current = new StringBuilder();
            var breakPosition = -1;

            foreach (var ch in rawLine)
            {
                current.Append(ch);

                if (gfx.MeasureString(current.ToString(), font).Width <= maxWidth)
                {
                    if (ch == ' ')
                    {
                        breakPosition = current.Length;
                    }
                    continue;
                }

                if (breakPosition > 0)
                {
                    result.Add(current.ToString(0, breakPosition).TrimEnd());
                    var remainder = current.ToString(breakPosition, current.Length - breakPosition);
                    current.Clear();
                    current.Append(remainder);
                }
                else
                {
                    // 直前までを1行として確定し、あふれた1文字を次行の先頭に送る
                    result.Add(current.ToString(0, current.Length - 1));
                    current.Clear();
                    current.Append(ch);
                }

                breakPosition = -1;
            }

            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>ページ下部の注記を描画する</summary>
    private static void DrawFooter(XGraphics gfx)
    {
        var font = new XFont("Japanese", 8);

        gfx.DrawString(
            "※ 本PDFはデモ・商談用のレイアウト確認機能です。実運用では感熱式ラベルプリンタへ直接印刷し、メールは各宛先へ送信されます。",
            font, XBrushes.DimGray, Mm(MarginLeftMm), Mm(289.5));
        gfx.DrawString(
            "※ 印刷方式の切替は appsettings.json の Printing:Provider（\"Ql800\" / \"PdfPreview\"）で行います。",
            font, XBrushes.DimGray, Mm(MarginLeftMm), Mm(293.5));
    }

    /// <summary>ミリメートルをPDFのポイント単位へ変換する</summary>
    private static double Mm(double millimeter) => XUnit.FromMillimeter(millimeter).Point;

    /// <summary>
    /// メール本文の描画設定。
    /// </summary>
    /// <param name="BodyFont">本文フォント（自動縮小後）</param>
    /// <param name="LineHeightMm">行送り（mm）</param>
    /// <param name="LeftLines">左カラム（予約登録者宛）の折り返し済み本文</param>
    /// <param name="RightLines">右カラム（来訪者代表宛）の折り返し済み本文</param>
    private sealed record BodyLayout(
        XFont BodyFont, double LineHeightMm, IReadOnlyList<string> LeftLines, IReadOnlyList<string> RightLines);
}
