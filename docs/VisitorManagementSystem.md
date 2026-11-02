# CLAUDE.md — 来館者記録管理システム 実装指示書

このファイルは、詳細設計書「来館者記録管理システム_詳細設計書.docx」（版数2.7）をもとに、
Claude Codeが本システムを実装する際に従うべき方針・仕様を抽出したものである。

**設計の正本（Source of Truth）は同梱の`来館者記録管理システム_詳細設計書.docx`である。**
本ファイルと元の設計書に矛盾がある場合は、設計書の記載を優先し、疑問点があれば実装を進める前にユーザーへ確認すること。
本ファイルはリポジトリのルートに配置し、Claude Codeがセッション開始時に自動的に読み込む前提で作成している。

---

## 0. 最優先事項：実装しないこと（スコープ外）

詳細設計書 **14章「将来拡張」** に記載の以下3項目は、**今回の実装対象に含めない**。
着手前の設計段階では拡張の余地を残す（インターフェースの拡張性は損なわない）が、
実装・テスト・ドキュメントのいずれにおいても、これらの機能を作り込まないこと。

| 項目 | 内容 | 対応方針 |
|---|---|---|
| Entra ID認証対応 | Azure SQL DatabaseにおけるEntra ID（旧Azure AD）認証への対応 | 実装しない。認証はSQL認証（ユーザーID・パスワード）のみとする（11.1節）。`IDbConnectionFactory`の実装追加で将来対応可能な設計にはなっているが、Entra ID用の実装クラスは作成しない。 |
| Teams通知連携 | 入館・退館時にMicrosoft Teamsへ通知を送信する機能 | 実装しない。通知はメール送信（5章）のみとする。 |
| 社員証（ICカード）連携 | QRコードに加え、社員証ICカードによる入退館読取への対応 | 実装しない。入退館の読取手段はQRコード（キーボードエミュレーション方式のQRリーダー）のみとする（8.3節）。 |

上記以外の1〜13章・15〜19章の内容はすべて実装対象である。
なお「複数拠点対応」は当初14章の将来拡張案だったが、発注者様回答により中核機能へ格上げ済みであり（2.3節）、実装対象に含まれる。誤って対象外としないこと。

---

## 1. システム概要

- 製品名: 来館者記録管理システム（旧名称「入退館管理システム」）。中小企業向けパッケージ製品。
- 目的: 来館者の事前予約受付、QRコードによる入館・退館受付、履歴管理、各種マスタ管理を一気通貫で実現する。
- 最大の特徴: 顧客の運用環境に応じて以下2種類のDB構成を、**アプリケーションコードの変更なく設定ファイルの切替のみ**で選択できること。
  - パターンA: SQL Server Express（社内オンプレミス運用・追加ライセンス費用なし）
  - パターンB: Azure SQL Database（クラウド運用・複数拠点/リモートアクセス対応）

### 提供形態・販売プラン（2.2節・2.3節）

プランは機能差分ではなく、対応する拠点数・受付端末数の規模で区分する。**全プラン共通で本書記載の全機能を提供し、プランによる機能制限は設けない。**

| プラン | 対応拠点・端末規模 |
|---|---|
| Entry | 1拠点・1端末仕様＋保守 |
| Standard | 2〜3拠点・複数端末仕様＋保守（拠点マスタSitesによる横断管理） |
| Professional | 4拠点以上・オーダーメイドカスタマイズ＋保守 |

---

## 2. 技術スタック（版数2.6で.NET 10に統一済み。現行版数は2.7）

| 区分 | 内容 |
|---|---|
| IDE | Visual Studio 2026 |
| 管理アプリ | .NET 10（`net10.0-windows`、WinForms） |
| 受付Web/API | ASP.NET Core 10（`net10.0`） |
| 共通ライブラリ | .NET 10（`net10.0`）。互換レイヤー（.NET Standard等）を挟まず、WinForms・ASP.NET Coreの双方から直接参照する |
| DB | SQL Server Express（オンプレ版） / Azure SQL Database（クラウド版）。開発時は両方の環境を用意して検証する |
| 設定ファイル | appsettings.json（WinForms・ASP.NET Core共通。`Microsoft.Extensions.Configuration`のOptionsパターンでバインド。App.configやNewtonsoft.Jsonは使用しない） |

### 主要ライブラリ（12.2節）

