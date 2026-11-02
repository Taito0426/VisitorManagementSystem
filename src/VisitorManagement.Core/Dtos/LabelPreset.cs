using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Dtos;

/// <summary>
/// 当日受付ラベルのプリセット寸法（設計書 9.2.1節 Sites.LabelSizeType / 18章 確認事項No.2）。
/// プリセット1（幅60mm×高さ110mm）のみ標準搭載。プリセット2・3は導入時ヒアリングにより個別設定する
/// 空の設定項目として用意しており、既定では未定義（IsDefined=false）である。
/// </summary>
/// <param name="SizeType">プリセット番号</param>
/// <param name="WidthMm">ラベル幅（mm）。未定義の場合は 0</param>
/// <param name="HeightMm">ラベル高さ（mm）。未定義の場合は 0</param>
/// <param name="LabelPartNumber">ラベル型番。未定義の場合は null</param>
public readonly record struct LabelPreset(
    LabelSizeType SizeType,
    double WidthMm,
    double HeightMm,
    string? LabelPartNumber)
{
    /// <summary>寸法が設定済みかどうか</summary>
    public bool IsDefined => WidthMm > 0 && HeightMm > 0;
}
