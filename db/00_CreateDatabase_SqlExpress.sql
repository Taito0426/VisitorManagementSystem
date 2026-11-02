/*
 * データベース作成（SQL Server Express・オンプレ版／デモ版）
 * 設計書 13.1節 ①、19.2節。
 *
 * 実行例)
 *   sqlcmd -S .\SQLEXPRESS -E -i 00_CreateDatabase_SqlExpress.sql
 *
 * 本番用（VisitorMgmt）とデモ用（VisitorMgmtDemo）を同一インスタンス上に
 * データベース名のみ分離して作成する（設計書 付録A.3）。
 */

SET NOCOUNT ON;
GO

IF DB_ID(N'VisitorMgmt') IS NULL
BEGIN
    CREATE DATABASE [VisitorMgmt];
    PRINT N'データベース VisitorMgmt を作成しました。';
END
ELSE
BEGIN
    PRINT N'データベース VisitorMgmt は既に存在します。';
END
GO

IF DB_ID(N'VisitorMgmtDemo') IS NULL
BEGIN
    CREATE DATABASE [VisitorMgmtDemo];
    PRINT N'データベース VisitorMgmtDemo を作成しました。';
END
ELSE
BEGIN
    PRINT N'データベース VisitorMgmtDemo は既に存在します。';
END
GO

/* 日本語の照合順序を明示（既定が Japanese 以外のインスタンスでも一貫した並び順にする） */
ALTER DATABASE [VisitorMgmt] COLLATE Japanese_CI_AS;
GO
ALTER DATABASE [VisitorMgmtDemo] COLLATE Japanese_CI_AS;
GO
