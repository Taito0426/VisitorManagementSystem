namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// QRコード画像の生成契約（設計書 6.2節・12.2節 QRCoder）。
/// </summary>
public interface IQrCodeImageGenerator
{
    /// <summary>QRトークン値からQRコード画像（PNG）を生成する</summary>
    /// <param name="tokenValue">QRコードに埋め込む値（GUID）</param>
    /// <param name="pixelsPerModule">1モジュールあたりのピクセル数</param>
    byte[] GeneratePng(Guid tokenValue, int pixelsPerModule = 10);
}
