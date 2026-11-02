using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Application.Exceptions;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;
using VisitorManagement.WebReception.Resources;
using VisitorManagement.WebReception.Security;

namespace VisitorManagement.WebReception.Pages;

/// <summary>
/// 当日受付登録画面（設計書 8.1節 画面No.9、6.6節）。
///
/// 事前予約と同じ登録・QR発行・入退館受付のロジックをそのまま再利用し、特別な例外フローを設けない。
/// 代表者1名＋複数名の同行者をその場でまとめて登録でき（グループ登録）、メールアドレスは任意項目とする。
/// 登録と同時にQRトークンを発行し、感熱式ラベルプリンタへQRコードラベルを印刷する。
/// 画面表記・入力エラーメッセージは日英併記とする（設計書 6.12節）。
/// </summary>
[Authorize]
public sealed class WalkInModel(
    ReservationService reservationService,
    MasterDataService masterDataService,
    ILabelPrinter labelPrinter,
    ILabelPdfPreview? labelPdfPreview,
    ILogger<WalkInModel> logger) : PageModel
{
    /// <summary>入力内容</summary>
    [BindProperty]
    public WalkInInput Input { get; set; } = new();

    /// <summary>選択可能な拠点</summary>
    public IReadOnlyList<Site> Sites { get; private set; } = [];

    /// <summary>拠点選択UIを表示するか（Entryプラン＝1拠点構成では非表示。設計書 6.11節）</summary>
    public bool ShowSiteSelector { get; private set; }

    /// <summary>当日受付登録を行う権限があるか</summary>
    public bool CanRegister { get; private set; }

    /// <summary>入力エラーメッセージ（日英併記）</summary>
    public List<string> ErrorMessages { get; } = [];

    /// <summary>登録完了時の結果</summary>
    public WalkInResult? Result { get; private set; }

    /// <summary>画面表示時の初期化</summary>
    public async Task OnGetAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
    }

    /// <summary>当日受付を登録する</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await InitializeAsync().ConfigureAwait(false);

        if (!CanRegister)
        {
            return Page();
        }

        if (!ValidateInput())
        {
            return Page();
        }

        var ctx = OperatorPrincipal.ToOperatorContext(User);
        var now = DateTime.Now;

        var input = new ReservationInput
        {
            SiteId = Input.SiteId,
            // 当日受付は受付時点を基準時刻とし、QRの有効期限もこの時刻から算出される（設計書 6.5節）
            VisitDate = DateOnly.FromDateTime(now),
            VisitTimeFrom = TimeOnly.FromDateTime(now),
            HostEmployeeName = Input.HostEmployeeName,
            PurposeOfVisit = Input.PurposeOfVisit,
            ReservationType = ReservationType.WalkIn,
            Representative = new ReservationMemberInput
            {
                CompanyName = Input.CompanyName,
                // 未入力は ValidateInput で弾いているため、ここでは非 null として扱う
                VisitorName = Input.VisitorName ?? string.Empty,
                VisitorNameKana = Input.VisitorNameKana,
                VisitorEmail = Input.Email,
                PhoneNumber = Input.PhoneNumber,
            },
            Companions = [.. Input.Companions
                .Where(c => !string.IsNullOrWhiteSpace(c.VisitorName))
                .Select(c => new ReservationMemberInput
                {
                    CompanyName = string.IsNullOrWhiteSpace(c.CompanyName) ? Input.CompanyName : c.CompanyName,
                    VisitorName = c.VisitorName!,
                })],
        };

        ReservationResult reservation;
        try
        {
            reservation = await reservationService.CreateAsync(input, ctx).ConfigureAwait(false);
        }
        catch (BusinessRuleException ex)
        {
            ErrorMessages.Add(ex.Message);
            return Page();
        }
        catch (PermissionDeniedException ex)
        {
            ErrorMessages.Add(ex.Message);
            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "当日受付の登録に失敗しました。");
            ErrorMessages.Add(ReceptionTexts.ErrorGeneric);
            return Page();
        }

        // 登録と同時にQRコードラベルを印刷する（設計書 6.6節）
        var (printed, printMessage) = await PrintLabelAsync(reservation).ConfigureAwait(false);

        Result = new WalkInResult
        {
            ReservationGroupId = reservation.Group.ReservationGroupId,
            RepresentativeName = reservation.Group.Representative.VisitorName,
            CompanionNames = [.. reservation.Group.Companions.Select(c => c.VisitorName)],
            QrCodeBase64 = Convert.ToBase64String(reservation.QrCodePng),
            LabelPrinted = printed,
            LabelMessage = printMessage,
            MailSent = reservation.MailResults.Any(r => r.IsSuccess),
            // テストモード（デモ版・Debugビルド）では実送信していない旨を表示する
            MailTestMode = reservation.MailResults.Any(r => r.IsTest),
            // デモ版ではラベルの代わりに A4 PDF プレビューを提供する（設計書 19.4節）
            PdfPreviewAvailable = labelPdfPreview is not null,
            // 当日受付は登録した時点で入館登録まで完了している（発行時刻＝入館時刻）
            CheckedInAt = reservation.Group.Token?.IssuedAt,
        };

        // 登録完了後は入力欄をクリアし、次の来館者をすぐ受け付けられるようにする
        Input = new WalkInInput { SiteId = input.SiteId };

        return Page();
    }

    /// <summary>
    /// デモ版のラベル・メールレイアウトをA4 PDFとして取得する（設計書 19.4節）。
    /// </summary>
    public async Task<IActionResult> OnGetLabelPdfAsync(Guid reservationGroupId)
    {
        if (labelPdfPreview is null)
        {
            return NotFound();
        }

        var ctx = OperatorPrincipal.ToOperatorContext(User);
        if (!PermissionPolicy.CanUseReception(ctx))
        {
            return Forbid();
        }

        var group = await reservationService.GetGroupAsync(reservationGroupId).ConfigureAwait(false);
        if (group?.Token is null)
        {
            return NotFound();
        }

        var site = await masterDataService.GetSiteAsync(group.Representative.SiteId).ConfigureAwait(false);
        if (site is null)
        {
            return NotFound();
        }

        var qrCodePng = reservationService.GenerateQrCodePng(group.Token);
        var pdf = await labelPdfPreview
            .GeneratePreviewPdfAsync(group, qrCodePng, site).ConfigureAwait(false);

        return File(pdf, "application/pdf", $"label-preview-{reservationGroupId:N}.pdf");
    }

    /// <summary>拠点一覧など画面の前提データを読み込む</summary>
    private async Task InitializeAsync()
    {
        var ctx = OperatorPrincipal.ToOperatorContext(User);
        CanRegister = PermissionPolicy.CanUseReception(ctx);

        Sites = await masterDataService.GetSitesAsync().ConfigureAwait(false);
        ShowSiteSelector = Sites.Count > 1 && !ctx.IsReception;

        if (Input.SiteId <= 0)
        {
            // 受付ロールは自身の所属拠点を既定とする
            Input.SiteId = ctx.SiteId > 0 ? ctx.SiteId : Sites.FirstOrDefault()?.SiteId ?? 0;
        }

        // 受付ロールは自拠点に固定する（他拠点の当日受付は行わない）
        if (ctx.IsReception)
        {
            Input.SiteId = ctx.SiteId;
        }
    }

    /// <summary>入力値を検証する（エラーメッセージは日英併記。設計書 2.5節）</summary>
    private bool ValidateInput()
    {
        if (Input.SiteId <= 0)
        {
            ErrorMessages.Add(ReceptionTexts.ErrorSiteRequired);
        }
        if (string.IsNullOrWhiteSpace(Input.VisitorName))
        {
            ErrorMessages.Add(ReceptionTexts.ErrorVisitorNameRequired);
        }
        if (!string.IsNullOrWhiteSpace(Input.Email) && !Input.Email.Contains('@', StringComparison.Ordinal))
        {
            ErrorMessages.Add(ReceptionTexts.ErrorEmailFormat);
        }

        // 同行者は氏名の入力があるものだけを対象とするため、
        // 会社名や電話番号のみ入力された行は入力漏れとして扱う
        if (Input.Companions.Any(c =>
                string.IsNullOrWhiteSpace(c.VisitorName) && !string.IsNullOrWhiteSpace(c.CompanyName)))
        {
            ErrorMessages.Add(ReceptionTexts.ErrorCompanionNameRequired);
        }

        return ErrorMessages.Count == 0;
    }

    /// <summary>QRコードラベルを印刷する。失敗しても登録自体は完了扱いとする</summary>
    private async Task<(bool Printed, string Message)> PrintLabelAsync(ReservationResult reservation)
    {
        var site = await masterDataService
            .GetSiteAsync(reservation.Group.Representative.SiteId).ConfigureAwait(false);

        if (site is null)
        {
            return (false, ReceptionTexts.LabelPrintFailed);
        }

        try
        {
            await labelPrinter.PrintQrLabelAsync(
                reservation.Group, reservation.QrCodePng, site).ConfigureAwait(false);

            return (true, ReceptionTexts.LabelPrintSucceeded);
        }
        catch (Exception ex)
        {
            // プリンタ未接続・b-PAC SDK 未インストール等でも受付自体は成立させる
            logger.LogError(ex, "QRコードラベルの印刷に失敗しました。");
            return (false, ReceptionTexts.LabelPrintFailed);
        }
    }
}