| ライブラリ | 用途 |
|---|---|
| Dapper | 軽量ORM、SQL実行（Repository実装で使用、両DB方式で共通） |
| Microsoft.Data.SqlClient | SQL Server接続ドライバ（SQL Server Express / Azure SQL Database双方対応） |
| Polly | リトライ・耐障害性（`AzureSqlRetryPolicy`の実装に使用） |
| SendGrid（NuGetパッケージ） | メール送信（クラウド版既定） |
| MailKit または System.Net.Mail | メール送信（SMTP方式） |
| QRCoder | QRコード画像生成 |
| Microsoft.Extensions.Configuration（Json／Binder） | appsettings.jsonの読込・`DatabaseSettings`等へのバインド |
| b-PAC SDK（ブラザー工業提供） | 感熱式ラベルプリンタ（QL-800想定）への印刷制御 |
| xUnit | 単体テスト |

### 開発PC・配布上の注意（13.1節）

管理アプリ（WinForms）は.NET 10で動作するため、.NET Framework 4.8と異なりOS標準搭載のランタイムは利用できない。
管理PCへの.NET 10 Desktopランタイムの事前インストール、または自己完結型（self-contained）発行のどちらで配布するかを、実装時またはプロジェクト設定時に決定すること（未確定の場合はユーザーに確認する）。

---

## 3. アーキテクチャ：DB切替設計（4章、本設計の中核）

### 3.1 設計方針

- **T-SQL互換性の活用**: SQL Server ExpressとAzure SQL Databaseは同一のT-SQL体系を共有するため、SQL文自体は共通化する。SQL Server Agent・クロスDBクエリ・CLR統合など非互換機能は使用しないこと（4.5節）。
- **差分の局所化**: 相違点（接続文字列、リトライ方針、バックアップ運用）は`ConnectionFactory` / `RetryPolicy` / `BackupService`の3種のみに集約する。それ以外のRepository層・業務ロジック層は完全共通コードとすること。
- **設定駆動**: `appsettings.json`の`Database:Provider`（`"SqlExpress"` | `"AzureSql"`）でDB方式を決定する。ビルドの出し分けは行わず、同一バイナリで両対応する。
- **認証方式**: SQL Server Express / Azure SQL Databaseともに SQL認証（ユーザーID・パスワード）に統一する（Entra ID認証は実装しない。0章参照）。

### 3.2 プロジェクト構成（4.3節）

| プロジェクト | 対象フレームワーク | 役割 |
|---|---|---|
| VisitorManagement.Core | .NET 10（net10.0） | エンティティ、DTO、Repository/MailSenderのインターフェース定義 |
| VisitorManagement.Infrastructure.Data | .NET 10（net10.0） | Dapperを用いたRepository実装。`IDbConnectionFactory`経由でのみ接続取得 |
| VisitorManagement.Infrastructure.Data.SqlExpress | .NET 10（net10.0） | `SqlExpressConnectionFactory`、`NoRetryPolicy`、`SqlExpressBackupService` |
| VisitorManagement.Infrastructure.Data.AzureSql | .NET 10（net10.0） | `AzureSqlConnectionFactory`、`AzureSqlRetryPolicy`（Polly使用）、`AzureSqlBackupService` |
| VisitorManagement.Infrastructure.Mail | .NET 10（net10.0） | `SendGridMailSender`、`SmtpMailSender` |
| VisitorManagement.WinFormsApp | .NET 10（net10.0-windows） | 管理PC向けアプリケーション本体 |
| VisitorManagement.WebReception | ASP.NET Core 10（net10.0） | 受付Web/APIアプリケーション本体 |

### 3.3 主要インターフェース定義（4.3.1節・そのまま実装すること）

```csharp
// DB接続ファクトリ（DB方式ごとに実装差し替え）
public interface IDbConnectionFactory
{
    IDbConnection CreateOpenConnection();
    string ProviderName { get; } // "SqlExpress" or "AzureSql"（デモ版もSqlExpressをそのまま使用する。9章参照）
}

// リトライ方針（Azure SQLの一時的な接続断/スロットリングに対応）
public interface IDbRetryPolicy
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation);
}

// 業務エンティティのRepository契約（DB方式に依存しない）
public interface IVisitorRepository
{
    Task<Visitor> GetByIdAsync(Guid visitorId);
    Task<Guid> CreateAsync(Visitor visitor);
    Task UpdateStatusAsync(Guid visitorId, VisitorStatus status);
    Task<IReadOnlyList<Visitor>> SearchAsync(VisitorSearchCondition condition);
}

public interface IQrTokenRepository { /* 発行・検証・使用回数更新 */ }
public interface IAccessLogRepository { /* 入退館履歴登録・照会 */ }
public interface IMailHistoryRepository { /* 送信履歴登録・照会 */ }
```

