using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Printing;

/// <summary>
/// 感熱式ラベルプリンタ（ブラザー QL-800 想定）への印刷（設計書 6.6節・12.2節・19.4節）。
///
/// b-PAC SDK は開発環境に存在しない場合があるため、COM参照を直接追加せず
/// ProgID による遅延バインディング（late binding）で呼び出す。
/// これにより SDK 未インストール環境でもビルド・実行が可能で、
/// 実機導入時は SDK をインストールするだけで追加改修なく動作する。
/// SDK が見つからない場合は原因が分かるメッセージを添えて例外を送出する。
/// </summary>
public sealed class Ql800LabelPrinter(
    IOptions<PrintingSettings> printingOptions,
    ILabelPresetProvider presetProvider,
    ILogger<Ql800LabelPrinter> logger) : ILabelPrinter
{
    /// <summary>b-PAC SDK のドキュメントオブジェクトの ProgID</summary>
    private const string BpacProgId = "bpac.Document";

    /// <summary>ラベルテンプレート（.lbx）内のオブジェクト名。テンプレート側と一致させること</summary>
    private const string ObjectNameQrCode = "qrImage";
    private const string ObjectNameCompany = "companyName";
    private const string ObjectNameVisitorName = "visitorName";
    private const string ObjectNameVisitDate = "visitDate";
    private const string ObjectNameLogo = "logoImage";

    /// <inheritdoc />
    public Task PrintQrLabelAsync(ReservationGroup group, byte[] qrCodePng, Site site)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(qrCodePng);
        ArgumentNullException.ThrowIfNull(site);

        // 実機ラベルはQRを持つ代表者1名分のみ印刷する（グループ単位読取。設計書 6.2節）
        var visitor = group.Representative;

        // b-PAC SDK は Windows 版COMコンポーネントのため、他プラットフォームでは利用できない
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "b-PAC SDK による実機印刷は Windows でのみ利用できます。"
                + "Linux 上で受付Webを稼働させる構成では、ラベル印刷は Windows の受付端末側で行ってください。");
        }

        var settings = printingOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.LbxTemplatePath))
        {
            throw new InvalidOperationException(
                "ラベルテンプレート（Printing:LbxTemplatePath）が設定されていません。"
                + "P-touch Editor で作成した .lbx ファイルのパスを appsettings.json に設定してください。");
        }
        if (!File.Exists(settings.LbxTemplatePath))
        {
            throw new FileNotFoundException(
                $"ラベルテンプレートが見つかりません: {settings.LbxTemplatePath}", settings.LbxTemplatePath);
        }

        var comType = Type.GetTypeFromProgID(BpacProgId);
        if (comType is null)
        {
            throw new InvalidOperationException(
                "b-PAC SDK（ブラザー工業提供）がインストールされていません。"
                + "ラベルプリンタのドライバとあわせて b-PAC SDK をインストールしてください。"
                + "デモ・商談の場では Printing:Provider を \"PdfPreview\" に切り替えることで、"
                + "A4 PDFプレビューによりレイアウトを確認できます（設計書 19.4節）。");
        }

        // QRコード画像・ロゴ画像は一時ファイル経由で b-PAC に渡す
        var qrPath = WriteTempImage(qrCodePng, "png");
        var logoPath = site.LabelLogoImage is { Length: > 0 } logo ? WriteTempImage(logo, "png") : null;

        object? document = null;
        try
        {
            document = Activator.CreateInstance(comType)
                ?? throw new InvalidOperationException("b-PAC SDK のドキュメントオブジェクトを生成できませんでした。");

            if (!Invoke<bool>(document, "Open", settings.LbxTemplatePath))
            {
                throw new InvalidOperationException(
                    $"ラベルテンプレートを開けませんでした: {settings.LbxTemplatePath}");
            }

            if (!string.IsNullOrWhiteSpace(settings.PrinterName))
            {
                Invoke<bool>(document, "SetPrinter", settings.PrinterName, true);
            }

            var preset = presetProvider.GetPreset(site.LabelSizeType);
            logger.LogInformation(
                "ラベル印刷を開始します。拠点: {SiteName} / プリセット: {Preset} ({Width}mm×{Height}mm)",
                site.SiteName, preset.SizeType, preset.WidthMm, preset.HeightMm);

            // テンプレート上のオブジェクトへ値を差し込む（該当オブジェクトが無い場合は読み飛ばす）
            SetObjectText(document, ObjectNameCompany, site.LabelCompanyName ?? site.SiteName);
            SetObjectText(document, ObjectNameVisitorName, visitor.VisitorName);
            SetObjectText(document, ObjectNameVisitDate,
                visitor.VisitDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            SetObjectFilePath(document, ObjectNameQrCode, qrPath);
            if (logoPath is not null)
            {
                SetObjectFilePath(document, ObjectNameLogo, logoPath);
            }

            if (!Invoke<bool>(document, "StartPrint", string.Empty, 0))
            {
                throw new InvalidOperationException("ラベル印刷の開始に失敗しました。プリンタの接続状態を確認してください。");
            }

            Invoke<bool>(document, "PrintOut", 1, 0);
            Invoke<bool>(document, "EndPrint");
            Invoke<bool>(document, "Close");

            logger.LogInformation("ラベル印刷を完了しました。来館者: {VisitorName}", visitor.VisitorName);
            return Task.CompletedTask;
        }
        finally
        {
            if (document is not null)
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(document);
            }
            DeleteTempFile(qrPath);
            DeleteTempFile(logoPath);
        }
    }

    /// <summary>テンプレート上のテキストオブジェクトへ文字列を設定する</summary>
    private static void SetObjectText(object document, string objectName, string? text)
    {
        var target = Invoke<object?>(document, "GetObject", objectName);
        if (target is null)
        {
            return;
        }

        SetProperty(target, "Text", text ?? string.Empty);
    }

    /// <summary>テンプレート上の画像オブジェクトへファイルパスを設定する</summary>
    private static void SetObjectFilePath(object document, string objectName, string filePath)
    {
        var target = Invoke<object?>(document, "GetObject", objectName);
        if (target is null)
        {
            return;
        }

        SetProperty(target, "FilePath", filePath);
    }

    /// <summary>COMメソッドを遅延バインディングで呼び出す</summary>
    private static T Invoke<T>(object target, string methodName, params object?[] args)
    {
        var result = target.GetType().InvokeMember(
            methodName, BindingFlags.InvokeMethod, binder: null, target: target, args: args);

        return result is T typed ? typed : default!;
    }

    /// <summary>COMプロパティを遅延バインディングで設定する</summary>
    private static void SetProperty(object target, string propertyName, object value)
        => target.GetType().InvokeMember(
            propertyName, BindingFlags.SetProperty, binder: null, target: target, args: [value]);

    /// <summary>画像を一時ファイルへ書き出す</summary>
    private static string WriteTempImage(byte[] content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vms-label-{Guid.NewGuid():N}.{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>一時ファイルを削除する（失敗しても印刷結果には影響しないため無視する）</summary>
    private static void DeleteTempFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
