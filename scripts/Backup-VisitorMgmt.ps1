<#
.SYNOPSIS
    SQL Server Express 上のデータベースをバックアップします（設計書 10.1節）。

.DESCRIPTION
    SQL Server Express は SQL Server Agent が利用できないため、
    Windows タスクスケジューラから本スクリプトを定時実行してバックアップを取得します。

    アプリケーションのサービスアカウント（svc_visitor）は最小権限のため
    BACKUP DATABASE 権限を持ちません（設計書 11.1節）。
    本スクリプトは Windows 認証で実行し、実行アカウントに
    sysadmin または db_backupoperator ロールを付与しておいてください。

    保管期間を超えた .bak ファイルは自動的に削除します（世代管理）。

.PARAMETER ServerInstance
    接続先の SQL Server インスタンス。既定は .\SQLEXPRESS

.PARAMETER Database
    バックアップ対象のデータベース名。既定は VisitorMgmt

.PARAMETER BackupPath
    バックアップファイルの出力先フォルダ。

.PARAMETER RetentionDays
    バックアップファイルの保管日数。既定は 30 日。

.EXAMPLE
    .\Backup-VisitorMgmt.ps1 -BackupPath "D:\Backup"

.EXAMPLE
    # タスクスケジューラ登録例（毎日 23:00）
    .\Register-ScheduledTasks.ps1 -BackupPath "D:\Backup"
#>
[CmdletBinding()]
param(
    [string]$ServerInstance = '.\SQLEXPRESS',
    [string]$Database = 'VisitorMgmt',
    [Parameter(Mandatory = $true)][string]$BackupPath,
    [int]$RetentionDays = 30
)

$ErrorActionPreference = 'Stop'

function Write-Log([string]$message) {
    Write-Output ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message)
}

Write-Log "バックアップを開始します。インスタンス=$ServerInstance データベース=$Database"

if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
    Write-Log "出力先フォルダを作成しました: $BackupPath"
}

$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$fileName = "{0}_{1}.bak" -f $Database, $timestamp
$filePath = Join-Path $BackupPath $fileName

# データベース名・パスは識別子／文字列リテラルとして安全にエスケープする
$escapedDatabase = $Database.Replace(']', ']]')
$escapedPath = $filePath.Replace("'", "''")

# バックアップ圧縮（COMPRESSION）は Express Edition ではサポートされないため、
# エディションを判定してオプションを切り替える。
# （設計書 10.1節のサンプルSQLは COMPRESSION を含むが、Express ではエラー 1844 となる）
$edition = (& sqlcmd -S $ServerInstance -E -I -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(nvarchar(128), SERVERPROPERTY('Edition'));") -join ''
$supportsCompression = $edition -notmatch 'Express|Web'
$options = if ($supportsCompression) { 'INIT, COMPRESSION, CHECKSUM' } else { 'INIT, CHECKSUM' }

Write-Log "エディション: $($edition.Trim()) / バックアップオプション: $options"

$query = @"
BACKUP DATABASE [$escapedDatabase]
TO DISK = N'$escapedPath'
WITH $options;
"@

# -I : QUOTED_IDENTIFIER を有効化する。
#      dbo.Visitors にフィルター選択インデックスがあるため、同テーブルを扱う操作では必須
& sqlcmd -S $ServerInstance -E -I -b -Q $query
if ($LASTEXITCODE -ne 0) {
    throw "BACKUP DATABASE の実行に失敗しました（終了コード: $LASTEXITCODE）。"
}

$size = [Math]::Round((Get-Item $filePath).Length / 1MB, 1)
Write-Log "バックアップを作成しました: $filePath ($size MB)"

# ---- 世代管理：保管期間を超えたバックアップを削除する ----
$threshold = (Get-Date).AddDays(-$RetentionDays)
$deleted = 0

Get-ChildItem -Path $BackupPath -Filter "$Database`_*.bak" -File |
    Where-Object { $_.CreationTime -lt $threshold } |
    ForEach-Object {
        Remove-Item $_.FullName -Force
        $deleted++
        Write-Log "保管期間を超えたバックアップを削除しました: $($_.Name)"
    }

Write-Log "完了しました。削除件数: $deleted / 保管期間: $RetentionDays 日"
