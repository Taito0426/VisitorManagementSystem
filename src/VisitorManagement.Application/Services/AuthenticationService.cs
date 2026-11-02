using Microsoft.Extensions.Logging;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Security;

namespace VisitorManagement.Application.Services;

/// <summary>
/// 操作ユーザーのログイン認証（設計書 11.1節）。
/// LoginId と PasswordHash（ソルト付きPBKDF2）で照合する。
/// </summary>
public sealed class AuthenticationService(
    IOperatorRepository operatorRepository,
    ILogger<AuthenticationService> logger)
{
    /// <summary>
    /// ログインを試行する。認証に失敗した場合は null を返す。
    /// 「ログインIDが存在しない」「パスワードが誤り」「アカウントが無効」を
    /// 呼び出し側に区別させないことで、アカウント列挙を防ぐ。
    /// </summary>
    public async Task<OperatorContext?> LoginAsync(string loginId, string password)
    {
        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var op = await operatorRepository.GetByLoginIdAsync(loginId).ConfigureAwait(false);
        if (op is null)
        {
            // 存在しないIDでもハッシュ計算と同等の時間を消費させ、応答時間による推測を防ぐ
            PasswordHasher.Verify(password, DummyHash);
            logger.LogWarning("ログインに失敗しました（該当ユーザーなし）。LoginId: {LoginId}", loginId);
            return null;
        }

        if (!PasswordHasher.Verify(password, op.PasswordHash))
        {
            logger.LogWarning("ログインに失敗しました（パスワード不一致）。LoginId: {LoginId}", loginId);
            return null;
        }

        if (!op.IsActive)
        {
            logger.LogWarning("ログインに失敗しました（無効化されたアカウント）。LoginId: {LoginId}", loginId);
            return null;
        }

        logger.LogInformation("ログインしました。LoginId: {LoginId} / ロール: {RoleType}", op.LoginId, op.RoleType);
        return new OperatorContext(op);
    }

    /// <summary>存在しないユーザーに対する照合時間をそろえるためのダミーハッシュ</summary>
    private static readonly string DummyHash = PasswordHasher.Hash("dummy-password-for-timing-equalization");
}
