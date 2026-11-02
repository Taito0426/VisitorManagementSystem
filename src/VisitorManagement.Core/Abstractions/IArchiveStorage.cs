namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// ログアーカイブCSVの保管先（設計書 10.4節・16章 確認事項No.5）。
/// オンプレ版はサーバー機のローカルディスク、クラウド版は Azure Blob Storage を使用する。
/// </summary>
public interface IArchiveStorage
{
    /// <summary>保管先の種別（"Local" | "AzureBlob"）</summary>
    string StorageMode { get; }

    /// <summary>保管場所の説明（履歴照会画面での案内表示に使用）</summary>
    string DescribeLocation();

    /// <summary>
    /// CSVファイルを保管先へ書き出す。
    /// </summary>
    /// <param name="fileName">ファイル名（1テーブル・1か月単位。例: AccessLogs_202601.csv）</param>
    /// <param name="csvContent">CSV本文</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>書き出し先のパスまたはURI</returns>
    Task<string> SaveAsync(string fileName, string csvContent, CancellationToken cancellationToken = default);
}