起動時のDI登録は、`Database:Provider`の値に応じて`IDbConnectionFactory` / `IDbRetryPolicy` / `IBackupService`の実装を切り替える。業務ロジック層（Repository実装・サービス層・画面）はインターフェースのみに依存し、Provider変更時の改修が発生しない構成にすること（ASP.NET Core・WinFormsで共通ロジック）。

### 3.4 appsettings.json（4.4節・付録B、実装のベースにする完全版）

```json
{
  "Database": {
    "Provider": "SqlExpress",   // "SqlExpress" | "AzureSql"
    "ConnectionStrings": {
      "SqlExpress": "Server=.\\SQLEXPRESS;Database=VisitorMgmt;User Id=svc_visitor;Password=***;TrustServerCertificate=True;Encrypt=False;",
      "AzureSql": "Server=tcp:contoso-visitormgmt.database.windows.net,1433;Initial Catalog=VisitorMgmt;User ID=svc_visitor;Password=***;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    },
    "RetryPolicy": {
      "SqlExpress": { "MaxRetryCount": 1, "MaxRetryDelaySeconds": 1 },
      "AzureSql":   { "MaxRetryCount": 5, "MaxRetryDelaySeconds": 10 }
    }
  },
  "Printing": {
    "Provider": "Ql800"   // "Ql800" | "PdfPreview"（デモ版。7章参照）
  },
  "Demo": {
    "Enabled": false,
    "ConnectionString": "Server=.\\SQLEXPRESS;Database=VisitorMgmtDemo;User Id=svc_visitor;Password=***;TrustServerCertificate=True;Encrypt=False;",
    "SeedOnFirstRun": true,
    "AllowResetFromAdminMenu": true
  },
  "Mail": {
    "Provider": "SendGrid",
    "SendGrid": { "ApiKey": "***", "FromAddress": "no-reply@example.com", "FromName": "来館受付システム" },
    "Smtp": { "Host": "smtp.office365.com", "Port": 587, "EnableSsl": true, "User": "no-reply@example.com", "Password": "***" }
  },
  "Backup": {
    "SqlExpress": { "DailyTime": "23:00", "RetentionDays": 30, "Path": "D:\\Backup\\" },
    "AzureSql":   { "PitrRetentionDays": 7, "LongTermRetentionEnabled": false }
  },
  "LogArchive": {
    "DbRetentionMonths": 3,
    "ExportOnBusinessDayOfMonth": 1,
    "ExportFormat": "csv",
    "StorageMode": "Local",
    "Local": { "ExportPath": "D:\\LogArchive\\" },
    "AzureBlob": { "ConnectionString": "***", "ContainerName": "log-archive" },
    "TargetTables": [ "AccessLogs", "MailHistory" ]
  }
}
```

`Printing`・`Demo`セクションはデモ版（本書7章）専用であり、商用構成（Entry/Standard/Professional）では既定値（`Ql800`／`Demo.Enabled:false`）のまま使用しないこと。`Demo.Enabled:true`でも`Database:Provider`は`"SqlExpress"`のまま変更せず、接続先のみ`Demo.ConnectionString`に切り替える。

### 3.5 接続文字列サンプル（付録A）

| 用途 | 接続文字列 |
|---|---|
| SQL Server Express（本番） | `Server=.\SQLEXPRESS;Database=VisitorMgmt;User Id=svc_visitor;Password=***;TrustServerCertificate=True;Encrypt=False;` |
| Azure SQL Database | `Server=tcp:<サーバー名>.database.windows.net,1433;Initial Catalog=VisitorMgmt;User ID=svc_visitor;Password=***;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;` |
| SQL Server Express（デモ版用DB） | `Server=.\SQLEXPRESS;Database=VisitorMgmtDemo;User Id=svc_visitor;Password=***;TrustServerCertificate=True;Encrypt=False;`（本番用と同一インスタンス、DB名のみ分離） |

---

## 4. 確定済みの重要な設計判断（実装時に必ず反映すること）

発注者様Q&A（15〜18章）で確定した仕様のうち、実装を誤りやすい重要なものを列挙する。

