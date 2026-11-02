using Microsoft.Extensions.Logging;
using VisitorManagement.Application.Exceptions;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Security;

namespace VisitorManagement.Application.Services;

/// <summary>
/// マスタ管理機能（設計書 6.9節・6.11節）。
/// 会社名マスタ・拠点マスタの登録・編集・削除を行い、変更内容を監査ログへ記録する。
/// いずれも管理者ロール専用（設計書 11.6節）。
/// </summary>
public sealed class MasterDataService(
    ICompanyRepository companyRepository,
    ISiteRepository siteRepository,
    AuditService auditService,
    ILogger<MasterDataService> logger)
{
    // --- 会社名マスタ（設計書 6.9節・9.2.7節） ---

    /// <summary>会社名マスタの一覧を取得する</summary>
    public Task<IReadOnlyList<Company>> GetCompaniesAsync() => companyRepository.GetAllAsync();

    /// <summary>会社を登録する</summary>
    public async Task<int> CreateCompanyAsync(Company company, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(company);
        RequireCompanyPermission(ctx);

        if (string.IsNullOrWhiteSpace(company.CompanyName))
        {
            throw new BusinessRuleException("会社名を入力してください。");
        }

        var companyId = await companyRepository.CreateAsync(company).ConfigureAwait(false);
        company.CompanyId = companyId;

        await auditService.RecordInsertAsync(AuditTargets.Companies, companyId, company, ctx).ConfigureAwait(false);
        logger.LogInformation("会社名マスタを登録しました。CompanyId: {CompanyId}", companyId);

        return companyId;
    }

    /// <summary>会社を更新する</summary>
    public async Task UpdateCompanyAsync(Company company, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(company);
        RequireCompanyPermission(ctx);

        if (string.IsNullOrWhiteSpace(company.CompanyName))
        {
            throw new BusinessRuleException("会社名を入力してください。");
        }

        var before = await companyRepository.GetByIdAsync(company.CompanyId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の会社が見つかりません。");

        await companyRepository.UpdateAsync(company).ConfigureAwait(false);
        await auditService.RecordUpdateAsync(AuditTargets.Companies, company.CompanyId, before, company, ctx)
            .ConfigureAwait(false);

        logger.LogInformation("会社名マスタを更新しました。CompanyId: {CompanyId}", company.CompanyId);
    }

    /// <summary>会社を削除する</summary>
    public async Task DeleteCompanyAsync(int companyId, OperatorContext ctx)
    {
        RequireCompanyPermission(ctx);

        var before = await companyRepository.GetByIdAsync(companyId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の会社が見つかりません。");

        await companyRepository.DeleteAsync(companyId).ConfigureAwait(false);
        await auditService.RecordDeleteAsync(AuditTargets.Companies, companyId, before, ctx).ConfigureAwait(false);

        logger.LogInformation("会社名マスタを削除しました。CompanyId: {CompanyId}", companyId);
    }

    // --- 拠点マスタ（設計書 6.11節・9.2.1節） ---

    /// <summary>拠点マスタの一覧を取得する</summary>
    public Task<IReadOnlyList<Site>> GetSitesAsync(bool activeOnly = true) => siteRepository.GetAllAsync(activeOnly);

    /// <summary>拠点を1件取得する</summary>
    public Task<Site?> GetSiteAsync(int siteId) => siteRepository.GetByIdAsync(siteId);

    /// <summary>
    /// 有効な拠点が1件のみか（Entryプランでは拠点選択UIを非表示にする。設計書 6.11節）。
    /// </summary>
    public async Task<bool> IsSingleSiteAsync() => await siteRepository.CountAsync().ConfigureAwait(false) <= 1;

    /// <summary>拠点を登録する</summary>
    public async Task<int> CreateSiteAsync(Site site, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(site);
        RequireSitePermission(ctx);
        ValidateSite(site);

        var siteId = await siteRepository.CreateAsync(site).ConfigureAwait(false);
        site.SiteId = siteId;

        // ロゴ画像は監査ログに含めない（サイズが大きく可読性を損なうため）
        await auditService.RecordInsertAsync(AuditTargets.Sites, siteId, ToAuditView(site), ctx).ConfigureAwait(false);
        logger.LogInformation("拠点マスタを登録しました。SiteId: {SiteId}", siteId);

        return siteId;
    }

    /// <summary>拠点を更新する</summary>
    public async Task UpdateSiteAsync(Site site, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(site);
        RequireSitePermission(ctx);
        ValidateSite(site);

        var before = await siteRepository.GetByIdAsync(site.SiteId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の拠点が見つかりません。");

        await siteRepository.UpdateAsync(site).ConfigureAwait(false);
        await auditService.RecordUpdateAsync(AuditTargets.Sites, site.SiteId, ToAuditView(before), ToAuditView(site), ctx)
            .ConfigureAwait(false);

        logger.LogInformation("拠点マスタを更新しました。SiteId: {SiteId}", site.SiteId);
    }

    /// <summary>拠点の入力値を検証する</summary>
    private static void ValidateSite(Site site)
    {
        if (string.IsNullOrWhiteSpace(site.SiteName))
        {
            throw new BusinessRuleException("拠点名を入力してください。");
        }
    }

    /// <summary>会社名マスタ管理の権限を確認する</summary>
    private static void RequireCompanyPermission(OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!PermissionPolicy.CanManageCompanies(ctx))
        {
            throw PermissionDeniedException.ForOperation("会社名マスタ管理");
        }
    }

    /// <summary>拠点マスタ管理の権限を確認する</summary>
    private static void RequireSitePermission(OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!PermissionPolicy.CanManageSites(ctx))
        {
            throw PermissionDeniedException.ForOperation("拠点マスタ管理");
        }
    }

    /// <summary>監査ログ記録用の表示（ロゴ画像はバイト数のみ記録する）</summary>
    private static object ToAuditView(Site site) => new
    {
        site.SiteId,
        site.SiteName,
        site.Address,
        site.LabelCompanyName,
        LabelLogoImageBytes = site.LabelLogoImage?.Length ?? 0,
        LabelSizeType = (byte)site.LabelSizeType,
        site.IsActive,
    };
}
