using VisitorManagement.Core.Entities;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// 拠点マスタのRepository契約（設計書 6.11節・9.2.1節）。
/// </summary>
public interface ISiteRepository
{
    /// <summary>拠点を1件取得する（該当なしの場合は null）</summary>
    Task<Site?> GetByIdAsync(int siteId, IDbSession? session = null);

    /// <summary>拠点の一覧を取得する</summary>
    /// <param name="activeOnly">true のとき有効な拠点（IsActive=1）のみ</param>
    Task<IReadOnlyList<Site>> GetAllAsync(bool activeOnly = true, IDbSession? session = null);

    /// <summary>拠点を登録し、SiteId を返す</summary>
    Task<int> CreateAsync(Site site, IDbSession? session = null);

    /// <summary>拠点を更新する</summary>
    Task UpdateAsync(Site site, IDbSession? session = null);

    /// <summary>登録済みの拠点件数を取得する（Entryプランの拠点選択UI非表示判定に使用。設計書 6.11節）</summary>
    Task<int> CountAsync(IDbSession? session = null);
}
