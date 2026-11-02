using System.Data;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// DB接続ファクトリ（設計書 4.3.1節。DB方式ごとに実装を差し替える）。
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>接続を開いた状態で取得する</summary>
    IDbConnection CreateOpenConnection();

    /// <summary>"SqlExpress" or "AzureSql"（デモ版もSqlExpressをそのまま使用する。設計書19章参照）</summary>
    string ProviderName { get; }
}
