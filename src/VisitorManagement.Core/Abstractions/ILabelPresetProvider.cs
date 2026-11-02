using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// ラベルプリセット寸法の提供元（設計書 9.2.1節・18章 確認事項No.2）。
/// プリセット1（幅60mm×高さ110mm）のみ標準搭載し、プリセット2・3は
/// 導入時ヒアリングにより appsettings.json で個別設定する。
/// </summary>
public interface ILabelPresetProvider
{
    /// <summary>指定プリセットの寸法を取得する</summary>
    LabelPreset GetPreset(LabelSizeType sizeType);

    /// <summary>全プリセットを取得する（未定義のものを含む）</summary>
    IReadOnlyList<LabelPreset> GetAllPresets();
}
