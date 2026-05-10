# Docs

Slay the Spire 2 マルチプレイ統計 mod + バックエンド + WebUI のドキュメント。

## 構成

| ファイル | 内容 |
|---|---|
| [`architecture.md`](./architecture.md) | システム全体のアーキテクチャ・mod 内部実装 |
| [`api.md`](./api.md) | mod ⇄ backend ⇄ WebUI の HTTP API 契約 (event_type カタログ含む) |
| [`operations.md`](./operations.md) | サーバ運用 / 配布モデル |
| [`roadmap.md`](./roadmap.md) | 将来構想 |
| [`spec/combat-stats.md`](./spec/combat-stats.md) | **戦闘統計**画面の表示仕様 |
| [`spec/run-overview.md`](./spec/run-overview.md) | **ラン全体統計**画面の表示仕様 |
| [`spec/data-sources.md`](./spec/data-sources.md) | UI に出るデータの正規経路 (mod hook ⇄ event ⇄ UI section) |
| [`archive/`](./archive/) | 過去の phase plan / 旧版 spec (履歴目的、追従しない) |

## 仕様ドキュメントの読み方

- `spec/` 以下が「**現在の WebUI が満たすべき仕様**」。改修時はここを先に更新してから実装する。
- `architecture.md` / `api.md` は実装契約。スキーマや経路の変更時に必ず追従する。
- `roadmap.md` は構想。実装済みの内容は移動して spec / architecture に反映させる。
- `archive/` はもう更新しない。歴史的経緯を見たい時にだけ参照する。

## 改修フロー

1. 該当する `spec/*.md` を更新（期待挙動を最新化）
2. `api.md` も影響あれば更新
3. mod / backend / web を実装
4. デプロイ前に spec の各項目を手で確認、もしくは E2E (今後整備) で確認
