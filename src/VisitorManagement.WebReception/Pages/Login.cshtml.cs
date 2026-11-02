using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.WebReception.Security;
// Microsoft.AspNetCore.Authentication にも AuthenticationService があるため、別名を付けて曖昧参照を避ける
using AuthenticationService = VisitorManagement.Application.Services.AuthenticationService;

namespace VisitorManagement.WebReception.Pages;

/// <summary>
/// 受付端末のログイン画面。
/// 認証は WinForms 管理アプリと同一の Operators テーブルで行う（設計書 11.1節）。
/// 本画面は受付担当者（社内利用者）向けのため日本語のみとする。
/// </summary>
public sealed class LoginModel(
    AuthenticationService authenticationService,
    IOperatorRepository operatorRepository) : PageModel
{
    /// <summary>入力されたログインID</summary>
    [BindProperty]
    public string LoginId { get; set; } = string.Empty;

    /// <summary>入力されたパスワード</summary>
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    /// <summary>エラーメッセージ</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>ログイン済みの場合は受付画面へ遷移する</summary>
    public IActionResult OnGet()
        => User.Identity?.IsAuthenticated == true ? RedirectToPage("/Reception") : Page();

    /// <summary>ログインを実行する</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginId) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "ログインIDとパスワードを入力してください。";
            return Page();
        }

        var context = await authenticationService.LoginAsync(LoginId.Trim(), Password).ConfigureAwait(false);
        if (context is null)
        {
            // 認証失敗の理由は区別して提示しない（アカウント列挙の防止）
            ErrorMessage = "ログインIDまたはパスワードが正しくありません。";
            return Page();
        }

        // Cookie に格納する Claims を組み立てるため、拠点名を含む操作ユーザーを取得する
        var op = await operatorRepository.GetByIdAsync(context.OperatorId).ConfigureAwait(false);
        if (op is null)
        {
            ErrorMessage = "操作ユーザーの情報を取得できませんでした。";
            return Page();
        }

        await HttpContext.SignInAsync(
            OperatorPrincipal.AuthenticationScheme,
            OperatorPrincipal.Create(op),
            new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);

        return RedirectToPage("/Reception");
    }
}

/// <summary>
/// ログアウト処理。
/// </summary>
public sealed class LogoutModel : PageModel
{
    /// <summary>ログアウトしてログイン画面へ戻る</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(OperatorPrincipal.AuthenticationScheme).ConfigureAwait(false);
        return RedirectToPage("/Login");
    }
}
