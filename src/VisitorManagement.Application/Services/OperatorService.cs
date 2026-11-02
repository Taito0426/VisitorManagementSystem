using Microsoft.Extensions.Logging;
using VisitorManagement.Application.Exceptions;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Security;

namespace VisitorManagement.Application.Services;

/// <summary>
/// 操作ユーザー管理機能（設計書 6.10節・9.2.2節）。
/// メールアドレス・権限ロール・所属拠点はいずれも登録時必須項目であり、
/// 未設定状態を発生させない（設計書 17章 確認事項No.4）。
/// 拠点間の異動は本サービスから SiteId を変更して対応する（個別の拠点切替UIは設けない）。
/// </summary>
public sealed class OperatorService(
    IOperatorRepository operatorRepository,
    ISiteRepository siteRepository,
    AuditService auditService,
    ILogger<OperatorService> logger)
{
    /// <summary>操作ユーザーの一覧を取得する</summary>
    public Task<IReadOnlyList<Operator>> GetAllAsync(OperatorContext ctx)
    {
        RequirePermission(ctx);
        return operatorRepository.GetAllAsync(activeOnly: false);
    }

    /// <summary>操作ユーザーを1件取得する</summary>
    public Task<Operator?> GetAsync(int operatorId, OperatorContext ctx)
    {
        RequirePermission(ctx);
        return operatorRepository.GetByIdAsync(operatorId);
    }

    /// <summary>操作ユーザーを登録する</summary>
    public async Task<int> CreateAsync(Operator op, string password, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(op);
        RequirePermission(ctx);

        await ValidateAsync(op, password, isNew: true).ConfigureAwait(false);

        op.PasswordHash = PasswordHasher.Hash(password);
        var operatorId = await operatorRepository.CreateAsync(op).ConfigureAwait(false);
        op.OperatorId = operatorId;

        await auditService.RecordInsertAsync(AuditTargets.Operators, operatorId, ToAuditView(op), ctx)
            .ConfigureAwait(false);

        logger.LogInformation(
            "操作ユーザーを登録しました。OperatorId: {OperatorId} / ロール: {RoleType}", operatorId, op.RoleType);

        return operatorId;
    }

    /// <summary>操作ユーザーを更新する（パスワードは含まない）</summary>
    public async Task UpdateAsync(Operator op, OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(op);
        RequirePermission(ctx);

        var before = await operatorRepository.GetByIdAsync(op.OperatorId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の操作ユーザーが見つかりません。");

        await ValidateAsync(op, password: null, isNew: false).ConfigureAwait(false);

        // 最後の有効な管理者を無効化・降格できないようにする（管理者不在を防ぐ）
        if (before.RoleType == Core.Enums.RoleType.Administrator
            && (op.RoleType != Core.Enums.RoleType.Administrator || !op.IsActive)
            && !await HasOtherActiveAdministratorAsync(op.OperatorId).ConfigureAwait(false))
        {
            throw new BusinessRuleException("有効な管理者が不在となるため、この操作ユーザーの権限・状態は変更できません。");
        }

        await operatorRepository.UpdateAsync(op).ConfigureAwait(false);
        await auditService.RecordUpdateAsync(
            AuditTargets.Operators, op.OperatorId, ToAuditView(before), ToAuditView(op), ctx).ConfigureAwait(false);

        logger.LogInformation("操作ユーザーを更新しました。OperatorId: {OperatorId}", op.OperatorId);
    }

    /// <summary>パスワードを変更する</summary>
    public async Task ChangePasswordAsync(int operatorId, string newPassword, OperatorContext ctx)
    {
        RequirePermission(ctx);
        ValidatePassword(newPassword);

        var target = await operatorRepository.GetByIdAsync(operatorId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の操作ユーザーが見つかりません。");

        await operatorRepository
            .UpdatePasswordHashAsync(operatorId, PasswordHasher.Hash(newPassword)).ConfigureAwait(false);

        // パスワード自体は監査ログに残さない（変更が行われた事実のみ記録する）
        await auditService.RecordUpdateAsync(
            AuditTargets.Operators, operatorId,
            new { target.LoginId, PasswordChanged = false },
            new { target.LoginId, PasswordChanged = true },
            ctx).ConfigureAwait(false);

        logger.LogInformation("操作ユーザーのパスワードを変更しました。OperatorId: {OperatorId}", operatorId);
    }

    /// <summary>操作ユーザーを無効化する（削除は行わず IsActive=false とする）</summary>
    public async Task DeactivateAsync(int operatorId, OperatorContext ctx)
    {
        RequirePermission(ctx);

        var target = await operatorRepository.GetByIdAsync(operatorId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("対象の操作ユーザーが見つかりません。");

        if (operatorId == ctx.OperatorId)
        {
            throw new BusinessRuleException("ログイン中の操作ユーザー自身を無効化することはできません。");
        }

        target.IsActive = false;
        await UpdateAsync(target, ctx).ConfigureAwait(false);
    }

    /// <summary>入力値を検証する</summary>
    private async Task ValidateAsync(Operator op, string? password, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(op.LoginId))
        {
            throw new BusinessRuleException("ログインIDを入力してください。");
        }
        if (string.IsNullOrWhiteSpace(op.DisplayName))
        {
            throw new BusinessRuleException("表示名を入力してください。");
        }

        // 予約登録者宛メールの送信先となるため必須（設計書 6.10節）
        if (string.IsNullOrWhiteSpace(op.Email))
        {
            throw new BusinessRuleException("メールアドレスを入力してください。予約登録者宛メールの送信先として必須です。");
        }
        if (!op.Email.Contains('@', StringComparison.Ordinal))
        {
            throw new BusinessRuleException("メールアドレスの形式が正しくありません。");
        }

        // ロール・所属拠点は登録時必須（設計書 17章 確認事項No.4）
        if (!Enum.IsDefined(op.RoleType))
        {
            throw new BusinessRuleException("権限ロールを選択してください。");
        }
        if (op.SiteId <= 0)
        {
            throw new BusinessRuleException("所属拠点を選択してください。");
        }

        var site = await siteRepository.GetByIdAsync(op.SiteId).ConfigureAwait(false)
            ?? throw new BusinessRuleException("指定された所属拠点が存在しません。");

        if (!site.IsActive)
        {
            throw new BusinessRuleException("廃止された拠点は所属拠点として設定できません。");
        }

        var excludeId = isNew ? (int?)null : op.OperatorId;
        if (await operatorRepository.ExistsLoginIdAsync(op.LoginId, excludeId).ConfigureAwait(false))
        {
            throw new BusinessRuleException($"ログインID「{op.LoginId}」は既に使用されています。");
        }

        if (isNew)
        {
            ValidatePassword(password);
        }
    }

    /// <summary>パスワードの強度を検証する</summary>
    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new BusinessRuleException("パスワードは8文字以上で入力してください。");
        }
    }

    /// <summary>指定ユーザー以外に有効な管理者が存在するか</summary>
    private async Task<bool> HasOtherActiveAdministratorAsync(int excludeOperatorId)
    {
        var operators = await operatorRepository.GetAllAsync(activeOnly: true).ConfigureAwait(false);
        return operators.Any(o => o.OperatorId != excludeOperatorId && o.RoleType == Core.Enums.RoleType.Administrator);
    }

    /// <summary>操作ユーザー管理の権限を確認する</summary>
    private static void RequirePermission(OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!PermissionPolicy.CanManageOperators(ctx))
        {
            throw PermissionDeniedException.ForOperation("操作ユーザー管理");
        }
    }

    /// <summary>監査ログ記録用の表示（パスワードハッシュは記録しない）</summary>
    private static object ToAuditView(Operator op) => new
    {
        op.OperatorId,
        op.LoginId,
        op.DisplayName,
        op.Email,
        RoleType = (byte)op.RoleType,
        op.SiteId,
        op.IsActive,
    };
}