1. **QRコードは代表者1名にのみ発行する**（グループ単位読取）。1予約に複数名（代表者＋同行者）を登録できるが、QRトークンは代表者のVisitors行にのみ1件発行し、同行者は個別QRを持たない。代表者QRの読取で、同一`ReservationGroupId`の全員のStatusを一括更新する（6.1〜6.4節、9.2.3〜9.2.4節）。
2. **QRの読取ロジックは「1回目=入館、2回目=退館、3回目以降=エラー」**。UsedCountで管理する（6.4節・7.2節）。
3. **グループはぐれ対応は退館側のみ**。代表者と同行者が別タイミングで退館する場合は、受付担当者（受付・管理者ロールのみ、一般ロールは不可）が手動で個人単位に退館処理できる機能（6.4.1節）を実装する。**入館側の個別対応機能は作らない**。別タイミングで到着する同行者は、新たに当日受付として個別登録する運用とする（18章確認事項No.1）。
4. **当日受付（walk-in）はグループ登録に対応する**。事前予約と同じロジックを再利用し、特別な例外フローは設けない（6.6節）。
5. **メール送信先は代表者（招待者）と予約登録者の2者のみ**。同行者個人へは送信しない。予約登録者宛にも代表者と同一のQRコードを添付し、紛失時の転送・再送信の起点とする（5.2節）。
6. **ラベルサイズはプリセット1（幅60mm×高さ110mm）のみ標準搭載**。`Sites.LabelSizeType`で管理し、プリセット2・3は導入時ヒアリングで個別設定する空の設定項目として用意する（9.2.1節）。
7. **操作ユーザーのRoleType・SiteIdは登録時必須**（NULL状態を発生させない設計。9.2.2節）。
8. **多言語対応（日英併記）の対象は「受付Web入力画面（エラーメッセージ含む）」と「来館案内メールの定型文」のみ**。QRコード読取結果画面・WinForms管理画面は日本語のみ（2.5節・6.12節・8.3節）。
9. **翻訳文言はリソースファイルに固定実装**し、管理画面からの編集項目にはしない。顧客ごとのカスタマイズ対象箇所には、行頭にコメント `// ★レイアウト調整` を付与し検索しやすくすること（6.12節）。
10. **RBACは3ロール（管理者／受付／一般）**。履歴照会の閲覧範囲は、一般=自分が登録した予約のみ、受付=自身の所属拠点の全件、管理者=全拠点・全件（11.6節、権限マトリクスは6章参照）。
    **※改修（設計書からの変更）**: 一般ロールも自分が来館者を招く立場となるため、来館予約の**登録・編集・キャンセル**を許可する。対象は自分が登録した予約に限り（閲覧範囲と同一）、他人の予約は一覧に表示されず操作もできない。履歴を消す**削除**と、送信をやり直す**メール再送信**、および**当日受付登録・入退館受付・手動退館処理**は従来どおり管理者・受付のみとする。
11. **DB方式・メール方式の切替は非稼働日バッチ運用**とし、稼働中の無停止切替（ゼロダウンタイム）は実装しないこと（13.3節）。
12. **本システムは新規導入であり、既存データの移行は発生しない**（15章確認事項No.11）。

---

## 5. データベース設計（9章）

### 5.1 ER概要

主要エンティティ: `Sites`（拠点マスタ）→ `Operators`（操作ユーザー）／`Visitors`（来館予約）→ `QRTokens`（QR発行、代表者行のみ）／`AccessLogs`（入退館履歴）／`MailHistory`（メール送信履歴）。加えて`Companies`（会社名マスタ）、`AuditLog`（操作監査ログ）。

### 5.2 テーブル定義

#### Sites（拠点マスタ、9.2.1節）
| 列 | 型 | PK | NULL | 既定値 | 備考 |
|---|---|---|---|---|---|
| SiteId | int | ○ | 不可 | IDENTITY(1,1) | 主キー |
| SiteName | nvarchar(100) | | 不可 | | 拠点名 |
| Address | nvarchar(200) | | 可 | | 住所 |
| LabelCompanyName | nvarchar(100) | | 可 | | ラベル印刷用表示社名 |
| LabelLogoImage | varbinary(max) | | 可 | | ラベル印刷用ロゴ画像 |
| LabelSizeType | tinyint | | 不可 | 1 | 1:幅60mm×高さ110mm（既定値）。2・3は導入時個別設定 |
| IsActive | bit | | 不可 | 1 | false:廃止拠点 |
| CreatedAt | datetime2(0) | | 不可 | sysutcdatetime() | 登録日時 |

