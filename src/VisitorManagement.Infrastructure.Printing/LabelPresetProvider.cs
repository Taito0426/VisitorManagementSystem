using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Printing;

/// <summary>
/// ラベルプリセット寸法の提供（設計書 9.2.1節・18章 確認事項No.2）。
/// プリセット1（幅60mm×高さ110mm）のみ標準搭載し、プリセット2・3は
/// 導入時ヒアリングにより appsettings.json の Printing:LabelPresets で個別設定する。
/// </summary>
public sealed class LabelPresetProvider(IOptions<PrintingSettings> printingOptions) : ILabelPresetProvider
{
    /// <inheritdoc />
    public LabelPreset GetPreset(LabelSizeType sizeType)
    {
        var presets = printingOptions.Value.LabelPresets;
        var settings = sizeType switch
        {
            LabelSizeType.Preset1 => presets.Preset1,
            LabelSizeType.Preset2 => presets.Preset2,
            LabelSizeType.Preset3 => presets.Preset3,
            _ => presets.Preset1,
        };

        return new LabelPreset(sizeType, settings.WidthMm, settings.HeightMm, settings.LabelPartNumber);
    }

    /// <inheritdoc />
    public IReadOnlyList<LabelPreset> GetAllPresets() =>
    [
        GetPreset(LabelSizeType.Preset1),
        GetPreset(LabelSizeType.Preset2),
        GetPreset(LabelSizeType.Preset3),
    ];
}
