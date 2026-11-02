namespace VisitorManagement.Core.Enums;

/// <summary>
/// 入退館履歴の区分（設計書 9.2.5節 / AccessLogs.AccessType）。
/// </summary>
public enum AccessType : byte
{
    /// <summary>入館</summary>
    CheckIn = 1,

    /// <summary>退館</summary>
    CheckOut = 2,

    /// <summary>エラー（期限切れ・無効トークン・3回目以降の読取など）</summary>
    Error = 9,
}