#### Operators（操作ユーザー、9.2.2節）
| 列 | 型 | PK | NULL | 既定値 | 備考 |
|---|---|---|---|---|---|
| OperatorId | int | ○ | 不可 | IDENTITY(1,1) | 主キー |
| LoginId | nvarchar(50) | | 不可 | | 一意制約あり |
| PasswordHash | nvarchar(256) | | 不可 | | ソルト付きハッシュ（PBKDF2/BCrypt等） |
| DisplayName | nvarchar(50) | | 不可 | | 表示名 |
| Email | nvarchar(256) | | 不可 | | 予約登録者宛メール送信先。必須 |
| RoleType | tinyint | | 不可 | 3 | 1:管理者 2:受付 3:一般。**登録時必須** |
| SiteId | int | | 不可 | | 外部キー→Sites。**登録時必須（NULL不可）** |
| IsActive | bit | | 不可 | 1 | false:無効化 |
| CreatedAt | datetime2(0) | | 不可 | sysutcdatetime() | |

#### Visitors（来館予約、9.2.3節）
| 列 | 型 | PK | NULL | 既定値 | 備考 |
|---|---|---|---|---|---|
| VisitorId | uniqueidentifier | ○ | 不可 | newid() | 主キー |
| ReservationGroupId | uniqueidentifier | | 不可 | newid() | 代表者・同行者を束ねるグループID |
| IsRepresentative | bit | | 不可 | 1 | 1:代表者 0:同行者 |
| SiteId | int | | 不可 | | 外部キー→Sites |
| CompanyName | nvarchar(100) | | 可 | | |
| VisitorName | nvarchar(50) | | 不可 | | |
| VisitorNameKana | nvarchar(50) | | 可 | | |
| VisitorEmail | nvarchar(256) | | 可 | | 代表者は必須運用。同行者行は未使用。当日受付の代表者は任意 |
| PhoneNumber | nvarchar(20) | | 可 | | |
| VisitDate | date | | 不可 | | |
| VisitTimeFrom | time(0) | | 不可 | | |
| VisitTimeTo | time(0) | | 可 | | |
| HostEmployeeName | nvarchar(50) | | 可 | | |
| PurposeOfVisit | nvarchar(200) | | 可 | | |
| ReservationType | tinyint | | 不可 | 1 | 1:事前予約 2:当日受付 |
| CreatedByOperatorId | int | | 不可 | | 外部キー→Operators（予約登録者） |
| Status | tinyint | | 不可 | 0 | 0:未入館 1:入館中 2:退館済 9:キャンセル |
| CreatedAt / UpdatedAt | datetime2(0) | | 不可 | sysutcdatetime() | UTC |
| RowVersion | rowversion | | 不可 | | 楽観的排他制御 |

#### QRTokens（QR発行、9.2.4節）※1予約につき1行、代表者行にのみ発行
| 列 | 型 | PK | NULL | 既定値 | 備考 |
|---|---|---|---|---|---|
| TokenId | uniqueidentifier | ○ | 不可 | newid() | |
| VisitorId | uniqueidentifier | | 不可 | | 外部キー→Visitors（代表者行のみ） |
| TokenValue | uniqueidentifier | | 不可 | newid() | QRコードに埋め込む値。一意制約あり |
| IssuedAt | datetime2(0) | | 不可 | sysutcdatetime() | |
| ExpiresAt | datetime2(0) | | 不可 | | 予約時刻-30分〜+2時間、当日受付は受付時刻基準 |
| UsedCount | tinyint | | 不可 | 0 | 読取回数（0/1/2） |
| ReissueCount | tinyint | | 不可 | 0 | メール再送信による再発行回数 |
| IsValid | bit | | 不可 | 1 | false:キャンセル等で無効化 |

#### AccessLogs（入退館履歴、9.2.5節）
| 列 | 型 | PK | NULL | 既定値 | 備考 |
|---|---|---|---|---|---|
| LogId | bigint | ○ | 不可 | IDENTITY(1,1) | |
| VisitorId | uniqueidentifier | | 不可 | | 外部キー→Visitors |
| SiteId | int | | 不可 | | 外部キー→Sites |
| TokenId | uniqueidentifier | | 可 | | 外部キー→QRTokens。手動受付時はNULL許容 |
| AccessType | tinyint | | 不可 | | 1:入館 2:退館 9:エラー |
| AccessDateTime | datetime2(0) | | 不可 | sysutcdatetime() | |
| DeviceInfo | nvarchar(100) | | 可 | | |
| OperatorNote | nvarchar(200) | | 可 | | 手動受付時の備考等 |

代表者QRの1回の読取につき、グループ内の来館者ごとにAccessLogs行を1件ずつ記録すること（TokenIdは代表者の1トークンを共通参照）。

