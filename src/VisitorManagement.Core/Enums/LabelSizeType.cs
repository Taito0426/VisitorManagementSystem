namespace VisitorManagement.Core.Enums;

/// <summary>
/// 当日受付ラベルのプリセットサイズ（設計書 9.2.1節 / Sites.LabelSizeType）。
/// プリセット1のみ標準搭載。2・3は導入時ヒアリングにより個別設定する空の設定項目。
/// </summary>
public enum LabelSizeType : byte
{
    /// <summary>プリセット1：幅60mm×高さ110mm（既定値）</summary>
    Preset1 = 1,

    /// <summary>プリセット2：導入時ヒアリングにより個別設定（既定では未定義）</summary>
    Preset2 = 2,

    /// <summary>プリセット3：導入時ヒアリングにより個別設定（既定では未定義）</summary>
    Preset3 = 3,
}
