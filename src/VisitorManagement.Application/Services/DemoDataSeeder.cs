using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Application.Exceptions;
using VisitorManagement.Core;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Application.Services;

/// <summary>
/// デモ用サンプルデータの投入（設計書 19.3節）。
/// 初回起動時に自動投入し（冪等処理。既にデータが存在する場合は再投入しない）、
/// 管理者ロールからワンクリックでシード状態へリセットできる。
/// 本サービスは Demo:Enabled=true のときのみ意味を持ち、商用構成では使用しない。
/// </summary>
public sealed class DemoDataSeeder(
    IDbSessionFactory sessionFactory,
    ISiteRepository siteRepository,
    ICompanyRepository companyRepository,
    IOperatorRepository operatorRepository,
    IVisitorRepository visitorRepository,
    IQrTokenRepository qrTokenRepository,
    IAccessLogRepository accessLogRepository,
    IOptions<DemoSettings> demoOptions,
    ILogger<DemoDataSeeder> logger)
{
    /// <summary>
    /// デモ用ログインアカウントの初期パスワード。
    /// 設計書 19.3節により本書（設計書）には掲載せず、デモ操作手順書（docs/デモ操作手順書.md）に記載する。
    /// </summary>
    public const string DemoPassword = "Demo#2026";

    /// <summary>デモ用ログインID</summary>
    public const string AdminLoginId = "demo-admin";
    public const string ReceptionLoginId = "demo-reception";
    public const string GeneralLoginId = "demo-general";

    /// <summary>
    /// 初回起動時のシード投入（冪等）。既にデータが存在する場合は何もしない。
    /// </summary>
    public async Task SeedIfEmptyAsync()
    {
        if (!demoOptions.Value.Enabled || !demoOptions.Value.SeedOnFirstRun)
        {
            return;
        }

        var operators = await operatorRepository.GetAllAsync(activeOnly: false).ConfigureAwait(false);
        if (operators.Count > 0)
        {
            logger.LogInformation("デモ用サンプルデータは既に投入済みのため、シード処理をスキップします。");
            return;
        }

        await SeedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// サンプルデータを初期状態へリセットする（管理者ロール専用。設計書 19.3節）。
    /// 既存データをすべて削除したうえで再投入する。
    /// </summary>
    public async Task ResetAsync(OperatorContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!demoOptions.Value.Enabled)
        {
            throw new BusinessRuleException("デモモードが無効のため、サンプルデータの初期化は実行できません。");
        }
        if (!demoOptions.Value.AllowResetFromAdminMenu)
        {
            throw new BusinessRuleException("設定によりサンプルデータの初期化は許可されていません。");
        }
        if (!PermissionPolicy.CanResetDemoData(ctx))
        {
            throw PermissionDeniedException.ForOperation("サンプルデータ初期化");
        }

        await DeleteAllAsync().ConfigureAwait(false);
        await SeedAsync().ConfigureAwait(false);

        logger.LogInformation("デモ用サンプルデータを初期状態へリセットしました。操作者: {LoginId}", ctx.LoginId);
    }

    /// <summary>サンプルデータを投入する</summary>
    private async Task SeedAsync()
    {
        // --- 拠点（複数拠点機能を提示できるよう2件） ---
        var honshaId = await siteRepository.CreateAsync(new Site
        {
            SiteName = "本社",
            Address = "東京都千代田区丸の内1-1-1",
            LabelCompanyName = "株式会社サンプルホールディングス",
            LabelSizeType = LabelSizeType.Preset1,
        }).ConfigureAwait(false);

        var osakaId = await siteRepository.CreateAsync(new Site
        {
            SiteName = "大阪支社",
            Address = "大阪府大阪市北区梅田2-2-2",
            LabelCompanyName = "株式会社サンプルホールディングス 大阪支社",
            LabelSizeType = LabelSizeType.Preset1,
        }).ConfigureAwait(false);

        // --- 会社名マスタ（架空名称） ---
        foreach (var (name, address, tel) in new (string, string, string)[]
        {
            ("株式会社サンプル商事", "東京都港区芝3-3-3", "03-1111-2222"),
            ("テスト工業株式会社", "神奈川県横浜市西区北幸4-4-4", "045-333-4444"),
            ("ダミーソリューションズ株式会社", "東京都渋谷区渋谷5-5-5", "03-5555-6666"),
            ("架空システム開発株式会社", "大阪府大阪市中央区本町6-6-6", "06-7777-8888"),
        })
        {
            await companyRepository.CreateAsync(new Company { CompanyName = name, Address = address, Tel = tel })
                .ConfigureAwait(false);
        }

        // --- 操作ユーザー（権限ロールごとに1件ずつ） ---
        var adminId = await CreateOperatorAsync(AdminLoginId, "デモ管理者", "demo-admin@example.com", RoleType.Administrator, honshaId)
            .ConfigureAwait(false);
        var receptionId = await CreateOperatorAsync(ReceptionLoginId, "デモ受付", "demo-reception@example.com", RoleType.Reception, honshaId)
            .ConfigureAwait(false);
        await CreateOperatorAsync(GeneralLoginId, "デモ一般", "demo-general@example.com", RoleType.General, osakaId)
            .ConfigureAwait(false);

        // --- 来館予約（ステータス違い・複数名同時来館を含む） ---
        var today = DateOnly.FromDateTime(DateTime.Today);

        // ① 未入館・複数名（代表者＋同行者2名）
        await CreateReservationAsync(
            honshaId, receptionId, today, new TimeOnly(14, 0), VisitorStatus.NotArrived, ReservationType.Advance,
            ("山田 太郎", "株式会社サンプル商事", "yamada@example.com"),
            [("佐藤 次郎", "株式会社サンプル商事"), ("鈴木 三郎", "株式会社サンプル商事")]).ConfigureAwait(false);

        // ② 入館中・単独
        await CreateReservationAsync(
            honshaId, receptionId, today, new TimeOnly(10, 30), VisitorStatus.CheckedIn, ReservationType.Advance,
            ("田中 花子", "テスト工業株式会社", "tanaka@example.com"), []).ConfigureAwait(false);

        // ③ 退館済・複数名
        await CreateReservationAsync(
            osakaId, adminId, today.AddDays(-1), new TimeOnly(15, 0), VisitorStatus.CheckedOut, ReservationType.Advance,
            ("高橋 一郎", "ダミーソリューションズ株式会社", "takahashi@example.com"),
            [("伊藤 二郎", "ダミーソリューションズ株式会社")]).ConfigureAwait(false);

        // ④ 当日受付（walk-in）・入館中・メールアドレスなし
        await CreateReservationAsync(
            honshaId, receptionId, today, TimeOnly.FromDateTime(DateTime.Now), VisitorStatus.CheckedIn, ReservationType.WalkIn,
            ("渡辺 四郎", "架空システム開発株式会社", null), []).ConfigureAwait(false);

        // ⑤ キャンセル
        await CreateReservationAsync(
            honshaId, adminId, today.AddDays(1), new TimeOnly(9, 0), VisitorStatus.Cancelled, ReservationType.Advance,
            ("中村 五郎", "株式会社サンプル商事", "nakamura@example.com"), []).ConfigureAwait(false);

        logger.LogInformation("デモ用サンプルデータを投入しました。");
    }

    /// <summary>デモ用の操作ユーザーを1件作成する</summary>
    private Task<int> CreateOperatorAsync(string loginId, string displayName, string email, RoleType role, int siteId)
        => operatorRepository.CreateAsync(new Operator
        {
            LoginId = loginId,
            PasswordHash = PasswordHasher.Hash(DemoPassword),
            DisplayName = displayName,
            Email = email,
            RoleType = role,
            SiteId = siteId,
            IsActive = true,
        });

    /// <summary>サンプル予約を1件（グループ単位で）作成する</summary>
    private async Task CreateReservationAsync(
        int siteId,
        int operatorId,
        DateOnly visitDate,
        TimeOnly visitTimeFrom,
        VisitorStatus status,
        ReservationType reservationType,
        (string Name, string Company, string? Email) representative,
        IReadOnlyList<(string Name, string Company)> companions)
    {
        var groupId = Guid.NewGuid();
        var members = new List<Visitor>
        {
            new()
            {
                VisitorId = Guid.NewGuid(),
                ReservationGroupId = groupId,
                IsRepresentative = true,
                SiteId = siteId,
                CompanyName = representative.Company,
                VisitorName = representative.Name,
                VisitorEmail = representative.Email,
                VisitDate = visitDate,
                VisitTimeFrom = visitTimeFrom,
                VisitTimeTo = visitTimeFrom.AddHours(1),
                HostEmployeeName = "営業部 担当者",
                PurposeOfVisit = "打合せ",
                ReservationType = reservationType,
                CreatedByOperatorId = operatorId,
                Status = status,
            },
        };

        members.AddRange(companions.Select(c => new Visitor
        {
            VisitorId = Guid.NewGuid(),
            ReservationGroupId = groupId,
            IsRepresentative = false,
            SiteId = siteId,
            CompanyName = c.Company,
            VisitorName = c.Name,
            VisitDate = visitDate,
            VisitTimeFrom = visitTimeFrom,
            VisitTimeTo = visitTimeFrom.AddHours(1),
            HostEmployeeName = "営業部 担当者",
            PurposeOfVisit = "打合せ",
            ReservationType = reservationType,
            CreatedByOperatorId = operatorId,
            Status = status,
        }));

        // QRトークンは代表者行にのみ発行する（設計書 6.2節）。
        // 状態に合わせて読取回数を設定し、入館中は1回・退館済は2回読み取られた状態を再現する。
        var usedCount = status switch
        {
            VisitorStatus.CheckedIn => (byte)1,
            VisitorStatus.CheckedOut => (byte)2,
            _ => (byte)0,
        };

        var token = new QrToken
        {
            TokenId = Guid.NewGuid(),
            VisitorId = members[0].VisitorId,
            TokenValue = Guid.NewGuid(),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = VisitRules.CalculateExpiresAtUtc(visitDate, visitTimeFrom),
            UsedCount = usedCount,
            IsValid = status != VisitorStatus.Cancelled,
        };

        using var session = sessionFactory.CreateSession(beginTransaction: true);

        foreach (var member in members)
        {
            await visitorRepository.CreateAsync(member, session).ConfigureAwait(false);
        }

        await qrTokenRepository.CreateAsync(token, session).ConfigureAwait(false);

        // 入館中・退館済のサンプルには、対応する入退館履歴も作成しておく
        if (usedCount >= 1)
        {
            var checkInAt = visitDate.ToDateTime(visitTimeFrom, DateTimeKind.Local).ToUniversalTime();
            await accessLogRepository.CreateManyAsync(
                members.Select(m => new AccessLog
                {
                    VisitorId = m.VisitorId,
                    SiteId = m.SiteId,
                    TokenId = token.TokenId,
                    AccessType = AccessType.CheckIn,
                    AccessDateTime = checkInAt,
                    DeviceInfo = "デモ用受付端末",
                }), session).ConfigureAwait(false);

            if (usedCount >= 2)
            {
                await accessLogRepository.CreateManyAsync(
                    members.Select(m => new AccessLog
                    {
                        VisitorId = m.VisitorId,
                        SiteId = m.SiteId,
                        TokenId = token.TokenId,
                        AccessType = AccessType.CheckOut,
                        AccessDateTime = checkInAt.AddHours(1),
                        DeviceInfo = "デモ用受付端末",
                    }), session).ConfigureAwait(false);
            }
        }

        session.Commit();
    }

    /// <summary>
    /// デモ用データベースの全データを削除する。
    /// 外部キーの参照順に従って削除するため、Repository ではなく直接SQLを実行する。
    /// </summary>
    private Task DeleteAllAsync()
    {
        const string sql = """
            DELETE FROM dbo.AccessLogs;
            DELETE FROM dbo.MailHistory;
            DELETE FROM dbo.QRTokens;
            DELETE FROM dbo.Visitors;
            DELETE FROM dbo.AuditLog;
            DELETE FROM dbo.Operators;
            DELETE FROM dbo.Companies;
            DELETE FROM dbo.Sites;
            DBCC CHECKIDENT('dbo.Sites', RESEED, 0) WITH NO_INFOMSGS;
            DBCC CHECKIDENT('dbo.Operators', RESEED, 0) WITH NO_INFOMSGS;
            DBCC CHECKIDENT('dbo.Companies', RESEED, 0) WITH NO_INFOMSGS;
            """;

        using var session = sessionFactory.CreateSession(beginTransaction: true);
        using var command = session.Connection.CreateCommand();

        command.Transaction = session.Transaction;
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.ExecuteNonQuery();

        session.Commit();
        return Task.CompletedTask;
    }
}
