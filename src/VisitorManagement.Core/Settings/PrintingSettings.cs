namespace VisitorManagement.Core.Settings;

/// <summary>
/// Printing セクション（設計書 19.4節・付録B）。デモ版専用の切替項目。
/// 商用構成（Entry/Standard/Professional）では既定値 "Ql800" のまま使用する。
/// </summary>
public sealed class PrintingSettings
{
    /// <summary>設定セクション名</summary>
    public const string SectionName = "Printing";

    /// <summary>"Ql800" | "PdfPreview"</summary>
    public string Provider { get; set; } = PrintingProviderNames.Ql800;

    /// <summary>ラベルプリセット寸法（プリセット2・3は導入時ヒアリングにより個別設定する）</summary>
    public LabelPresetSettings LabelPresets { get; set; } = new();

    /// <summary>プリンタ名（b-PAC でプリンタを明示指定する場合。未設定時は既定プリンタ）</summary>
    public string? PrinterName { get; set; }

    /// <summary>b-PAC 用ラベルテンプレート（.lbx）のパス</summary>
    public string? LbxTemplatePath { get; set; }

    /// <summary>デモ版でPDFを出力するフォルダ（未設定時は一時フォルダ）</summary>
    public string? PdfOutputPath { get; set; }
}

/// <summary>
/// ラベルプリセット3種の寸法設定（設計書 9.2.1節 Sites.LabelSizeType）。
/// </summary>
public sealed class LabelPresetSettings
{
    /// <summary>プリセット1：標準搭載（幅60mm×高さ110mm）</summary>
    public LabelSizeSettings Preset1 { get; set; } = new() { WidthMm = 60, HeightMm = 110 };

    /// <summary>プリセット2：導入時ヒアリングにより個別設定（既定は未定義）</summary>
    public LabelSizeSettings Preset2 { get; set; } = new();

    /// <summary>プリセット3：導入時ヒアリングにより個別設定（既定は未定義）</summary>
    public LabelSizeSettings Preset3 { get; set; } = new();
}

/// <summary>
/// ラベル1種類分の寸法設定。
/// </summary>
public sealed class LabelSizeSettings
{
    /// <summary>ラベル幅（mm）。0 のとき未定義</summary>
    public double WidthMm { get; set; }

    /// <summary>ラベル高さ（mm）。0 のとき未定義</summary>
    public double HeightMm { get; set; }

    /// <summary>ラベル型番</summary>
    public string? LabelPartNumber { get; set; }
}

/// <summary>
/// ラベル印刷方式の識別子。
/// </summary>
public static class PrintingProviderNames
{
    /// <summary>ブラザー QL-800（b-PAC SDK経由で実機に印刷）</summary>
    public const string Ql800 = "Ql800";

    /// <summary>デモ版：A4 PDFとして生成しプレビュー表示</summary>
    public const string PdfPreview = "PdfPreview";
}
