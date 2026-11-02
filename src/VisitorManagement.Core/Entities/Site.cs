using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Entities;

/// <summary>
/// 拠点マスタ（設計書 9.2.1節 Sites）。
/// Standard/Professionalプランで複数拠点を管理する。Entryプランは1件のみ保持する。
/// </summary>
public sealed class Site
{
    /// <summary>主キー（IDENTITY）</summary>
    public int SiteId { get; set; }

    /// <summary>拠点名</summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>住所</summary>
    public string? Address { get; set; }

    /// <summary>当日受付ラベル印刷用の表示社名</summary>
    public string? LabelCompanyName { get; set; }

    /// <summary>当日受付ラベル印刷用のロゴ画像</summary>
    public byte[]? LabelLogoImage { get; set; }

    /// <summary>当日受付ラベルのプリセットサイズ（既定：1＝幅60mm×高さ110mm）</summary>
    public LabelSizeType LabelSizeType { get; set; } = Enums.LabelSizeType.Preset1;

    /// <summary>false のとき廃止された拠点</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>登録日時（UTC）</summary>
    public DateTime CreatedAt { get; set; }
}
