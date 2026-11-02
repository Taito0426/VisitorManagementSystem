using System.Data;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using VisitorManagement.Core.Abstractions;

namespace VisitorManagement.Infrastructure.Data;

/// <summary>
/// 初期スキーマ作成スクリプト（db/01_CreateSchema.sql）を実行する初期化処理。
/// 主にデモ版の初回起動時に使用する（設計書 19.2節）。
/// スクリプトは冪等であり、既に作成済みのオブジェクトには影響しない。
/// </summary>
public sealed class DatabaseInitializer(
    IDbSessionFactory sessionFactory,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    /// <summary>埋め込みリソースとして同梱したスキーマ作成スクリプト</summary>
    private const string SchemaResourceName = "VisitorManagement.Infrastructure.Data.Sql.CreateSchema.sql";

    /// <inheritdoc />
    public Task<bool> IsSchemaCreatedAsync(CancellationToken cancellationToken = default)
    {
        using var session = sessionFactory.CreateSession();
        using var command = session.Connection.CreateCommand();

        // 主要テーブルの存在で判定する
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.Visitors', N'U') IS NULL THEN 0 ELSE 1 END;";
        command.CommandType = CommandType.Text;

        var exists = Convert.ToInt32(command.ExecuteScalar()) == 1;
        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var script = ReadSchemaScript();

        using var session = sessionFactory.CreateSession();

        // GO はバッチ区切りであり T-SQL の構文ではないため、ADO.NET から実行する際は分割して送る
        foreach (var batch in SplitBatches(script))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var command = session.Connection.CreateCommand();
            command.CommandText = batch;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = 120;
            command.ExecuteNonQuery();
        }

        logger.LogInformation("初期スキーマの作成（確認）が完了しました。");
        return Task.CompletedTask;
    }

    /// <summary>埋め込みリソースからスキーマ作成スクリプトを読み込む</summary>
    private static string ReadSchemaScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"スキーマ作成スクリプトが埋め込まれていません: {SchemaResourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>GO 行でスクリプトをバッチに分割する</summary>
    internal static IEnumerable<string> SplitBatches(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var builder = new StringBuilder();

        foreach (var line in script.Split('\n'))
        {
            var trimmed = line.Trim().TrimEnd('\r');

            if (string.Equals(trimmed, "GO", StringComparison.OrdinalIgnoreCase))
            {
                var batch = builder.ToString().Trim();
                if (batch.Length > 0)
                {
                    yield return batch;
                }

                builder.Clear();
                continue;
            }

            builder.AppendLine(line);
        }

        var last = builder.ToString().Trim();
        if (last.Length > 0)
        {
            yield return last;
        }
    }
}
