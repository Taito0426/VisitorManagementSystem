using VisitorManagement.Core.Security;

namespace VisitorManagement.Tests;

/// <summary>
/// パスワードハッシュのテスト（設計書 11.1節：ソルト付きハッシュ・平文保存禁止）。
/// </summary>
public sealed class PasswordHasherTests
{
    [Fact]
    public void 正しいパスワードは検証に成功する()
    {
        var hash = PasswordHasher.Hash("Passw0rd!");

        Assert.True(PasswordHasher.Verify("Passw0rd!", hash));
    }

    [Fact]
    public void 誤ったパスワードは検証に失敗する()
    {
        var hash = PasswordHasher.Hash("Passw0rd!");

        Assert.False(PasswordHasher.Verify("passw0rd!", hash));
        Assert.False(PasswordHasher.Verify("Passw0rd", hash));
        Assert.False(PasswordHasher.Verify(string.Empty, hash));
    }

    [Fact]
    public void ハッシュに平文が含まれない()
    {
        const string password = "SuperSecret123";

        var hash = PasswordHasher.Hash(password);

        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void 同じパスワードでもソルトにより毎回異なるハッシュになる()
    {
        var first = PasswordHasher.Hash("Passw0rd!");
        var second = PasswordHasher.Hash("Passw0rd!");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("Passw0rd!", first));
        Assert.True(PasswordHasher.Verify("Passw0rd!", second));
    }

    [Fact]
    public void ハッシュはOperatorsのPasswordHash列に収まる長さである()
    {
        var hash = PasswordHasher.Hash("Passw0rd!");

        // Operators.PasswordHash は nvarchar(256)（設計書 9.2.2節）
        Assert.True(hash.Length <= 256, $"ハッシュ長 {hash.Length} が nvarchar(256) を超えています。");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("PBKDF2$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("PBKDF2$1000$***$***")]
    public void 不正な形式のハッシュは例外を出さず検証に失敗する(string storedHash)
        => Assert.False(PasswordHasher.Verify("Passw0rd!", storedHash));
}
