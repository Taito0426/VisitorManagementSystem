using Microsoft.AspNetCore.Authentication.Cookies;
using VisitorManagement.Composition;
using VisitorManagement.WebReception.Endpoints;
using VisitorManagement.WebReception.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    // 導入先ごとの上書き設定（接続文字列等）。リポジトリには含めない
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("VISITORMGMT_");

// 設定ファイル中の DPAPI 暗号化値（"ENC:" 接頭辞）を復号する（設計書 11.3節）。
// クラウド版では Azure Key Vault 参照／環境変数を使用するため、暗号化値がなければ何も行わない。
DpapiSecretProtector.UnprotectConfiguration(builder.Configuration);

// DB方式・メール方式・印刷方式の切替を含む全サービスを登録する
// （設計書 4.4節：WinForms と同一の登録ロジックを使用する）
builder.Services.AddVisitorManagement(builder.Configuration);

builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

// 受付端末の操作ユーザー認証（Operators テーブル。設計書 11.1節）
builder.Services
    .AddAuthentication(OperatorPrincipal.AuthenticationScheme)
    .AddCookie(OperatorPrincipal.AuthenticationScheme, options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.Name = "vms.reception";
        options.Cookie.HttpOnly = true;
        // CSRF対策として同一サイトからの送信のみ許可する
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// デモ版（Demo:Enabled=true）は初回起動時にスキーマ作成とサンプルデータ投入を自動実行する（設計書 19.2節）
await app.Services.InitializeDemoAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // 設計書 11.2節：通信は全区間 HTTPS/暗号化を必須とする
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// QRコード読取API（設計書 8.3節：読取結果は即座に /api/access/scan へ送信する）
app.MapAccessApi();

app.Run();
