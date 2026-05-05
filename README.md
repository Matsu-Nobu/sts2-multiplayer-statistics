# sts2-multiplayer-statistics

Slay the Spire 2 のマルチプレイ／シングルプレイにおけるラン統計を収集・共有するためのプロジェクト。
mod でゲームから統計を収集し、バックエンドサーバに送信、ブラウザで共有 URL から閲覧できる。

```
[STS2 mod (C#/Godot)]
        │ HTTP POST（ターン終了時 / 戦闘外イベント）
        ▼
[バックエンドサーバ (Go + SQLite)]
        │ HTTP GET
        ▼
[ブラウザ / 共有URL（Svelte SPA）]
```

## 構成

```
sts2-multiplayer-statistics/
├── docs/         設計・計画ドキュメント（仕様の一次情報）
├── mod/          STS2 用 mod 本体（C#）
├── mod.tests/    mod のユニットテスト（xunit.v3）
├── backend/      Go 製 API サーバ（実装中）
├── web/          Svelte + Vite による閲覧 UI（実装予定）
└── Makefile      mod / backend / web のビルド・テスト・実行を統合
```

## 現在の状況

| コンポーネント | 状態 |
|----------------|------|
| mod（統計収集） | Phase 1.5 完了。ターン単位／戦闘単位の統計を JSONL ローカルログに出力 |
| バックエンド | 設計確定、実装着手予定 |
| WebUI | 設計確定、実装未着手 |
| デプロイ | 未着手 |

## ドキュメント

仕様や設計判断はすべて `docs/` 配下に集約している。

| ドキュメント | 内容 |
|--------------|------|
| [`docs/DESIGN.md`](docs/DESIGN.md) | 全体アーキテクチャと設計判断 |
| [`docs/STATS_DESIGN.md`](docs/STATS_DESIGN.md) | 収集している統計項目の定義（表示要件起点） |
| [`docs/API.md`](docs/API.md) | mod ⇄ backend ⇄ WebUI の HTTP API 仕様（**現在実装されているもののみ**） |
| [`docs/PHASE2_PLAN.md`](docs/PHASE2_PLAN.md) | バックエンド・WebUI 実装計画 |
| [`docs/ROADMAP.md`](docs/ROADMAP.md) | 段階的拡張計画と派生統計のアイデア |
| [`docs/OPERATIONS.md`](docs/OPERATIONS.md) | 運用方針・配布モデル |
| [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) | mod 側の初期実装計画（Phase 1 時点） |

## 開発環境

### 前提

- macOS（arm64 で開発中。クロスプラットフォーム対応はビルドフローのみで `docs/DESIGN.md` 参照）
- Slay the Spire 2 がインストール済み（Steam 版）
- .NET SDK（mod ビルド用、`brew install dotnet`）
- Go 1.22+（バックエンド用、`brew install go`）
- Node.js 20+（WebUI 用、`brew install node`）

### セットアップ

ゲームのインストールパスを `.env` に記述する。

```bash
cp .env.example .env
# .env を編集（STS2_DATA_DIR / STS2_MODS_DIR）
```

`.env.example`:

```
export STS2_DATA_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64"
export STS2_MODS_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods"
```

### よく使うコマンド（Makefile）

```bash
make all                 # mod のテスト → ビルド → インストール
make test                # mod のユニットテスト
make build               # mod のビルド
make install             # mod を STS2 の mods/ にコピー
make log                 # mod が出力する統計ログを tail -f
```

backend / web のターゲットは実装と同時に追加予定。

## 動作確認

1. `make all` で mod をビルド・インストール
2. STS2 を起動
3. 戦闘を1回プレイ
4. `make log` で `~/Library/Application Support/SlayTheSpire2/sts_stats.jsonl` が更新されているか確認

## 設計の特徴

- **単一クライアント収集**: STS2 のマルチプレイは決定論的ロックステップなので、ホスト1人の mod だけで全プレイヤーの統計を収集可能
- **イベントログ + ターン集計のハイブリッド**: 戦闘中の高頻度データは per-turn 集計、それ以外の discrete event は raw event ログに格納
- **将来の統計拡張に強い**: 新しい event_type を追加するだけで派生統計が増やせる（DB スキーマ変更不要）
- **失敗に強い**: バックエンド送信は best-effort。失敗してもゲームは止まらず JSONL ローカルログに残る
- **共有 URL**: ホストが mod を入れていれば、仲間は URL 1 つ受け取るだけで閲覧可能

## ライセンス

未定。
