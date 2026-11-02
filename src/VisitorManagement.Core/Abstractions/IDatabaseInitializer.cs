namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// データベースの初期化（設計書 19.2節）。
/// デモ版は初回起動時にアプリがスキーマ作成とシードデータ投入を自動実行する。
/// 商用構成では導入手順（設計書 13.1節・13.2節）でスキーマを作成するため使用しない。
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>スキーマが作成済みかを判定する</summary>
    Task<bool> IsSchemaCreatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 初期スキーマを作成する（冪等）。既に作成済みのテーブルは変更しない。
    /// </summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
}
