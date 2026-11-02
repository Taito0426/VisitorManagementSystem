/*
 * 来館者記録管理システム 初期スキーマ作成スクリプト
 * 設計書「来館者記録管理システム_詳細設計書」版数2.6 9章 データベース設計に準拠。
 *
 * SQL Server Express / Azure SQL Database の双方で実行できるよう、
 * 非互換機能（SQL Server Agent・クロスDBクエリ・CLR統合・USE文）は使用しない（設計書 4.5節）。
 * 対象データベースは接続時に指定すること。
 *   例) sqlcmd -S .\SQLEXPRESS -d VisitorMgmt -I -i 01_CreateSchema.sql
 *
 * 【重要】QUOTED_IDENTIFIER について
 *   dbo.Visitors にはフィルター選択インデックス（UQ_Visitors_GroupRepresentative。
 *   1予約グループに代表者は1名のみ、を保証する）を作成している。
 *   SQL Server の仕様上、フィルター選択インデックスを持つテーブルへの INSERT/UPDATE/DELETE は
 *   QUOTED_IDENTIFIER が ON でなければ失敗する。
 *     - アプリケーション: Microsoft.Data.SqlClient は既定で ON のため対応不要。
 *     - sqlcmd/バッチ  : 既定が OFF のため、-I オプションを付けるか
 *                        スクリプト冒頭で SET QUOTED_IDENTIFIER ON を実行すること。
 */

SET NOCOUNT ON;
SET XACT_ABORT ON;
/* フィルター選択インデックス（UQ_Visitors_GroupRepresentative）の作成に必須。
   sqlcmd は既定で QUOTED_IDENTIFIER OFF のため、スクリプト側で明示的に有効化する。 */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ------------------------------------------------------------------
 * Sites（拠点マスタ・設計書 9.2.1節）
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Sites', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Sites
    (
        SiteId              int             IDENTITY(1,1) NOT NULL,
        SiteName            nvarchar(100)   NOT NULL,
        Address             nvarchar(200)   NULL,
        LabelCompanyName    nvarchar(100)   NULL,           -- 当日受付ラベル印刷用の表示社名
        LabelLogoImage      varbinary(max)  NULL,           -- 当日受付ラベル印刷用のロゴ画像
        LabelSizeType       tinyint         NOT NULL CONSTRAINT DF_Sites_LabelSizeType DEFAULT (1),
        IsActive            bit             NOT NULL CONSTRAINT DF_Sites_IsActive DEFAULT (1),
        CreatedAt           datetime2(0)    NOT NULL CONSTRAINT DF_Sites_CreatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_Sites PRIMARY KEY CLUSTERED (SiteId),
        -- 1:幅60mm×高さ110mm（既定値）、2・3:導入時ヒアリングにより個別設定
        CONSTRAINT CK_Sites_LabelSizeType CHECK (LabelSizeType IN (1, 2, 3))
    );
END
GO

