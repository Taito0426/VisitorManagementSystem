namespace VisitorManagement.Core.Entities;

/// <summary>
/// 会社名マスタ（設計書 9.2.7節 Companies）。来館予約入力を補助する。
/// </summary>
public sealed class Company
{
    /// <summary>主キー（IDENTITY）</summary>
    public int CompanyId { get; set; }

    /// <summary>会社名</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>住所</summary>
    public string? Address { get; set; }

    /// <summary>電話番号</summary>
    public string? Tel { get; set; }
}