#### MailHistory（メール送信履歴、9.2.6節）
| 列 | 型 | PK | NULL | 既定値 | 備考 |
|---|---|---|---|---|---|
| MailId | bigint | ○ | 不可 | IDENTITY(1,1) | |
| VisitorId | uniqueidentifier | | 不可 | | 外部キー→Visitors |
| MailType | tinyint | | 不可 | | 1:来館案内 2:リマインド 3:キャンセル通知 4:再送信 |
| RecipientType | tinyint | | 不可 | | 1:招待者(代表者のみ) 2:予約登録者(Operators.Email) |
| Provider | tinyint | | 不可 | | 1:SendGrid 2:SMTP |
| SentAt | datetime2(0) | | 不可 | sysutcdatetime() | |
| Status | tinyint | | 不可 | | 0:失敗 1:成功 |
| ErrorMessage | nvarchar(500) | | 可 | | |

#### Companies（会社名マスタ、9.2.7節）
| 列 | 型 | PK | NULL | 既定値 | 備考 |
|---|---|---|---|---|---|
| CompanyId | int | ○ | 不可 | IDENTITY(1,1) | |
| CompanyName | nvarchar(100) | | 不可 | | |
| Address | nvarchar(200) | | 可 | | |
| Tel | nvarchar(20) | | 可 | | |

#### AuditLog（操作監査ログ、9.2.8節）※DB切替設定変更専用テーブル（SystemConfigHistory）は廃止済み
| 列 | 型 | PK | NULL | 既定値 | 備考 |
|---|---|---|---|---|---|
| AuditLogId | bigint | ○ | 不可 | IDENTITY(1,1) | |
| TargetEntity | nvarchar(50) | | 不可 | | 例: Sites, Companies, Operators, MailSettings |
| TargetId | nvarchar(50) | | 可 | | |
| ChangeType | tinyint | | 不可 | | 1:登録 2:更新 3:削除 |
| OldValue / NewValue | nvarchar(max) | | 可 | | JSON等 |
| ChangedAt | datetime2(0) | | 不可 | sysutcdatetime() | |
| ChangedByOperatorId | int | | 不可 | | 外部キー→Operators |

---

## 6. 状態遷移（7章）

### 予約ステータス（Visitors.Status）
`未入館(0)` → `入館中(1)` → `退館済(2)`。`キャンセル(9)`は未入館・入館中から管理者操作で遷移可能（退館済からのキャンセルは不可）。当日受付も登録直後は「未入館」から開始し、以降のフローは事前予約と完全共通。

### QRTokensの状態（UsedCount）
- `IsValid=true`かつ`ExpiresAt`未到来: 読取可能
- `UsedCount=0`: 読取で入館処理、1に更新
- `UsedCount=1`: 読取で退館処理、2に更新
- `UsedCount>=2`または無効/期限切れ: エラー表示のみ、AccessLogsへエラー記録のみ

---

## 7. デモシステム仕様（19章）

営業デモ用に、以下を実装すること。

- **DBはSqlExpressプロバイダをそのまま使用**する（デモ専用の`IDbConnectionFactory`実装は追加しない）。接続先データベース名のみ`VisitorMgmtDemo`として本番用（`VisitorMgmt`）と分離する。デモ固有の振る舞い（シード投入・リセット機能の有効化）は`Demo:Enabled`設定フラグで制御する（本番構成では`false`固定）。
- **Web受付アプリは自己完結型（self-contained）発行**し、組み込みKestrelのみで起動できるようにする（IIS/Azure App Serviceの設定は不要）。
- **サンプルデータの自動シード**（初回起動時、冪等処理）: 拠点2件（例:「本社」「大阪支社」）、会社名3〜5件、権限ロールごとの操作ユーザー3件（`demo-admin`/`demo-reception`/`demo-general`）、ステータス違い・複数名同時来館を含む来館予約サンプル数件。
- **サンプルデータ初期化機能**をWinForms管理画面に設ける（管理者ロール専用、ワンクリックでシード状態へリセット）。
- **`ILabelPrinter`インターフェース**を新設し、本番用`Ql800LabelPrinter`（b-PAC SDK経由で実機印刷）とデモ用`DemoPdfLabelPrinter`（A4 PDFとして生成しプレビュー表示）を実装する。設定`Printing:Provider`（`"Ql800"` | `"PdfPreview"`）で切替える。

```csharp
// ラベル印刷の共通契約（本番/デモで実装を切り替える）
public interface ILabelPrinter
{
    // 本番: Ql800LabelPrinter（b-PAC SDK経由で実機に印刷）
    // デモ: DemoPdfLabelPrinter（A4 PDFとして生成しプレビュー表示）
    Task PrintQrLabelAsync(Visitor visitor, byte[] qrCodePng, Site site);
}
```