/// <summary>
/// 当日受付登録画面の入力内容。
/// </summary>
public sealed class WalkInInput
{
    /// <summary>来館先拠点</summary>
    public int SiteId { get; set; }

    /// <summary>会社名</summary>
    public string? CompanyName { get; set; }

    /// <summary>代表者氏名</summary>
    public string? VisitorName { get; set; }

    /// <summary>フリガナ</summary>
    public string? VisitorNameKana { get; set; }

    /// <summary>メールアドレス（任意項目。設計書 6.6節）</summary>
    public string? Email { get; set; }

    /// <summary>電話番号</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>訪問先担当者</summary>
    public string? HostEmployeeName { get; set; }

    /// <summary>来館目的</summary>
    public string? PurposeOfVisit { get; set; }

    /// <summary>同行者（氏名が入力された行のみ登録対象）</summary>
    public List<WalkInCompanionInput> Companions { get; set; } = [new(), new(), new()];
}

/// <summary>
/// 当日受付登録画面の同行者1名分の入力。
/// </summary>
public sealed class WalkInCompanionInput
{
    /// <summary>会社名（未入力の場合は代表者の会社名を使用する）</summary>
    public string? CompanyName { get; set; }

    /// <summary>氏名</summary>
    public string? VisitorName { get; set; }
}

