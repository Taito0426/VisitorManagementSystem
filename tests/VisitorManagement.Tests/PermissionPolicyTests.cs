using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.Tests;

/// <summary>
/// RBAC権限マトリクスのテスト（設計書 11.6節）。
/// </summary>
public sealed class PermissionPolicyTests
{
    private const int OwnSiteId = 1;
    private const int OtherSiteId = 2;
    private const int OperatorId = 10;

    private static OperatorContext Context(RoleType role) => new(new Operator
    {
        OperatorId = OperatorId,
        LoginId = "user",
        DisplayName = "テスト ユーザー",
        Email = "user@example.com",
        RoleType = role,
        SiteId = OwnSiteId,
    });

    [Theory]
    [InlineData(RoleType.Administrator)]
    [InlineData(RoleType.Reception)]
    [InlineData(RoleType.General)]
    public void 来館予約の登録と編集は全ロールで可能である(RoleType role)
    {
        var ctx = Context(role);

        Assert.True(PermissionPolicy.CanCreateReservation(ctx));
        Assert.True(PermissionPolicy.CanEditReservation(ctx));
    }

    [Theory]
    [InlineData(RoleType.Administrator, true)]
    [InlineData(RoleType.Reception, true)]
    [InlineData(RoleType.General, false)]
    public void 来館予約の削除は管理者と受付のみ可能である(RoleType role, bool expected)
        => Assert.Equal(expected, PermissionPolicy.CanDeleteReservation(Context(role)));

    [Theory]
    [InlineData(RoleType.Administrator, true)]
    [InlineData(RoleType.Reception, true)]
    [InlineData(RoleType.General, false)]
    public void 一般ロールは他人が登録した予約を操作できない(RoleType role, bool expected)
    {
        var othersReservation = new Visitor { CreatedByOperatorId = OperatorId + 1 };

        Assert.Equal(expected, PermissionPolicy.CanOperateOnReservation(Context(role), othersReservation));
    }

    [Theory]
    [InlineData(RoleType.Administrator)]
    [InlineData(RoleType.Reception)]
    [InlineData(RoleType.General)]
    public void 自分が登録した予約はどのロールでも操作できる(RoleType role)
    {
        var ownReservation = new Visitor { CreatedByOperatorId = OperatorId };

        Assert.True(PermissionPolicy.CanOperateOnReservation(Context(role), ownReservation));
    }

    [Theory]
    [InlineData(RoleType.Administrator, true)]
    [InlineData(RoleType.Reception, true)]
    [InlineData(RoleType.General, false)]
    public void 当日受付登録と入退館受付は管理者と受付のみ可能である(RoleType role, bool expected)
        => Assert.Equal(expected, PermissionPolicy.CanUseReception(Context(role)));

    [Theory]
    [InlineData(RoleType.Administrator, true)]
    [InlineData(RoleType.Reception, true)]
    [InlineData(RoleType.General, false)]
    public void 手動退館処理は管理者と受付のみ可能である(RoleType role, bool expected)
        => Assert.Equal(expected, PermissionPolicy.CanManualCheckOut(Context(role)));

    [Theory]
    [InlineData(RoleType.Administrator, true)]
    [InlineData(RoleType.Reception, true)]
    [InlineData(RoleType.General, false)]
    public void メール再送信は管理者と受付のみ可能である(RoleType role, bool expected)
        => Assert.Equal(expected, PermissionPolicy.CanResendMail(Context(role)));

    [Theory]
    [InlineData(RoleType.Administrator, true)]
    [InlineData(RoleType.Reception, false)]
    [InlineData(RoleType.General, false)]
    public void マスタ管理と操作ユーザー管理とシステム設定確認は管理者のみ可能である(RoleType role, bool expected)
    {
        var ctx = Context(role);

        Assert.Equal(expected, PermissionPolicy.CanManageCompanies(ctx));
        Assert.Equal(expected, PermissionPolicy.CanManageSites(ctx));
        Assert.Equal(expected, PermissionPolicy.CanManageOperators(ctx));
        Assert.Equal(expected, PermissionPolicy.CanViewSystemSettings(ctx));
        Assert.Equal(expected, PermissionPolicy.CanResetDemoData(ctx));
    }

    [Fact]
    public void 管理者は全拠点全件を閲覧できる()
    {
        var (siteId, createdBy) = PermissionPolicy.ResolveViewScope(Context(RoleType.Administrator), null);

        Assert.Null(siteId);
        Assert.Null(createdBy);
    }

    [Fact]
    public void 管理者は画面で選択した拠点に絞り込める()
    {
        var (siteId, createdBy) = PermissionPolicy.ResolveViewScope(Context(RoleType.Administrator), OtherSiteId);

        Assert.Equal(OtherSiteId, siteId);
        Assert.Null(createdBy);
    }

    [Fact]
    public void 受付は他拠点を指定しても自身の所属拠点に固定される()
    {
        var (siteId, createdBy) = PermissionPolicy.ResolveViewScope(Context(RoleType.Reception), OtherSiteId);

        Assert.Equal(OwnSiteId, siteId);
        Assert.Null(createdBy);
    }

    [Fact]
    public void 一般は自分が登録した予約のみ閲覧できる()
    {
        var (_, createdBy) = PermissionPolicy.ResolveViewScope(Context(RoleType.General), null);

        Assert.Equal(OperatorId, createdBy);
    }

    [Fact]
    public void 検索条件に閲覧範囲が適用される()
    {
        var condition = new VisitorSearchCondition { SiteId = OtherSiteId };

        PermissionPolicy.ApplyViewScope(Context(RoleType.Reception), condition);

        Assert.Equal(OwnSiteId, condition.SiteId);
        Assert.Null(condition.CreatedByOperatorId);
    }

    [Fact]
    public void 履歴検索条件にも閲覧範囲が適用される()
    {
        var accessCondition = new AccessLogSearchCondition();
        var mailCondition = new MailHistorySearchCondition();
        var ctx = Context(RoleType.General);

        PermissionPolicy.ApplyViewScope(ctx, accessCondition);
        PermissionPolicy.ApplyViewScope(ctx, mailCondition);

        Assert.Equal(OperatorId, accessCondition.CreatedByOperatorId);
        Assert.Equal(OperatorId, mailCondition.CreatedByOperatorId);
    }
}
