using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Entities;

/// <summary>
/// 入退館履歴（設計書 9.2.5節 AccessLogs）。
/// 代表者QRの1回の読取につき、グループ内の来館者ごとに1件ずつ記録する。
/// </summary>
public sealed class AccessLog
{
    /// <summary>主キー（IDENTITY）</summary>
    public long LogId { get; set; }

    /// <summary>外部キー→Visitors</summary>
    public Guid VisitorId { get; set; }

    /// <summary>受付を行った拠点（外部キー→Sites）</summary>
    public int SiteId { get; set; }

    /// <summary>外部キー→QRTokens（代表者のトークンを参照。手動受付時はNULL許容）</summary>
    public Guid? TokenId { get; set; }

    /// <summary>1:入館 2:退館 9:エラー</summary>
    public AccessType AccessType { get; set; }

    /// <summary>受付日時（UTC）</summary>
    public DateTime AccessDateTime { get; set; }

    /// <summary>受付端末識別情報</summary>
    public string? DeviceInfo { get; set; }

    /// <summary>手動受付時の備考等</summary>
    public string? OperatorNote { get; set; }

    /// <summary>来訪者氏名（照会用の非永続項目）</summary>
    public string? VisitorName { get; set; }

    /// <summary>来訪者所属会社名（照会用の非永続項目）</summary>
    public string? CompanyName { get; set; }

    /// <summary>拠点名（照会用の非永続項目）</summary>
    public string? SiteName { get; set; }
}
