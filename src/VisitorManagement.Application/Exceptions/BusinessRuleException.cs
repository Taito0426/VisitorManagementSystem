namespace VisitorManagement.Application.Exceptions;

/// <summary>
/// 業務ルール違反（入力不備・状態不整合）を表す例外。
/// 画面側はメッセージをそのままユーザーへ提示できる。
/// </summary>
public sealed class BusinessRuleException(string message) : Exception(message);

/// <summary>
/// 権限不足（設計書 11.6節 RBAC）を表す例外。
/// </summary>
public sealed class PermissionDeniedException(string message) : Exception(message)
{
    /// <summary>操作名を添えた既定メッセージで生成する</summary>
    public static PermissionDeniedException ForOperation(string operationName)
        => new($"「{operationName}」を実行する権限がありません。");
}
