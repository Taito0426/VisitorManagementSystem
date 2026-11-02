using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Dtos;

/// <summary>
/// QRコード読取結果（設計書 6.4節・8.3節）。
/// 受付画面はグループ全員分の結果を一括表示する。
/// </summary>
public sealed class ScanResult
{
    /// <summary>読取に成功し入退館処理が行われた場合 true</summary>
    public bool IsSuccess { get; set; }

    /// <summary>実行された処理区分（成功時は入館／退館、失敗時はエラー）</summary>
    public AccessType ResultType { get; set; }

    /// <summary>画面表示用メッセージ（日本語）</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>グループ全員の氏名一覧（代表者を先頭とする）</summary>
    public IReadOnlyList<ScanResultMember> Members { get; set; } = [];

    /// <summary>来館先拠点名</summary>
    public string? SiteName { get; set; }
}

/// <summary>
/// 読取結果に表示するグループ内の来館者1名分の情報。
/// </summary>
public sealed class ScanResultMember
{
    /// <summary>来館者ID</summary>
    public Guid VisitorId { get; set; }

    /// <summary>氏名</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>会社名</summary>
    public string? CompanyName { get; set; }

    /// <summary>true:代表者 false:同行者</summary>
    public bool IsRepresentative { get; set; }

    /// <summary>処理後のステータス</summary>
    public VisitorStatus Status { get; set; }
}
