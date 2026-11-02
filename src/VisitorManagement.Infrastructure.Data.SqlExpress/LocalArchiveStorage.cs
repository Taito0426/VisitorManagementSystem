using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Data.SqlExpress;

/// <summary>
/// ログアーカイブCSVをローカルディスクへ保管する（設計書 10.4節。オンプレ版）。
/// </summary>
public sealed class LocalArchiveStorage(
    IOptions<LogArchiveSettings> logArchiveOptions,
    ILogger<LocalArchiveStorage> logger) : IArchiveStorage
{
    /// <inheritdoc />
    public string StorageMode => LogArchiveStorageModes.Local;

    /// <inheritdoc />
    public string DescribeLocation() => logArchiveOptions.Value.Local.ExportPath;

    /// <inheritdoc />
    public async Task<string> SaveAsync(string fileName, string csvContent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var directory = logArchiveOptions.Value.Local.ExportPath;
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, fileName);

        // Excel で直接開けるよう BOM 付き UTF-8 で出力する
        await File.WriteAllTextAsync(filePath, csvContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("ログアーカイブCSVを出力しました: {FilePath}", filePath);
        return filePath;
    }
}
