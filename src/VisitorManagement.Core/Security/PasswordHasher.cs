using System.Security.Cryptography;

namespace VisitorManagement.Core.Security;

/// <summary>
/// 操作ユーザーのパスワードハッシュ（設計書 11.1節）。
/// ソルト付きPBKDF2(HMAC-SHA256)で保存し、平文保存は行わない。
/// 保存形式: "PBKDF2$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;"（Operators.PasswordHash nvarchar(256)）。
/// </summary>
public static class PasswordHasher
{
    /// <summary>ハッシュ形式の識別子</summary>
    private const string Algorithm = "PBKDF2";

    /// <summary>反復回数</summary>
    private const int Iterations = 210_000;

    /// <summary>ソルト長（バイト）</summary>
    private const int SaltSize = 16;

    /// <summary>ハッシュ長（バイト）</summary>
    private const int HashSize = 32;

    /// <summary>
    /// 平文パスワードからハッシュ文字列を生成する。
    /// </summary>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Algorithm}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// 平文パスワードが保存済みハッシュと一致するかを検証する。
    /// タイミング攻撃を避けるため固定時間比較を用いる。
    /// </summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
