<#
.SYNOPSIS
    appsettings.json に保存する機微情報を Windows DPAPI で暗号化します（設計書 11.3節）。

.DESCRIPTION
    接続文字列・APIキー・SMTPパスワードなどの平文保存を避けるため、
    マシン単位（LocalMachine スコープ）で暗号化した "ENC:<base64>" 形式の文字列を生成します。
    生成した値を appsettings.json の該当項目にそのまま貼り付けてください。
    管理アプリは起動時に自動で復号します。

    暗号化した値は、暗号化を行ったマシン上でのみ復号できます。
    別のマシンへ移設する場合は、移設先で本スクリプトを再実行して値を作り直してください。

.PARAMETER Value
    暗号化する平文。省略した場合は画面入力（非表示）を求めます。

.PARAMETER Decrypt
    指定した場合、"ENC:" 形式の値を復号して内容を確認します。

.EXAMPLE
    .\Protect-Secret.ps1
    # 平文をマスク入力し、暗号化結果を表示します

.EXAMPLE
    .\Protect-Secret.ps1 -Value "Server=.\SQLEXPRESS;Database=VisitorMgmt;User Id=svc_visitor;Password=xxxx;"

.EXAMPLE
    .\Protect-Secret.ps1 -Value "ENC:AQAAAN..." -Decrypt
#>
[CmdletBinding()]
param(
    [string]$Value,
    [switch]$Decrypt
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$prefix = 'ENC:'

# 値が指定されていない場合は画面から入力する（暗号化時はマスク入力）
if (-not $Value) {
    if ($Decrypt) {
        $Value = Read-Host -Prompt '復号する値（ENC:で始まる文字列）'
    }
    else {
        $secure = Read-Host -Prompt '暗号化する平文' -AsSecureString
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try {
            $Value = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        }
        finally {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

if ($Decrypt) {
    if (-not $Value.StartsWith($prefix)) {
        throw "復号対象の値は '$prefix' で始まる必要があります。"
    }

    $encrypted = [Convert]::FromBase64String($Value.Substring($prefix.Length))
    $plainBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)

    Write-Output ([System.Text.Encoding]::UTF8.GetString($plainBytes))
    return
}

$plainBytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
$encrypted = [System.Security.Cryptography.ProtectedData]::Protect(
    $plainBytes, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)

$result = $prefix + [Convert]::ToBase64String($encrypted)

Write-Output ''
Write-Output '--- appsettings.json に貼り付ける値 ---'
Write-Output $result
Write-Output ''
Write-Output '対象キー（いずれか）:'
Write-Output '  Database:ConnectionStrings:SqlExpress'
Write-Output '  Database:ConnectionStrings:AzureSql'
Write-Output '  Demo:ConnectionString'
Write-Output '  Mail:SendGrid:ApiKey'
Write-Output '  Mail:Smtp:Password'
Write-Output '  LogArchive:AzureBlob:ConnectionString'
