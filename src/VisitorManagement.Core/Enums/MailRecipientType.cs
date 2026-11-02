namespace VisitorManagement.Core.Enums;

/// <summary>
/// メール宛先区分（設計書 5.2節 / MailHistory.RecipientType）。
/// </summary>
public enum MailRecipientType : byte
{
    /// <summary>招待者（代表者のみ）。送信先は Visitors.VisitorEmail</summary>
    Invitee = 1,

    /// <summary>予約登録者（操作ユーザー）。送信先は Operators.Email</summary>
    Registrant = 2,
}