/* ------------------------------------------------------------------
 * Operators（操作ユーザー・設計書 9.2.2節）
 * RoleType・SiteId はいずれも登録時必須（NULL不可）
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Operators', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Operators
    (
        OperatorId          int             IDENTITY(1,1) NOT NULL,
        LoginId             nvarchar(50)    NOT NULL,
        PasswordHash        nvarchar(256)   NOT NULL,       -- ソルト付きPBKDF2。平文保存は行わない
        DisplayName         nvarchar(50)    NOT NULL,
        Email               nvarchar(256)   NOT NULL,       -- 予約登録者宛メールの送信先。必須
        RoleType            tinyint         NOT NULL CONSTRAINT DF_Operators_RoleType DEFAULT (3),
        SiteId              int             NOT NULL,
        IsActive            bit             NOT NULL CONSTRAINT DF_Operators_IsActive DEFAULT (1),
        CreatedAt           datetime2(0)    NOT NULL CONSTRAINT DF_Operators_CreatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_Operators PRIMARY KEY CLUSTERED (OperatorId),
        CONSTRAINT UQ_Operators_LoginId UNIQUE (LoginId),
        CONSTRAINT FK_Operators_Sites FOREIGN KEY (SiteId) REFERENCES dbo.Sites (SiteId),
        -- 1:管理者 2:受付 3:一般
        CONSTRAINT CK_Operators_RoleType CHECK (RoleType IN (1, 2, 3))
    );
END
GO

/* ------------------------------------------------------------------
 * Visitors（来館予約・設計書 9.2.3節）
 * 代表者・同行者を ReservationGroupId で束ねる
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Visitors', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Visitors
    (
        VisitorId           uniqueidentifier NOT NULL CONSTRAINT DF_Visitors_VisitorId DEFAULT (newid()),
        ReservationGroupId  uniqueidentifier NOT NULL CONSTRAINT DF_Visitors_ReservationGroupId DEFAULT (newid()),
        IsRepresentative    bit             NOT NULL CONSTRAINT DF_Visitors_IsRepresentative DEFAULT (1),
        SiteId              int             NOT NULL,
        CompanyName         nvarchar(100)   NULL,
        VisitorName         nvarchar(50)    NOT NULL,
        VisitorNameKana     nvarchar(50)    NULL,
        VisitorEmail        nvarchar(256)   NULL,           -- 代表者は必須運用。同行者行は未使用
        PhoneNumber         nvarchar(20)    NULL,
        VisitDate           date            NOT NULL,
        VisitTimeFrom       time(0)         NOT NULL,
        VisitTimeTo         time(0)         NULL,
        HostEmployeeName    nvarchar(50)    NULL,
        PurposeOfVisit      nvarchar(200)   NULL,
        ReservationType     tinyint         NOT NULL CONSTRAINT DF_Visitors_ReservationType DEFAULT (1),
        CreatedByOperatorId int             NOT NULL,
        Status              tinyint         NOT NULL CONSTRAINT DF_Visitors_Status DEFAULT (0),
        CreatedAt           datetime2(0)    NOT NULL CONSTRAINT DF_Visitors_CreatedAt DEFAULT (sysutcdatetime()),
        UpdatedAt           datetime2(0)    NOT NULL CONSTRAINT DF_Visitors_UpdatedAt DEFAULT (sysutcdatetime()),
        RowVersion          rowversion      NOT NULL,       -- 楽観的排他制御用
        CONSTRAINT PK_Visitors PRIMARY KEY CLUSTERED (VisitorId),
        CONSTRAINT FK_Visitors_Sites FOREIGN KEY (SiteId) REFERENCES dbo.Sites (SiteId),
        CONSTRAINT FK_Visitors_Operators FOREIGN KEY (CreatedByOperatorId) REFERENCES dbo.Operators (OperatorId),
        -- 1:事前予約 2:当日受付
        CONSTRAINT CK_Visitors_ReservationType CHECK (ReservationType IN (1, 2)),
        -- 0:未入館 1:入館中 2:退館済 9:キャンセル
        CONSTRAINT CK_Visitors_Status CHECK (Status IN (0, 1, 2, 9))
    );
END
GO

/* ------------------------------------------------------------------
 * QRTokens（QR発行・設計書 9.2.4節）
 * 1予約につき1行のみ、代表者の VisitorId に紐づけて発行する
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.QRTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QRTokens
    (
        TokenId             uniqueidentifier NOT NULL CONSTRAINT DF_QRTokens_TokenId DEFAULT (newid()),
        VisitorId           uniqueidentifier NOT NULL,      -- 代表者行のみ
        TokenValue          uniqueidentifier NOT NULL CONSTRAINT DF_QRTokens_TokenValue DEFAULT (newid()),
        IssuedAt            datetime2(0)    NOT NULL CONSTRAINT DF_QRTokens_IssuedAt DEFAULT (sysutcdatetime()),
        ExpiresAt           datetime2(0)    NOT NULL,       -- 予約時刻-30分〜+2時間、当日受付は受付時刻基準
        UsedCount           tinyint         NOT NULL CONSTRAINT DF_QRTokens_UsedCount DEFAULT (0),
        ReissueCount        tinyint         NOT NULL CONSTRAINT DF_QRTokens_ReissueCount DEFAULT (0),
        IsValid             bit             NOT NULL CONSTRAINT DF_QRTokens_IsValid DEFAULT (1),
        CONSTRAINT PK_QRTokens PRIMARY KEY CLUSTERED (TokenId),
        CONSTRAINT UQ_QRTokens_TokenValue UNIQUE (TokenValue),
        CONSTRAINT FK_QRTokens_Visitors FOREIGN KEY (VisitorId) REFERENCES dbo.Visitors (VisitorId)
    );
END
GO

/* ------------------------------------------------------------------
 * AccessLogs（入退館履歴・設計書 9.2.5節）
 * 代表者QRの1回の読取につき、グループ内の来館者ごとに1件ずつ記録する
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.AccessLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccessLogs
    (
        LogId               bigint          IDENTITY(1,1) NOT NULL,
        VisitorId           uniqueidentifier NOT NULL,
        SiteId              int             NOT NULL,
        TokenId             uniqueidentifier NULL,          -- 手動受付時はNULL許容
        AccessType          tinyint         NOT NULL,
        AccessDateTime      datetime2(0)    NOT NULL CONSTRAINT DF_AccessLogs_AccessDateTime DEFAULT (sysutcdatetime()),
        DeviceInfo          nvarchar(100)   NULL,
        OperatorNote        nvarchar(200)   NULL,
        CONSTRAINT PK_AccessLogs PRIMARY KEY CLUSTERED (LogId),
        CONSTRAINT FK_AccessLogs_Visitors FOREIGN KEY (VisitorId) REFERENCES dbo.Visitors (VisitorId),
        CONSTRAINT FK_AccessLogs_Sites FOREIGN KEY (SiteId) REFERENCES dbo.Sites (SiteId),
        CONSTRAINT FK_AccessLogs_QRTokens FOREIGN KEY (TokenId) REFERENCES dbo.QRTokens (TokenId),
        -- 1:入館 2:退館 9:エラー
        CONSTRAINT CK_AccessLogs_AccessType CHECK (AccessType IN (1, 2, 9))
    );
END
GO

/* ------------------------------------------------------------------
 * MailHistory（メール送信履歴・設計書 9.2.6節）
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.MailHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailHistory
    (
        MailId              bigint          IDENTITY(1,1) NOT NULL,
        VisitorId           uniqueidentifier NOT NULL,
        MailType            tinyint         NOT NULL,
        RecipientType       tinyint         NOT NULL,
        Provider            tinyint         NOT NULL,
        SentAt              datetime2(0)    NOT NULL CONSTRAINT DF_MailHistory_SentAt DEFAULT (sysutcdatetime()),
        Status              tinyint         NOT NULL,
        ErrorMessage        nvarchar(500)   NULL,
        CONSTRAINT PK_MailHistory PRIMARY KEY CLUSTERED (MailId),
        CONSTRAINT FK_MailHistory_Visitors FOREIGN KEY (VisitorId) REFERENCES dbo.Visitors (VisitorId),
        -- 1:来館案内 2:リマインド 3:キャンセル通知 4:再送信
        CONSTRAINT CK_MailHistory_MailType CHECK (MailType IN (1, 2, 3, 4)),
        -- 1:招待者(代表者のみ) 2:予約登録者
        CONSTRAINT CK_MailHistory_RecipientType CHECK (RecipientType IN (1, 2)),
        -- 1:SendGrid 2:SMTP
        CONSTRAINT CK_MailHistory_Provider CHECK (Provider IN (1, 2)),
        -- 0:失敗 1:成功
        CONSTRAINT CK_MailHistory_Status CHECK (Status IN (0, 1))
    );
END
GO

/* ------------------------------------------------------------------
 * Companies（会社名マスタ・設計書 9.2.7節）
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Companies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Companies
    (
        CompanyId           int             IDENTITY(1,1) NOT NULL,
        CompanyName         nvarchar(100)   NOT NULL,
        Address             nvarchar(200)   NULL,
        Tel                 nvarchar(20)    NULL,
        CONSTRAINT PK_Companies PRIMARY KEY CLUSTERED (CompanyId)
    );
END
GO

/* ------------------------------------------------------------------
 * AuditLog（操作監査ログ・設計書 9.2.8節）
 * 旧版の SystemConfigHistory は廃止済み
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog
    (
        AuditLogId          bigint          IDENTITY(1,1) NOT NULL,
        TargetEntity        nvarchar(50)    NOT NULL,       -- 例: Sites, Companies, Operators, MailSettings
        TargetId            nvarchar(50)    NULL,
        ChangeType          tinyint         NOT NULL,
        OldValue            nvarchar(max)   NULL,
        NewValue            nvarchar(max)   NULL,
        ChangedAt           datetime2(0)    NOT NULL CONSTRAINT DF_AuditLog_ChangedAt DEFAULT (sysutcdatetime()),
        ChangedByOperatorId int             NOT NULL,
        CONSTRAINT PK_AuditLog PRIMARY KEY CLUSTERED (AuditLogId),
        CONSTRAINT FK_AuditLog_Operators FOREIGN KEY (ChangedByOperatorId) REFERENCES dbo.Operators (OperatorId),
        -- 1:登録 2:更新 3:削除
        CONSTRAINT CK_AuditLog_ChangeType CHECK (ChangeType IN (1, 2, 3))
    );
END
GO

/* ------------------------------------------------------------------
 * インデックス
 * テーブル作成とは独立した冪等ブロックとし、既存DBへの再実行でも
 * 不足しているインデックスのみが追加されるようにする。
 * ------------------------------------------------------------------ */

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Operators_SiteId' AND object_id = OBJECT_ID(N'dbo.Operators'))
    CREATE NONCLUSTERED INDEX IX_Operators_SiteId ON dbo.Operators (SiteId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Visitors_ReservationGroupId' AND object_id = OBJECT_ID(N'dbo.Visitors'))
    CREATE NONCLUSTERED INDEX IX_Visitors_ReservationGroupId ON dbo.Visitors (ReservationGroupId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Visitors_VisitDate_SiteId' AND object_id = OBJECT_ID(N'dbo.Visitors'))
    CREATE NONCLUSTERED INDEX IX_Visitors_VisitDate_SiteId ON dbo.Visitors (VisitDate, SiteId) INCLUDE (Status);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Visitors_CreatedByOperatorId' AND object_id = OBJECT_ID(N'dbo.Visitors'))
    CREATE NONCLUSTERED INDEX IX_Visitors_CreatedByOperatorId ON dbo.Visitors (CreatedByOperatorId);
GO

-- 手動退館処理（設計書 6.4.1節）の氏名検索用
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Visitors_VisitorName' AND object_id = OBJECT_ID(N'dbo.Visitors'))
    CREATE NONCLUSTERED INDEX IX_Visitors_VisitorName ON dbo.Visitors (VisitorName);
GO

-- 1予約グループに代表者は1名のみ（フィルター選択インデックス。QUOTED_IDENTIFIER ON が必須）
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Visitors_GroupRepresentative' AND object_id = OBJECT_ID(N'dbo.Visitors'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Visitors_GroupRepresentative
        ON dbo.Visitors (ReservationGroupId) WHERE IsRepresentative = 1;
GO

-- 1予約（代表者1行）につきトークンは1件
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_QRTokens_VisitorId' AND object_id = OBJECT_ID(N'dbo.QRTokens'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_QRTokens_VisitorId ON dbo.QRTokens (VisitorId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AccessLogs_AccessDateTime' AND object_id = OBJECT_ID(N'dbo.AccessLogs'))
    CREATE NONCLUSTERED INDEX IX_AccessLogs_AccessDateTime ON dbo.AccessLogs (AccessDateTime);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AccessLogs_VisitorId' AND object_id = OBJECT_ID(N'dbo.AccessLogs'))
    CREATE NONCLUSTERED INDEX IX_AccessLogs_VisitorId ON dbo.AccessLogs (VisitorId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AccessLogs_SiteId_AccessDateTime' AND object_id = OBJECT_ID(N'dbo.AccessLogs'))
    CREATE NONCLUSTERED INDEX IX_AccessLogs_SiteId_AccessDateTime ON dbo.AccessLogs (SiteId, AccessDateTime);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MailHistory_SentAt' AND object_id = OBJECT_ID(N'dbo.MailHistory'))
    CREATE NONCLUSTERED INDEX IX_MailHistory_SentAt ON dbo.MailHistory (SentAt);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MailHistory_VisitorId' AND object_id = OBJECT_ID(N'dbo.MailHistory'))
    CREATE NONCLUSTERED INDEX IX_MailHistory_VisitorId ON dbo.MailHistory (VisitorId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Companies_CompanyName' AND object_id = OBJECT_ID(N'dbo.Companies'))
    CREATE NONCLUSTERED INDEX IX_Companies_CompanyName ON dbo.Companies (CompanyName);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_ChangedAt' AND object_id = OBJECT_ID(N'dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_ChangedAt ON dbo.AuditLog (ChangedAt);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_TargetEntity' AND object_id = OBJECT_ID(N'dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_TargetEntity ON dbo.AuditLog (TargetEntity);
GO

PRINT N'来館者記録管理システム: 初期スキーマの作成が完了しました。';
GO
