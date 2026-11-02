using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VisitorManagement.WebReception.Pages;

/// <summary>
/// 未処理例外の表示ページ。技術的な詳細は表示せず、参照IDのみを示す。
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    /// <summary>ログと突き合わせるための参照ID</summary>
    public string? RequestId { get; private set; }

    /// <summary>参照IDを設定する</summary>
    public void OnGet() => RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
