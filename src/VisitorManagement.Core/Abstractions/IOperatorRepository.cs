using VisitorManagement.Core.Entities;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// 操作ユーザーのRepository契約（設計書 6.10節・9.2.2節）。
/// </summary>
public interface IOperatorRepository
{
    /// <summary>操作ユーザーを1件取得する（該当なしの場合は null）</summary>
    Task<Operator?> GetByIdAsync(int operatorId, IDbSession? session = null);

    /// <summary>ログインIDから操作ユーザーを取得する（該当なしの場合は null）</summary>
    Task<Operator?> GetByLoginIdAsync(string loginId, IDbSession? session = null);

    /// <summary>操作ユーザーの一覧を取得する</summary>
    /// <param name="activeOnly">true のとき有効なアカウント（IsActive=1）のみ</param>
    Task<IReadOnlyList<Operator>> GetAllAsync(bool activeOnly = false, IDbSession? session = null);

    /// <summary>操作ユーザーを登録し、OperatorId を返す</summary>
    Task<int> CreateAsync(Operator op, IDbSession? session = null);

    /// <summary>操作ユーザーを更新する（PasswordHash は含まない）</summary>
    Task UpdateAsync(Operator op, IDbSession? session = null);

    /// <summary>パスワードハッシュを更新する</summary>
    Task UpdatePasswordHashAsync(int operatorId, string passwordHash, IDbSession? session = null);

    /// <summary>ログインIDが既に使用されているかを判定する</summary>
    /// <param name="excludeOperatorId">編集時に自身を除外する場合に指定</param>
    Task<bool> ExistsLoginIdAsync(string loginId, int? excludeOperatorId = null, IDbSession? session = null);
}
