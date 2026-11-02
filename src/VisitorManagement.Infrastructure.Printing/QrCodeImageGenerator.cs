using QRCoder;
using VisitorManagement.Core.Abstractions;

namespace VisitorManagement.Infrastructure.Printing;

/// <summary>
/// QRCoder による QRコード画像生成（設計書 6.2節・12.2節）。
/// QRコードには QRTokens.TokenValue（GUID）をそのまま埋め込む（設計書 11.4節）。
/// </summary>
public sealed class QrCodeImageGenerator : IQrCodeImageGenerator
{
    /// <summary>QRコードの前景色（黒）</summary>
    private static readonly byte[] DarkColorRgb = [0x00, 0x00, 0x00];

    /// <summary>QRコードの背景色（白）</summary>
    private static readonly byte[] LightColorRgb = [0xFF, 0xFF, 0xFF];

    /// <inheritdoc />
    public byte[] GeneratePng(Guid tokenValue, int pixelsPerModule = 10)
    {
        if (pixelsPerModule <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerModule), "1モジュールあたりのピクセル数は1以上で指定してください。");
        }

        using var generator = new QRCodeGenerator();
        // 誤り訂正レベル Q：ラベル印刷時の汚れ・かすれに対する耐性を確保する
        using var data = generator.CreateQrCode(tokenValue.ToString("D"), QRCodeGenerator.ECCLevel.Q);

        var pngQrCode = new PngByteQRCode(data);

        // 色を明示して 24bit トゥルーカラーPNGとして出力する。
        // 既定の 1bit グレースケールPNGは PDFsharp が読み込めず、
        // デモ版のPDFプレビュー（設計書 19.4節）で「Unsupported image format」となるため。
        return pngQrCode.GetGraphic(pixelsPerModule, DarkColorRgb, LightColorRgb);
    }
}
