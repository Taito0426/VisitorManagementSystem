using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Data.AzureSql;

/// <summary>
/// ログアーカイブCSVを Azure Blob Storage へ保管する（設計書 10.4節・16章 確認事項No.5。クラウド版）。
/// </summary>
public sealed class AzureBlobArchiveStorage(
    IOptions<LogArchiveSettings> logArchiveOptions,
    ILogger<AzureBlobArchiveStorage> logger) : IArchiveStorage
{
    /// <inheritdoc />
    public string StorageMode => LogArchiveStorageModes.AzureBlob;

    /// <inheritdoc />
    public string DescribeLocation()
        => $"Azure Blob Storage / コンテナー: {logArchiveOptions.Value.AzureBlob.ContainerName}";

    /// <inheritdoc />
    public async Task<string> SaveAsync(string fileName, string csvContent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var settings = logArchiveOptions.Value.AzureBlob;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException(
                "Azure Blob Storage の接続文字列（LogArchive:AzureBlob:ConnectionString）が設定されていません。");
        }

        var containerClient = new BlobContainerClient(settings.ConnectionString, settings.ContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var blobClient = containerClient.GetBlobClient(fileName);

        // Excel で直接開けるよう BOM 付き UTF-8 で出力する
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csvContent);
        using var stream = new MemoryStream(bytes);

        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "text/csv; charset=utf-8" } },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("ログアーカイブCSVを Azure Blob Storage へ出力しました: {BlobUri}", blobClient.Uri);
        return blobClient.Uri.ToString();
    }
}
