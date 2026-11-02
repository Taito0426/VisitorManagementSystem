/*
 * サービス用SQLログインの作成と最小権限付与（SQL Server Express）
 * 設計書 11.1節「DB専用の最小権限アカウントを発行し、対象テーブルへのCRUD権限のみを付与する
 * （sysadmin等の強い権限は付与しない）」、13.1節 ②。
 *
 * パスワードはスクリプトに直書きせず、sqlcmd の変数で渡すこと。
 * 実行例)
 *   sqlcmd -S .\SQLEXPRESS -E -i 02_CreateLogin_SqlExpress.sql -v SvcPassword="<パスワード>"
 */

SET NOCOUNT ON;
GO

/* ---- サーバーレベル：ログイン作成（サーバーロールは付与しない） ---- */
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'svc_visitor')
BEGIN
    CREATE LOGIN [svc_visitor]
        WITH PASSWORD = N'$(SvcPassword)',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = [VisitorMgmt];
    PRINT N'ログイン svc_visitor を作成しました。';
END
ELSE
BEGIN
    PRINT N'ログイン svc_visitor は既に存在します。';
END
GO

/* ---- 本番用データベース：ユーザー作成とCRUD権限付与 ---- */
USE [VisitorMgmt];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'svc_visitor')
BEGIN
    CREATE USER [svc_visitor] FOR LOGIN [svc_visitor];
END
GO
/* テーブルへのCRUDのみ。db_owner 等の強い権限は付与しない */
ALTER ROLE db_datareader ADD MEMBER [svc_visitor];
ALTER ROLE db_datawriter ADD MEMBER [svc_visitor];
GO
PRINT N'VisitorMgmt に svc_visitor のCRUD権限を付与しました。';
GO

/* ---- デモ用データベース：同一ログインを使用（設計書 19.2節） ---- */
USE [VisitorMgmtDemo];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'svc_visitor')
BEGIN
    CREATE USER [svc_visitor] FOR LOGIN [svc_visitor];
END
GO
ALTER ROLE db_datareader ADD MEMBER [svc_visitor];
ALTER ROLE db_datawriter ADD MEMBER [svc_visitor];
GO
/*
 * デモ版は初回起動時にアプリがスキーマ作成とシード投入を自動実行するため（設計書 19.2節）、
 * デモ用DBに限り DDL 権限を追加で付与する。本番用DBには付与しない。
 */
GRANT CREATE TABLE TO [svc_visitor];
GRANT ALTER ON SCHEMA::dbo TO [svc_visitor];
GRANT REFERENCES ON SCHEMA::dbo TO [svc_visitor];
GO
PRINT N'VisitorMgmtDemo に svc_visitor のCRUD権限とデモ用DDL権限を付与しました。';
GO
