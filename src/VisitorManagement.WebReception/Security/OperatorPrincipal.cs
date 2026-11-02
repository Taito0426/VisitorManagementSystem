using System.Security.Claims;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WebReception.Security;

/// <summary>
/// 受付Webの認証情報（Cookie）と <see cref="OperatorContext"/> の相互変換。
/// 認証は WinForms 管理アプリと同一の Operators テーブルで行う（設計書 11.1節）。
/// </summary>
public static class OperatorPrincipal
{
    /// <summary>認証スキーム名</summary>
    public const string AuthenticationScheme = "VisitorManagementReception";

    private const string ClaimOperatorId = "vms:operatorId";
    private const string ClaimSiteId = "vms:siteId";
    private const string ClaimSiteName = "vms:siteName";

    /// <summary>操作ユーザーから Cookie に格納する ClaimsPrincipal を生成する</summary>
    public static ClaimsPrincipal Create(Operator op)
    {
        ArgumentNullException.ThrowIfNull(op);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, op.LoginId),
            new(ClaimTypes.Name, op.DisplayName),
            new(ClaimTypes.Email, op.Email),
            new(ClaimTypes.Role, op.RoleType.ToString()),
            new(ClaimOperatorId, op.OperatorId.ToString()),
            new(ClaimSiteId, op.SiteId.ToString()),
            new(ClaimSiteName, op.SiteName ?? string.Empty),
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme));
    }

    /// <summary>
    /// Cookie の ClaimsPrincipal から <see cref="OperatorContext"/> を復元する。
    /// 認証済みでない場合は例外を送出する。
    /// </summary>
    public static OperatorContext ToOperatorContext(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("ログインしていません。");
        }

        return new OperatorContext(new Operator
        {
            OperatorId = int.Parse(principal.FindFirstValue(ClaimOperatorId) ?? "0"),
            LoginId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            DisplayName = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            Email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            RoleType = Enum.TryParse<RoleType>(principal.FindFirstValue(ClaimTypes.Role), out var role)
                ? role
                : RoleType.General,
            SiteId = int.Parse(principal.FindFirstValue(ClaimSiteId) ?? "0"),
            SiteName = principal.FindFirstValue(ClaimSiteName),
        });
    }
}
