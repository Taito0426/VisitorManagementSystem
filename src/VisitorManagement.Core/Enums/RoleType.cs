namespace VisitorManagement.Core.Enums;

/// <summary>
/// 操作ユーザーの権限ロール（設計書 6.10節・11.6節 / Operators.RoleType）。
/// </summary>
public enum RoleType : byte
{
    /// <summary>管理者：全機能・全拠点のデータを利用可能</summary>
    Administrator = 1,

    /// <summary>受付：入退館受付・当日受付・予約編集・手動退館処理。履歴は自拠点のみ</summary>
    Reception = 2,

    /// <summary>一般：閲覧のみ。履歴・予約は自分が登録した分のみ</summary>
    General = 3,
}
