using VisitorManagement.Core.Entities;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// 会社名マスタのRepository契約（設計書 6.9節・9.2.7節）。
/// </summary>
public interface ICompanyRepository
{
    /// <summary>会社を1件取得する（該当なしの場合は null）</summary>
    Task<Company?> GetByIdAsync(int companyId, IDbSession? session = null);

    /// <summary>会社名マスタの一覧を取得する（会社名の昇順）</summary>
    Task<IReadOnlyList<Company>> GetAllAsync(IDbSession? session = null);

    /// <summary>会社を登録し、CompanyId を返す</summary>
    Task<int> CreateAsync(Company company, IDbSession? session = null);

    /// <summary>会社を更新する</summary>
    Task UpdateAsync(Company company, IDbSession? session = null);

    /// <summary>会社を削除する</summary>
    Task DeleteAsync(int companyId, IDbSession? session = null);
}
