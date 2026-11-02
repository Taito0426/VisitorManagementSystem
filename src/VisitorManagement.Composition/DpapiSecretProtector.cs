using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace VisitorManagement.Composition;

/// <summary>
/// 設定ファイル中の機微情報を Windows DPAPI で保護する（設計書 11.3節）。
///
/// 接続文字列やAPIキーの平文保存を避けるため、マシン単位（LocalMachine スコープ）で
/// 暗号化した値を "ENC:&lt;base64&gt;" 形式で appsettings.json に保存し、起動時に復号して設定へ反映する。
/// WinForms 管理アプリと、オンプレ版の受付Webアプリの双方から使用する。
/// クラウド版（Azure App Service）では Azure Key Vault 参照または環境変数を使用するため本クラスは使用しない。
/// </summary>
public static class DpapiSecretProtector
{
    /// <summary>暗号化済みの値であることを示す接頭辞</summary>
    public const string Prefix = "ENC:";

    /// <summary>
    /// 起動時に復号する対象の設定キー。値が "ENC:" で始まる場合のみ復号する。
    /// </summary>
    private static readonly string[] ProtectedKeys =
    [
        "Database:ConnectionStrings:SqlExpress",
        "Database:ConnectionStrings:AzureSql",
        "Demo:ConnectionString",
        "Mail:SendGrid:ApiKey",
        "Mail:Smtp:Password",
        "LogArchive:AzureBlob:ConnectionString",
    ];

    /// <summary>平文を暗号化して "ENC:&lt;base64&gt;" 形式の文字列を返す</summary>
    [SupportedOSPlatform("windows")]
    public static string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText), optionalEntropy: null, scope: DataProtectionScope.LocalMachine);

        return Prefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// "ENC:" で始まる値を復号する。平文の場合はそのまま返すため、
    /// 暗号化前後のどちらの設定ファイルでも動作する。
    /// </summary>
    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "DPAPI で暗号化された設定値は Windows 上でのみ復号できます。"
                + "Linux 環境では環境変数または Azure Key Vault を使用してください（設計書 11.3節）。");
        }

        try
        {
            var encrypted = Convert.FromBase64String(value[Prefix.Length..]);
            var plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException(
                "設定ファイル中の暗号化された値を復号できませんでした。"
                + "値は暗号化を行ったマシン上でのみ復号できます（DPAPI マシン単位スコープ）。"
                + "scripts\\Protect-Secret.ps1 で暗号化し直してください。", ex);
        }
    }

    /// <summary>
    /// 設定内の保護対象キーを復号し、以降の Options バインドで平文が使われるようにする。
    /// 暗号化されていない環境では何も行わないため、常に呼び出して差し支えない。
    /// </summary>
    public static void UnprotectConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var key in ProtectedKeys)
        {
            var value = configuration[key];
            if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            configuration[key] = Unprotect(value);
        }
    }
}
