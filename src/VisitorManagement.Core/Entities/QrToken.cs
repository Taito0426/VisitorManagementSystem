namespace VisitorManagement.Core.Entities;

/// <summary>
/// QRトークン（設計書 9.2.4節 QRTokens）。
/// 1予約（ReservationGroupId）につき1行のみ発行し、代表者の VisitorId に紐づける。
/// 同行者の Visitors 行に対応する QRTokens 行は作成しない。
/// </summary>
public sealed class QrToken
{
    /// <summary>主キー</summary>
    public Guid TokenId { get; set; }

    /// <summary>外部キー→Visitors（代表者行のみ）</summary>
    public Guid VisitorId { get; set; }

    /// <summary>QRコードに埋め込む値（一意制約あり）</summary>
    public Guid TokenValue { get; set; }

    /// <summary>発行日時（UTC）</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>有効期限（予約時刻-30分〜+2時間。当日受付は受付時刻基準）</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>読取回数（0/1/2）</summary>
    public byte UsedCount { get; set; }

    /// <summary>メール再送信による再発行回数（監査目的。読取ロジックには影響しない）</summary>
    public byte ReissueCount { get; set; }

    /// <summary>false のときキャンセル等により無効化</summary>
    public bool IsValid { get; set; } = true;
}
