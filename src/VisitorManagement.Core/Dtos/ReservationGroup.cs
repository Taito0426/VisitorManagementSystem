using VisitorManagement.Core.Entities;

namespace VisitorManagement.Core.Dtos;

/// <summary>
/// 予約グループ（代表者1名＋同行者0名以上）とそのQRトークンをまとめて扱うDTO。
/// QRトークンは代表者行にのみ発行される（設計書 6.2節）。
/// </summary>
public sealed class ReservationGroup
{
    /// <summary>グループID</summary>
    public Guid ReservationGroupId { get; set; }

    /// <summary>代表者（IsRepresentative=true）</summary>
    public Visitor Representative { get; set; } = null!;

    /// <summary>同行者（0名以上）</summary>
    public IReadOnlyList<Visitor> Companions { get; set; } = [];

    /// <summary>代表者に紐づくQRトークン（未発行の場合は null）</summary>
    public QrToken? Token { get; set; }

    /// <summary>
    /// 予約登録者（照会用の非永続項目）。
    /// 予約登録者宛メールの宛先・文面の組み立てに使用する（取得できない場合は null）。
    /// </summary>
    public Operator? RegisteredBy { get; set; }

    /// <summary>代表者＋同行者の全員（代表者を先頭とする）</summary>
    public IEnumerable<Visitor> AllMembers
    {
        get
        {
            yield return Representative;
            foreach (var companion in Companions)
            {
                yield return companion;
            }
        }
    }
}
