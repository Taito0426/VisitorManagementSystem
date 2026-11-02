using VisitorManagement.Infrastructure.Data;

namespace VisitorManagement.Tests;

/// <summary>
/// スキーマ作成スクリプトのバッチ分割のテスト（設計書 19.2節）。
/// GO は T-SQL の構文ではなくバッチ区切りのため、ADO.NET から実行する際は分割が必要。
/// </summary>
public sealed class DatabaseInitializerTests
{
    [Fact]
    public void GO行でバッチが分割される()
    {
        const string script = """
            CREATE TABLE A (Id int);
            GO
            CREATE TABLE B (Id int);
            GO
            """;

        var batches = DatabaseInitializer.SplitBatches(script).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Contains("CREATE TABLE A", batches[0], StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE B", batches[1], StringComparison.Ordinal);
        Assert.DoesNotContain("GO", batches[0], StringComparison.Ordinal);
    }

    [Fact]
    public void 末尾にGOがなくても最後のバッチが返る()
    {
        const string script = """
            CREATE TABLE A (Id int);
            GO
            CREATE TABLE B (Id int);
            """;

        var batches = DatabaseInitializer.SplitBatches(script).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Contains("CREATE TABLE B", batches[1], StringComparison.Ordinal);
    }

    [Fact]
    public void 空のバッチは返らない()
    {
        const string script = """
            GO

            GO
            SELECT 1;
            GO
            """;

        var batches = DatabaseInitializer.SplitBatches(script).ToList();

        Assert.Single(batches);
        Assert.Contains("SELECT 1", batches[0], StringComparison.Ordinal);
    }

    [Fact]
    public void GOを含む語句はバッチ区切りとして扱わない()
    {
        const string script = """
            SELECT 'GOOD' AS Result;
            SELECT Category FROM T WHERE Category = 'GO';
            """;

        var batches = DatabaseInitializer.SplitBatches(script).ToList();

        // 行全体が GO の場合のみ区切りとみなすため、1バッチにまとまる
        Assert.Single(batches);
    }

    [Fact]
    public void 実際のスキーマ作成スクリプトが複数バッチに分割できる()
    {
        // 埋め込みリソースとして同梱したスクリプトが読み込めることを確認する
        var initializerType = typeof(DatabaseInitializer);
        using var stream = initializerType.Assembly
            .GetManifestResourceStream("VisitorManagement.Infrastructure.Data.Sql.CreateSchema.sql");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var batches = DatabaseInitializer.SplitBatches(reader.ReadToEnd()).ToList();

        // 8テーブル＋インデックス群のため十分な数のバッチに分割される
        Assert.True(batches.Count > 10, $"バッチ数が想定より少ないです: {batches.Count}");
        Assert.Contains(batches, b => b.Contains("dbo.Visitors", StringComparison.Ordinal));
        Assert.Contains(batches, b => b.Contains("UQ_Visitors_GroupRepresentative", StringComparison.Ordinal));
    }
}
