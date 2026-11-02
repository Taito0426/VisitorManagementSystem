using PdfSharp.Fonts;

namespace VisitorManagement.Infrastructure.Printing;

/// <summary>
/// デモ版のPDF生成（設計書 19.4節）で使用する日本語フォントの解決。
/// PDFsharp のコアパッケージはシステムフォントを自動解決しないため、
/// Windows 標準搭載の日本語フォントから利用可能なものを1つ選んで使用する。
/// </summary>
public sealed class JapaneseFontResolver : IFontResolver
{
    /// <summary>解決済みフォントの内部識別名</summary>
    private const string FaceName = "VisitorManagement#Japanese";

    /// <summary>
    /// 探索するフォントファイルの候補（優先順）。
    /// TrueType Collection(.ttc) よりも単一フェイスの .ttf を優先する。
    /// </summary>
    private static readonly string[] CandidateFileNames =
    [
        "NotoSansJP-VF.ttf",    // Noto Sans JP（Windows 11 標準）
        "BIZ-UDGothicR.ttc",    // BIZ UDゴシック
        "YuGothR.ttc",          // 游ゴシック
        "meiryo.ttc",           // メイリオ
        "msgothic.ttc",         // MS ゴシック
    ];

    private static readonly Lock Gate = new();
    private static byte[]? _fontData;
    private static bool _probed;

    /// <inheritdoc />
    public byte[]? GetFont(string faceName) => LoadFontData();

    /// <inheritdoc />
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => LoadFontData() is null ? null : new FontResolverInfo(FaceName, isBold, isItalic);

    /// <summary>利用可能な日本語フォントを1度だけ探索して読み込む</summary>
    private static byte[]? LoadFontData()
    {
        lock (Gate)
        {
            if (_probed)
            {
                return _fontData;
            }

            _probed = true;
            var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

            foreach (var fileName in CandidateFileNames)
            {
                var path = Path.Combine(fontsDirectory, fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    _fontData = File.ReadAllBytes(path);
                    return _fontData;
                }
                catch (IOException)
                {
                    // 読み取りに失敗した場合は次の候補を試す
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return _fontData;
        }
    }
}
