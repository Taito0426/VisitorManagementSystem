/*
 * サービス用SQLユーザーの作成と最小権限付与（Azure SQL Database）
 * 設計書 11.1節・13.2節。
 *
 * Azure SQL Database では USE 文によるDBコンテキスト切替が使用できないため（設計書 4.5節）、
 * master と対象データベースに個別に接続して実行すること。
 *
 * 実行例)
 *   -- ① master に接続して実行（ログイン作成）
 *   sqlcmd -S tcp:<サーバー名>.database.windows.net,1433 -d master -U <管理者> -P <パスワード> ^
 *          -i 02_CreateUser_AzureSql.sql -v Step="master" -v SvcPassword="<パスワード>"
 *   -- ② VisitorMgmt に接続して実行（ユーザー作成・権限付与）
 *   sqlcmd -S tcp:<サーバー名>.database.windows.net,1433 -d VisitorMgmt -U <管理者> -P <パスワード> ^
 *          -i 02_CreateUser_AzureSql.sql -v Step="database" -v SvcPassword="<パスワード>"
 */

SET NOCOUNT ON;
GO

/* ---- ① master データベースで実行：ログイン作成 ---- */
IF N'$(Step)' = N'master'
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'svc_visitor')
    BEGIN
        EXEC(N'CREATE LOGIN [svc_visitor] WITH PASSWORD = N''$(SvcPassword)''');
        PRINT N'ログイン svc_visitor を作成しました。';
    END
    ELSE
    BEGIN
        PRINT N'ログイン svc_visitor は既に存在します。';
    END
END
GO

/* ---- ② VisitorMgmt データベースで実行：ユーザー作成とCRUD権限付与 ---- */
IF N'$(Step)' = N'database'
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'svc_visitor')
    BEGIN
        EXEC(N'CREATE USER [svc_visitor] FOR LOGIN [svc_visitor]');
    END

    EXEC(N'ALTER ROLE db_datareader ADD MEMBER [svc_visitor]');
    EXEC(N'ALTER ROLE db_datawriter ADD MEMBER [svc_visitor]');
    PRINT N'VisitorMgmt に svc_visitor のCRUD権限を付与しました。';
END
GO
