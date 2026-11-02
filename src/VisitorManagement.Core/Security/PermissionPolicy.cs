using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;

namespace VisitorManagement.Core.Security;

/// <summary>
/// RBAC権限マトリクス（設計書 11.6節）を1か所に集約した判定ロジック。
/// 画面・API のいずれからも同一の判定を用いる。
///
/// 来館予約については、一般ロールも自分が来館者を招く立場となるため
/// 「登録・編集・キャンセル」を許可する。ただし対象は自分が登録した予約に限り、
/// 他人の予約は一覧にも表示されない（ResolveViewScope）。
/// 履歴を消す操作となる「削除」と、送信をやり直す「メール再送信」は
/// 従来どおり管理者・受付のみとする。
/// </summary>
public static class PermissionPolicy
{
    /// <summary>来館予約の登録（全ロール）</summary>
    public static bool CanCreateReservation(OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return true;
    }

    /// <summary>
    /// 来館予約の編集・キャンセル（全ロール）。
    /// 一般ロールは自分が登録した予約のみが対象のため、
    /// 個別の予約に対しては <see cref="CanOperateOnReservation"/> も併せて判定する。
    /// </summary>
    public static bool CanEditReservation(OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return true;
    }

    /// <summary>来館予約の削除（管理者／受付）</summary>
    public static bool CanDeleteReservation(OperatorContext ctx) =>
        ctx.RoleType is RoleType.Administrator or RoleType.Reception;

    /// <summary>
    /// 対象の来館予約を操作できるか。
    /// 一般ロールは自分が登録した予約のみを操作でき、他人の予約には一切手を加えられない。
    /// </summary>
    /// <param name="ctx">ログイン中の操作ユーザー</param>
    /// <param name="representative">対象予約の代表者行</param>
    public static bool CanOperateOnReservation(OperatorContext ctx, Visitor representative)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(representative);

        return !ctx.IsGeneral || representative.CreatedByOperatorId == ctx.OperatorId;
    }

    /// <summary>当日受付登録・入退館受付（管理者／受付）</summary>
    public static bool CanUseReception(OperatorContext ctx) =>
        ctx.RoleType is RoleType.Administrator or RoleType.Reception;

    /// <summary>手動退館処理（管理者／受付。設計書 6.4.1節）</summary>
    public static bool CanManualCheckOut(OperatorContext ctx) =>
        ctx.RoleType is RoleType.Administrator or RoleType.Reception;

    /// <summary>メール再送信（管理者／受付）</summary>
    public static bool CanResendMail(OperatorContext ctx) =>
        ctx.RoleType is RoleType.Administrator or RoleType.Reception;

    /// <summary>会社名マスタ管理（管理者のみ）</summary>
    public static bool CanManageCompanies(OperatorContext ctx) => ctx.IsAdministrator;

    /// <summary>拠点マスタ管理（管理者のみ）</summary>
    public static bool CanManageSites(OperatorContext ctx) => ctx.IsAdministrator;

    /// <summary>操作ユーザー管理（管理者のみ）</summary>
    public static bool CanManageOperators(OperatorContext ctx) => ctx.IsAdministrator;

    /// <summary>システム設定確認（管理者のみ）</summary>
    public static bool CanViewSystemSettings(OperatorContext ctx) => ctx.IsAdministrator;

    /// <summary>サンプルデータ初期化（管理者のみ。設計書 19.3節）</summary>
    public static bool CanResetDemoData(OperatorContext ctx) => ctx.IsAdministrator;

    /// <summary>
    /// 履歴照会・予約閲覧の範囲を検索条件に反映する（設計書 11.6節）。
    /// 管理者=全拠点・全件、受付=自身の所属拠点の全件、一般=自分が登録した予約のみ。
    /// </summary>
    /// <param name="ctx">ログイン中の操作ユーザー</param>
    /// <param name="requestedSiteId">画面で選択された拠点（未選択は null）</param>
    /// <returns>閲覧範囲を適用した (拠点ID, 登録者ID) の組</returns>
    public static (int? SiteId, int? CreatedByOperatorId) ResolveViewScope(OperatorContext ctx, int? requestedSiteId)
    {
        return ctx.RoleType switch
        {
            // 管理者は全拠点・全件。画面で拠点が選択されていればその絞り込みのみ適用する
            RoleType.Administrator => (requestedSiteId, null),

            // 受付は自身の所属拠点に固定する（他拠点は選択できない）
            RoleType.Reception => (ctx.SiteId, null),

            // 一般は自分が登録した予約のみ。拠点の絞り込みは併用可能
            _ => (requestedSiteId, ctx.OperatorId),
        };
    }

    /// <summary>来館予約の検索条件に閲覧範囲を適用する</summary>
    public static void ApplyViewScope(OperatorContext ctx, VisitorSearchCondition condition)
    {
        var (siteId, createdBy) = ResolveViewScope(ctx, condition.SiteId);
        condition.SiteId = siteId;
        condition.CreatedByOperatorId = createdBy;
    }

    /// <summary>入退館履歴の検索条件に閲覧範囲を適用する</summary>
    public static void ApplyViewScope(OperatorContext ctx, AccessLogSearchCondition condition)
    {
        var (siteId, createdBy) = ResolveViewScope(ctx, condition.SiteId);
        condition.SiteId = siteId;
        condition.CreatedByOperatorId = createdBy;
    }

    /// <summary>メール送信履歴の検索条件に閲覧範囲を適用する</summary>
    public static void ApplyViewScope(OperatorContext ctx, MailHistorySearchCondition condition)
    {
        var (siteId, createdBy) = ResolveViewScope(ctx, condition.SiteId);
        condition.SiteId = siteId;
        condition.CreatedByOperatorId = createdBy;
    }
}
