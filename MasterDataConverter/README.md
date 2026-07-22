# MasterDataConverter

Google スプレッドシート上のマスターデータを取得し、ゲーム向けの成果物（SQLite / YAML / JSON、C# コード）へ変換する CLI ツールです。

## 必要条件

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Google Cloud のサービスアカウント認証情報（`key.json`）
- 対象スプレッドシートへの読み取り権限（サービスアカウントを共有）

## セットアップ

1. リポジトリをクローンする
2. Google Cloud でサービスアカウントを作成し、Sheets API を有効化する
3. サービスアカウントの JSON キーを `MasterDataConverter/key.json` に配置する（git 管理対象外）
4. 対象スプレッドシートをサービスアカウントのメールアドレスへ「閲覧者」以上で共有する

```bash
cd MasterDataConverter
dotnet restore
```

## 実行

```bash
dotnet run -- [options]
```

例:

```bash
# すべて出力（Data + Schema + Enum）、SQLite
dotnet run -- -t db -m All

# スキーマと C# コードのみ
dotnet run -- -m Schema

# Enum 定義のみ
dotnet run -- -m Enum

# JSON でデータ出力
dotnet run -- -t json -m Data -o ./output
```

## オプション

| オプション | 説明 | デフォルト |
|-----------|------|-----------|
| `-s` | スプレッドシート ID | （要指定） |
| `-o` | 出力ディレクトリ | `../../../output` |
| `-k` | サービスアカウントキーのパス | `../../../key.json` |
| `-t` | データ出力形式（`db` / `yaml` / `json`） | `db` |
| `-m` | モード（`Data` / `Schema` / `Enum` / `All`） | `All` |
| `-v` | 対象バージョン（例: `1.0.0`） | `1.0.0` |
| `-h` | ヘルプ表示 | — |

### モード

- **Data** … マスターデータを `db` / `yaml` / `json` で出力
- **Schema** … スキーマ YAML と C# クラスを出力
- **Enum** … `$Reference` シートから Enum 定義（`MasterDataDefine.cs`）を出力
- **All** … 上記すべて

## 出力物

実行時に出力ディレクトリを一度削除してから再生成します。

| ファイル | 内容 |
|---------|------|
| `master.db` / `master.bytes` | SQLite（`-t db`） |
| `master.yaml` | YAML データ（`-t yaml`） |
| `master.json` | JSON データ（`-t json`） |
| `{MasterName}.yaml` | スキーマ YAML |
| `{MasterName}.cs` | スキーマから生成した C# クラス |
| `MasterDataDefine.cs` | Enum 定義 |

## プロジェクト構成

| ファイル | 役割 |
|---------|------|
| `main.cs` | エントリポイント・引数解析・変換フロー |
| `GoogleAuthService.cs` | サービスアカウント認証 |
| `GoogleSpreadSheetService.cs` | スプレッドシート読み取り |
| `MasterDataSqliteService.cs` | SQLite 出力 |
| `MasterDataCodeGenerator.cs` | C# / Enum コード生成 |

## 注意

- `key.json` / `credentials.json` などの認証情報はリポジトリに含めないでください
- `output/` は生成物のため git 管理対象外です
