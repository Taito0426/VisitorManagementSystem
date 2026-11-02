# 来館者記録管理システム

来館者の事前予約受付、QRコードによる入館・退館受付、履歴管理、各種マスタ管理を提供する
中小企業向けパッケージ製品です。

**設計の正本は `来館者記録管理システム_詳細設計書.docx`（版数2.7）です。**
本リポジトリの実装は同設計書および `VisitorManagementSystem.md`（実装指示書）に準拠しています。

---

## 最大の特徴：DB切替アーキテクチャ

顧客の運用環境に応じて、**アプリケーションコードの変更なく設定ファイルの切替のみ**で
2種類のDB構成を選択できます（設計書 4章）。

```jsonc
{ "Database": { "Provider": "SqlExpress" } }   // "SqlExpress" | "AzureSql"
```

| | SQL Server Express（オンプレ版） | Azure SQL Database（クラウド版） |
|---|---|---|
| ConnectionFactory | `SqlExpressConnectionFactory` | `AzureSqlConnectionFactory` |
| RetryPolicy | `NoRetryPolicy` | `AzureSqlRetryPolicy`（Polly 指数バックオフ） |
| BackupService | `SqlExpressBackupService` | `AzureSqlBackupService`（自動バックアップに委譲） |
| ArchiveStorage | `LocalArchiveStorage` | `AzureBlobArchiveStorage` |

Repository層のSQL文と業務ロジックは完全共通です。ビルドの出し分けは行わず、同一バイナリで両対応します。

---

## プロジェクト構成

| プロジェクト | TFM | 役割 |
|---|---|---|
| `VisitorManagement.Core` | net10.0 | エンティティ、DTO、インターフェース定義、業務ルール、RBAC |
| `VisitorManagement.Application` ※ | net10.0 | 業務サービス層（予約・入退館・履歴・マスタ・アーカイブ） |
| `VisitorManagement.Infrastructure.Data` | net10.0 | Dapper による Repository 実装（DB方式共通） |
| `VisitorManagement.Infrastructure.Data.SqlExpress` | net10.0 | SQL Server Express 用の接続・リトライ・バックアップ・アーカイブ |
| `VisitorManagement.Infrastructure.Data.AzureSql` | net10.0 | Azure SQL Database 用の同上 |
| `VisitorManagement.Infrastructure.Mail` | net10.0 | SendGrid / SMTP / デモ用モックのメール送信 |
| `VisitorManagement.Infrastructure.Printing` ※ | net10.0 | QRコード生成、b-PAC ラベル印刷、デモ用PDFプレビュー |
| `VisitorManagement.Composition` ※ | net10.0 | DI合成（Provider切替）、DPAPI設定保護 |
| `VisitorManagement.WinFormsApp` | net10.0-windows | 管理PC向けアプリケーション（画面1〜7） |
| `VisitorManagement.WebReception` | net10.0 | 受付Web/API（画面8〜9） |
| `VisitorManagement.BatchRunner` ※ | net10.0 | 定時バッチ（バックアップ・ログアーカイブ） |
| `VisitorManagement.Tests` | net10.0 | xUnit 単体テスト |

※ 設計書4.3節のプロジェクト一覧にない追加分です。責務の分離と実行基盤の必要性から追加しています。

---

## ディレクトリ

```
db/         初期スキーマ・SQLログイン作成スクリプト
docs/       導入手順書・デモ操作手順書・運用マニュアル
scripts/    バックアップ、タスクスケジューラ登録、設定値のDPAPI暗号化
src/        アプリケーション本体
tests/      単体テスト
```

---

## ビルドと実行

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/VisitorManagement.WebReception
```

### 自己完結型での発行（設計書 13.1節・19.2節）

```bash
dotnet publish src/VisitorManagement.WinFormsApp -p:PublishProfile=SelfContained -c Release
```

```bash
dotnet publish src/VisitorManagement.WebReception -p:PublishProfile=SelfContained -c Release
```

---

## セットアップ

詳細は [docs/導入手順書.md](docs/導入手順書.md) を参照してください。

```bash
sqlcmd -S .\SQLEXPRESS -E -I -i db/00_CreateDatabase_SqlExpress.sql
```

> **`sqlcmd` には必ず `-I` を付けてください。** `dbo.Visitors` にフィルター選択インデックスがあるため、
> QUOTED_IDENTIFIER が OFF だと同テーブルへの DDL/DML がエラー 1934 で失敗します。

---

## 実装上の重要な仕様（設計書 Q&Aで確定した内容）

1. **QRコードは代表者1名にのみ発行**（グループ単位読取）。同行者は個別QRを持たず、
   代表者QRの読取でグループ全員のステータスが一括更新される
2. **読取ロジックは 1回目＝入館 / 2回目＝退館 / 3回目以降＝エラー**（`QRTokens.UsedCount` で管理）
3. **グループはぐれ対応は退館側のみ**。入館側の個別対応は設けず、別タイミング到着の同行者は
   当日受付として新規登録する
4. **メール送信先は招待者（代表者）と予約登録者の2者のみ**。同行者個人へは送信しない
5. **ラベルサイズはプリセット1（幅60mm×高さ110mm）のみ標準搭載**。2・3は導入時ヒアリングで個別設定
6. **RBACは3ロール**（管理者／受付／一般）。履歴の閲覧範囲は
   一般＝自分が登録した予約のみ／受付＝自拠点の全件／管理者＝全拠点・全件。
   来館予約の登録・編集・キャンセルは一般ロールにも許可する（自分が登録した予約のみ）。
   削除・メール再送信・当日受付・入退館受付は管理者／受付のみ
7. **多言語（日英併記）の対象は受付Web入力画面とメール定型文のみ**。
   翻訳文言は固定実装し、調整箇所は `// ★レイアウト調整` コメントで検索できる
8. **DB方式・メール方式の切替は非稼働日バッチ運用**。稼働中の無停止切替は非対応

## スコープ外（設計書 14章 将来拡張）

Entra ID認証対応、Teams通知連携、社員証（ICカード）連携は実装対象外です。