- PDFレイアウトは、A4用紙内に`Sites.LabelSizeType`のラベル実寸を点線枠で示し、本番と同一のQRコード・社名・ロゴのサンプル画像を配置する。1ページに複数プリセットを並べて比較確認できるようにする。
- デモ版はEntry/Standard/Professionalの商用構成とは別プロファイルであり、そのままの構成では商用提供しないこと。メール送信・ラベル印刷は実際には行わない（メールはメモリ内モック`DemoMailSender`、印刷はPDFプレビューのみ）。

---

## 8. 機能仕様サマリ（6章）

以下は各機能の実装ポイント。詳細は設計書6章の該当節を参照すること。

- **6.1 来館予約機能**: WinForms管理画面から登録。代表者1名＋任意で複数同行者。`ReservationGroupId`で束ねる。登録完了時にQR自動発行＋メール送信。
- **6.2 QR発行機能**: 代表者行にのみ1件発行。有効期限は予約時刻の30分前〜2時間後。
- **6.3 来館案内メール送信**: 代表者・予約登録者へ送信。同行者の氏名一覧を本文に記載。日英併記。
- **6.4 入退館受付機能**: グループ単位読取（1回目=入館、2回目=退館、3回目以降=エラー）。
- **6.4.1 手動退館処理機能**: 受付・管理者ロールのみ。個人単位で退館ステータスを手動更新。氏名・来館日での検索にも対応。
- **6.5 受付時間ルール**: QR有効時間帯外の読取はエラーとしAccessLogsに記録。
- **6.6 当日受付登録機能**: グループ登録対応。メールアドレスは任意。登録と同時にQR発行・ラベル即時印刷。別タイミング到着の同行者は新規の当日受付として個別登録（グループへの追加はしない）。
- **6.7 メール再送信機能**: WinForms予約管理画面から。招待者・予約登録者双方へ再送信。当日受付分は対象外。
- **6.8 履歴管理機能**: AccessLogs・MailHistoryを検索・CSV出力。DB保持は直近3か月、それ以前はアーカイブ済CSVへの案内表示。
- **6.9 マスタ管理機能**: 会社名マスタ等の登録・編集・削除。
- **6.10 操作ユーザー管理機能**: 3ロール（管理者/受付/一般）。ロール・所属拠点は登録時必須。拠点異動はマスタ画面から都度変更。
- **6.11 拠点管理機能**: Standard以上で複数拠点管理。Entryプランは拠点選択UIを非表示。
- **6.12 多言語対応機能**: 受付Web入力画面とメール定型文のみ日英併記。翻訳はリソースファイルに固定実装し、`// ★レイアウト調整`コメントでカスタマイズ箇所を明示。
- **6.13 システム設定管理機能**: 管理者専用。DB方式・メール方式の現在値を参照のみ（変更は設定ファイル編集＋再起動）。

---

## 9. 画面一覧（8章）

| No. | 画面名 | アプリケーション | 概要 |
|---|---|---|---|
| 1 | ログイン画面 | WinForms | 操作ユーザーID・パスワード認証 |
| 2 | 来館予約登録・編集画面 | WinForms | 代表者・同行者登録、QR再発行、メール再送信 |
| 3 | 履歴照会画面 | WinForms | 入退館履歴・メール送信履歴の検索/CSV出力 |
| 4 | マスタ管理画面 | WinForms | 会社名等マスタの登録・編集・削除 |
| 5 | 拠点マスタ管理画面 | WinForms | 拠点登録・編集、ラベル印刷用社名・ロゴ設定 |
| 6 | 操作ユーザー管理画面 | WinForms | 操作ユーザーの登録・編集・無効化、ロール・拠点設定 |
| 7 | システム設定確認画面 | WinForms | DB方式・メール方式の現在値表示（読取専用） |
| 8 | 受付画面 | ASP.NET Core（Web） | QRコード読取・入退館受付・結果表示（日本語のみ） |
| 9 | 当日受付登録画面 | ASP.NET Core（Web） | 予約なし来館者の当日登録・QR即時発行・ラベル印刷（日英併記） |

QRリーダーはキーボードエミュレーション方式（HIDモード）を前提とし、読取結果は即座に`/api/access/scan`へ送信すること（8.3節）。

---

## 10. セキュリティ・権限（11章）

