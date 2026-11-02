using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Core.Security;
using VisitorManagement.WebReception.Security;

namespace VisitorManagement.WebReception.Pages;

/// <summary>
/// 受付画面（設計書 8.1節 画面No.8、8.3節）。
/// QRコードの読取と入退館受付の結果表示を行う。読取結果表示は日本語のみとする。
/// </summary>
[Authorize]
public sealed class ReceptionModel : PageModel
{
    /// <summary>入退館受付を行う権限があるか（管理者・受付ロールのみ。設計書 11.6節）</summary>
    public bool CanUseReception { get; private set; }

    /// <summary>
    /// AccessLogs.DeviceInfo に記録する受付端末の識別情報。
    /// 画面側で読取経路（スキャナー／カメラ）を末尾に付けて送信する。
    /// </summary>
    public string DeviceInfo { get; private set; } = string.Empty;

    /// <summary>画面表示時の初期化</summary>
    public void OnGet()
    {
        var ctx = OperatorPrincipal.ToOperatorContext(User);
        CanUseReception = PermissionPolicy.CanUseReception(ctx);

        // 拠点・操作ユーザー・接続元から端末を識別できる情報を組み立てる
        var remoteAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        DeviceInfo = $"受付Web {ctx.SiteName}／{ctx.LoginId}／{remoteAddress}";
    }
}