/// <summary>
/// 当日受付の登録結果。
/// </summary>
public sealed class WalkInResult
{
    /// <summary>登録された予約グループ</summary>
    public Guid ReservationGroupId { get; init; }

    /// <summary>代表者氏名</summary>
    public string RepresentativeName { get; init; } = string.Empty;

    /// <summary>同行者氏名の一覧</summary>
    public IReadOnlyList<string> CompanionNames { get; init; } = [];

    /// <summary>QRコード画像（Base64）</summary>
    public string QrCodeBase64 { get; init; } = string.Empty;

    /// <summary>ラベル印刷に成功したか</summary>
    public bool LabelPrinted { get; init; }

    /// <summary>ラベル印刷の結果メッセージ</summary>
    public string LabelMessage { get; init; } = string.Empty;

    /// <summary>来館案内メールを送信したか</summary>
    public bool MailSent { get; init; }

    /// <summary>テストモード（デモ版・Debugビルド）で、実際の外部送信を行っていないか</summary>
    public bool MailTestMode { get; init; }

    /// <summary>デモ版のPDFプレビューが利用可能か</summary>
    public bool PdfPreviewAvailable { get; init; }

    /// <summary>入館登録した日時（UTC）。当日受付は登録＝入館のため必ず設定される</summary>
    public DateTime? CheckedInAt { get; init; }
}
