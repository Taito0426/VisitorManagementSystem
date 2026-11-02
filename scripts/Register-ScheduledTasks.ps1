<#
.SYNOPSIS
    バックアップとログアーカイブのタスクを Windows タスクスケジューラへ登録します
    （設計書 13.1節 ④、10.1節、10.4節）。

.DESCRIPTION
    SQL Server Express は SQL Server Agent が利用できないため、
    定時処理は Windows タスクスケジューラで実行します。

    登録するタスク:
      1. VisitorMgmt-Backup       毎日 23:00  … Backup-VisitorMgmt.ps1 を実行
      2. VisitorMgmt-LogArchive   毎日 01:00  … BatchRunner log-archive を
                                                --only-on-scheduled-day 付きで実行

    ログアーカイブは「翌月第1営業日に実行」する仕様のため（設計書 10.4節）、
    毎日起動して当日が対象営業日でなければ何もしない方式としています。
    タスクスケジューラでは「第N営業日」を直接表現できないための構成です。

    本スクリプトは管理者権限で実行してください。

.PARAMETER BackupPath
    バックアップファイルの出力先フォルダ。

.PARAMETER BatchRunnerPath
    VisitorManagement.BatchRunner.exe のフルパス。

.PARAMETER BackupTime
    バックアップの実行時刻（HH:mm）。既定は 23:00

.PARAMETER ArchiveTime
    ログアーカイブの実行時刻（HH:mm）。既定は 01:00

.PARAMETER RunAsUser
    タスクの実行アカウント。既定は SYSTEM。
    BACKUP DATABASE 権限を持つアカウントを指定してください。

.EXAMPLE
    .\Register-ScheduledTasks.ps1 -BackupPath "D:\Backup" `
        -BatchRunnerPath "C:\Program Files\VisitorManagement\VisitorManagement.BatchRunner.exe"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BackupPath,
    [Parameter(Mandatory = $true)][string]$BatchRunnerPath,
    [string]$BackupTime = '23:00',
    [string]$ArchiveTime = '01:00',
    [string]$RunAsUser = 'SYSTEM'
)

$ErrorActionPreference = 'Stop'

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$backupScript = Join-Path $scriptDirectory 'Backup-VisitorMgmt.ps1'

if (-not (Test-Path $backupScript)) {
    throw "バックアップスクリプトが見つかりません: $backupScript"
}
if (-not (Test-Path $BatchRunnerPath)) {
    throw "BatchRunner が見つかりません: $BatchRunnerPath"
}

function Register-VisitorMgmtTask {
    param(
        [string]$TaskName,
        [string]$Description,
        [Microsoft.Management.Infrastructure.CimInstance]$Action,
        [string]$Time
    )

    $trigger = New-ScheduledTaskTrigger -Daily -At $Time
    $principal = New-ScheduledTaskPrincipal -UserId $RunAsUser -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd `
        -ExecutionTimeLimit (New-TimeSpan -Hours 2)

    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Output "既存のタスクを削除しました: $TaskName"
    }

    Register-ScheduledTask -TaskName $TaskName -Description $Description `
        -Action $Action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null

    Write-Output "登録しました: $TaskName ($Time)"
}

# ---- ① バックアップ（毎日 23:00。設計書 10.1節） ----
$backupAction = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument ("-NoProfile -ExecutionPolicy Bypass -File `"$backupScript`" -BackupPath `"$BackupPath`"")

Register-VisitorMgmtTask `
    -TaskName 'VisitorMgmt-Backup' `
    -Description '来館者記録管理システム: データベースのバックアップ（設計書 10.1節）' `
    -Action $backupAction `
    -Time $BackupTime

# ---- ② ログアーカイブ（毎日起動し、対象営業日のみ実行。設計書 10.4節） ----
$archiveAction = New-ScheduledTaskAction `
    -Execute $BatchRunnerPath `
    -Argument 'log-archive --only-on-scheduled-day' `
    -WorkingDirectory (Split-Path -Parent $BatchRunnerPath)

Register-VisitorMgmtTask `
    -TaskName 'VisitorMgmt-LogArchive' `
    -Description '来館者記録管理システム: 入退館履歴・メール送信履歴の月次CSVアーカイブ（設計書 10.4節）' `
    -Action $archiveAction `
    -Time $ArchiveTime

Write-Output ''
Write-Output '登録済みタスクの確認: Get-ScheduledTask -TaskName VisitorMgmt-*'
