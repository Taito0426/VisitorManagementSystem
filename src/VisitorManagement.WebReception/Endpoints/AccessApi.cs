using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;
using VisitorManagement.WebReception.Security;

namespace VisitorManagement.WebReception.Endpoints;

/// <summary>
/// 入退館受付API（設計書 8.3節）。
/// QRリーダー（キーボードエミュレーション方式）で読み取った値を受け取り、
/// グループ単位で入退館受付を行う。
/// </summary>
public static class AccessApi
{
    /// <summary>APIエンドポイントを登録する</summary>
    public static void MapAccessApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/access/scan", ScanAsync)
            .RequireAuthorization()
            .WithName("AccessScan");
    }

    /// <summary>QRコードを読み取り、入退館受付を行う</summary>
    private static async Task<IResult> ScanAsync(
        [FromBody] ScanRequest request,
        HttpContext httpContext,
        AccessService accessService,
        IAntiforgery antiforgery)
    {
        // JSON POST は自動的な検証対象外のため、明示的にCSRFトークンを検証する
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new ScanResponse
            {
                IsSuccess = false,
                ResultType = nameof(AccessType.Error),
                Message = "画面の有効期限が切れています。ページを再読み込みしてください。",
            });
        }

        var ctx = OperatorPrincipal.ToOperatorContext(httpContext.User);

        // 入退館受付は管理者・受付ロールのみ利用可能（設計書 11.6節）
        if (!PermissionPolicy.CanUseReception(ctx))
        {
            return Results.Json(new ScanResponse
            {
                IsSuccess = false,
                ResultType = nameof(AccessType.Error),
                Message = "入退館受付を行う権限がありません。",
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!Guid.TryParse(request.TokenValue?.Trim(), out var tokenValue))
        {
            // QRリーダーの読取ミス・別形式のコードを読み取った場合
            return Results.Ok(new ScanResponse
            {
                IsSuccess = false,
                ResultType = nameof(AccessType.Error),
                Message = "QRコードを読み取れませんでした。受付担当者にお申し出ください。",
            });
        }

        var deviceInfo = string.IsNullOrWhiteSpace(request.DeviceInfo)
            ? $"受付Web ({ctx.LoginId})"
            : request.DeviceInfo.Trim();

        // 端末情報の桁あふれ（AccessLogs.DeviceInfo は nvarchar(100)）は AccessService 側で丸める
        var result = await accessService.ScanAsync(tokenValue, deviceInfo).ConfigureAwait(false);

        return Results.Ok(ToResponse(result));
    }

    /// <summary>読取結果をAPIレスポンスへ変換する</summary>
    private static ScanResponse ToResponse(ScanResult result) => new()
    {
        IsSuccess = result.IsSuccess,
        ResultType = result.ResultType.ToString(),
        // 読取結果表示は日本語のみとする（設計書 8.3節・16章 確認事項No.6）
        ResultTypeText = HistoryService.ToText(result.ResultType),
        Message = result.Message,
        SiteName = result.SiteName,
        Members = [.. result.Members.Select(m => new ScanResponseMember
        {
            VisitorName = m.VisitorName,
            CompanyName = m.CompanyName,
            RoleText = m.IsRepresentative ? "代表者" : "同行者",
            StatusText = HistoryService.ToText(m.Status),
        })],
    };
}

/// <summary>
/// QRコード読取リクエスト。
/// </summary>
public sealed class ScanRequest
{
    /// <summary>QRコードに埋め込まれた値（GUID文字列）</summary>
    public string? TokenValue { get; set; }

    /// <summary>受付端末の識別情報</summary>
    public string? DeviceInfo { get; set; }
}

/// <summary>
/// QRコード読取レスポンス。受付画面はグループ全員分をまとめて表示する。
/// </summary>
public sealed class ScanResponse
{
    /// <summary>入退館処理が行われたか</summary>
    public bool IsSuccess { get; set; }

    /// <summary>処理区分（CheckIn / CheckOut / Error）</summary>
    public string ResultType { get; set; } = string.Empty;

    /// <summary>処理区分の表示名（入館 / 退館 / エラー）</summary>
    public string ResultTypeText { get; set; } = string.Empty;

    /// <summary>画面表示用メッセージ</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>来館先拠点名</summary>
    public string? SiteName { get; set; }

    /// <summary>グループ全員の情報</summary>
    public IReadOnlyList<ScanResponseMember> Members { get; set; } = [];
}

/// <summary>
/// 読取結果に表示するグループ内の来館者1名分。
/// </summary>
public sealed class ScanResponseMember
{
    /// <summary>氏名</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>会社名</summary>
    public string? CompanyName { get; set; }

    /// <summary>代表者／同行者</summary>
    public string RoleText { get; set; } = string.Empty;

    /// <summary>処理後のステータス</summary>
    public string StatusText { get; set; } = string.Empty;
}
