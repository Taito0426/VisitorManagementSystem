using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VisitorManagement.WebReception.Pages;

/// <summary>
/// ルートへのアクセスを受付画面へ振り分ける。
/// </summary>
public sealed class IndexModel : PageModel
{
    /// <summary>受付画面へリダイレクトする</summary>
    public IActionResult OnGet() => RedirectToPage("/Reception");
}
