using VisitorManagement.Core.Entities;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// QRトークンのRepository契約（設計書 4.3.1節：発行・検証・使用回数更新）。
/// 1予約につき1行のみ、代表者の Visitors 行に紐づけて発行する。
/// </summary>
public interface IQrTokenRepository
{
    /// <summary>QRトークンを発行（登録）する</summary>
    Task<Guid> CreateAsync(QrToken token, IDbSession? session = null);

    /// <summary>QRコードに埋め込まれた値からトークンを取得する（該当なしの場合は null）</summary>
    Task<QrToken?> GetByTokenValueAsync(Guid tokenValue, IDbSession? session = null);

    /// <summary>代表者の VisitorId からトークンを取得する（該当なしの場合は null）</summary>
    Task<QrToken?> GetByVisitorIdAsync(Guid visitorId, IDbSession? session = null);

    /// <summary>読取回数（UsedCount）を更新する</summary>
    Task UpdateUsedCountAsync(Guid tokenId, byte usedCount, IDbSession? session = null);

    /// <summary>
    /// 読取回数が想定値のときに限り 1 加算する（同時読取に対する排他制御）。
    /// 別の受付端末が先に読み取っていた場合は false を返し、二重に入退館処理されるのを防ぐ。
    /// </summary>
    /// <param name="tokenId">対象トークン</param>
    /// <param name="expectedUsedCount">加算前に期待する読取回数（0 または 1）</param>
    /// <param name="session">読取処理と同一のトランザクション</param>
    /// <returns>加算できた場合 true</returns>
    Task<bool> TryConsumeAsync(Guid tokenId, byte expectedUsedCount, IDbSession session);

    /// <summary>有効期限を延長し、再発行回数（ReissueCount）を1加算する（設計書 5.3節）</summary>
    Task ExtendExpirationAsync(Guid tokenId, DateTime expiresAt, IDbSession? session = null);

    /// <summary>
    /// 有効期限のみを更新する（予約日時の変更に追随させる場合に使用）。
    /// 再発行ではないため ReissueCount は加算しない。
    /// </summary>
    Task UpdateExpirationAsync(Guid tokenId, DateTime expiresAt, IDbSession? session = null);

    /// <summary>再発行回数（ReissueCount）のみを1加算する（設計書 5.3節）</summary>
    Task IncrementReissueCountAsync(Guid tokenId, IDbSession? session = null);

    /// <summary>トークンを無効化する（キャンセル時。設計書 7.1節）</summary>
    Task InvalidateAsync(Guid tokenId, IDbSession? session = null);
}
