namespace VisitorManagement.Core.Enums;

/// <summary>
/// 予約の種別（設計書 6.6節 / Visitors.ReservationType）。
/// </summary>
public enum ReservationType : byte
{
    /// <summary>事前予約（WinForms管理画面から登録）</summary>
    Advance = 1,

    /// <summary>当日受付（予約なし来館者を受付Web画面から登録）</summary>
    WalkIn = 2,
}
