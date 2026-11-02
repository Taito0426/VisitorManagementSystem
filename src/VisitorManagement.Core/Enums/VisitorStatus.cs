namespace VisitorManagement.Core.Enums;

/// <summary>
/// 来館予約のステータス（設計書 7.1節 / Visitors.Status）。
/// </summary>
public enum VisitorStatus : byte
{
    /// <summary>未入館（予約登録直後の初期状態）</summary>
    NotArrived = 0,

    /// <summary>入館中（QR初回読取により遷移）</summary>
    CheckedIn = 1,

    /// <summary>退館済（QR2回目読取により遷移。最終状態）</summary>
    CheckedOut = 2,

    /// <summary>キャンセル（未入館・入館中からのみ遷移可能）</summary>
    Cancelled = 9,
}