- DB接続はSQL認証のみ、最小権限のサービスアカウントを使用（sysadmin等の強い権限は付与しない）。
- 操作ユーザー認証は`LoginId`/`PasswordHash`（PBKDF2/BCrypt等のソルト付きハッシュ、平文保存禁止）。
- 通信は全区間HTTPS/暗号化必須。Azure SQL Databaseは`Encrypt=True`・`TrustServerCertificate=False`必須。
- 接続文字列中のパスワード: WinForms側はWindows DPAPI（`System.Security.Cryptography.ProtectedData`のNuGetパッケージ版）で暗号化保存。ASP.NET Core側はAzure Key Vault（クラウド版）またはユーザーシークレット/環境変数（オンプレ版）。
- QRトークンはGUID値で推測困難性を確保。期限切れ・無効トークンは拒否しAccessLogsにエラー記録。
- マスタ変更・運用設定変更はAuditLogに記録する。DB方式切替自体の変更監査テーブルは設けない（非稼働日バッチ運用のため）。

### RBAC権限マトリクス（11.6節）

| 機能／操作 | 管理者 | 受付 | 一般 |
|---|---|---|---|
| 来館予約の登録・編集・キャンセル | ○ | ○ | ○（自分が登録した予約のみ）※改修 |
| 来館予約の削除 | ○ | ○ | × |
| 当日受付登録・入退館受付 | ○ | ○ | × |
| 手動退館処理 | ○ | ○ | × |
| メール再送信 | ○ | ○ | × |
| 履歴照会・CSV出力 | 全拠点・全件 | 自身の所属拠点の全件 | 自分が登録した予約のみ |
| 会社名マスタ管理 | ○ | × | × |
| 拠点マスタ管理 | ○ | × | × |
| 操作ユーザー管理 | ○ | × | × |
| システム設定確認 | ○ | × | × |

---

## 11. バックアップ・運用（10章）

- SQL Server Express: Windowsタスクスケジューラ＋PowerShell/sqlcmdで毎日23:00に`BACKUP DATABASE`実行。保存先はサーバー機ローカル、保管期間30日。
- Azure SQL Database: 自動バックアップ（標準機能）をそのまま利用。追加実装不要。
- 障害復旧（DR）: 本番機＋自動バックアップ＋予備PC（本番機と同一拠点に設置）＋復旧手順書。目標復旧時間（RTO）30分以内。予備PCは平常時からOS・ミドルウェア・アプリをセットアップ済みの状態で待機させる。
- ログアーカイブ: AccessLogs・MailHistoryはDB上3か月保持。翌月第1営業日に前月分をCSV出力後、出力から3か月経過時にDBから削除。保管先はオンプレ版=ローカルディスク、クラウド版=Azure Blob Storage。

---

## 12. テスト方針（12.3節）

- 単体テスト: xUnitでRepository層・サービス層をテスト。DBアクセスはDapperのモック化、またはローカルSQL Server Express（テスト専用DB）を使用。
- DB切替結合テスト: SQL Server Express環境とAzure SQL Database環境の両方で、主要業務シナリオ（予約登録→QR発行→入館→退館→履歴照会）を検証。
- QR受付シナリオテスト: 1回目/2回目/3回目以降の読取、期限切れトークン、無効化トークン等の境界値を網羅。
- 性能テスト: Azure SQL Databaseのスロットリング時のリトライ挙動を疑似的に検証。

---

## 13. 導入手順の要点（13章）

- オンプレ版: SQL Server Expressインストール→初期スキーマ作成→サービス用SQLログイン作成→`appsettings.json`設定→バックアップ/アーカイブバッチ登録→拠点・マスタ・操作ユーザー登録→ラベルプリンタ設定。
- クラウド版: Azure SQL Database作成→Azure App Serviceプロビジョニング→`appsettings.json`設定（Key Vault参照）→自動バックアップ確認→SendGrid設定→マスタ登録→ログアーカイブバッチ設定。
- DB方式変更: 非稼働日に、移行先DB準備→データエクスポート→データインポート→`Database:Provider`切替→再起動・動作確認、の順で行う。無停止移行はサポート対象外。

---

## 14. 実装を進める上での確認方針

設計書15〜18章のQ&Aにより、実装開始時点で未確定の仕様上の疑問点は生じていない（18章末尾の記載）。
ただし実装を進める中で、設計書に明記のない詳細（画面のレイアウト細部、エラーメッセージの厳密な文言、テストデータの粒度など）に判断が必要になった場合は、**推測で進めず、いったん立ち止まってユーザーに確認すること**。特に以下は必ず確認すること。

- 0章に挙げた「実装しないこと」の境界が曖昧なケース（例: 将来拡張3項目に関連しそうな要望が来た場合）
- DB方式・メール方式・ラベルプリンタ機種など、設計書が「導入時ヒアリングにより個別設定」としている項目の具体値
- 本ファイルと設計書（docx）の間に矛盾を見つけた場合
