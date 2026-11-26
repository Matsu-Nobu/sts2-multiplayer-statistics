# カード報酬の評価

## 概要

カード報酬の画面を開いた時点の状態を、AlphaDefect (hmty-CHEN/AlphaDefect-Public、MIT ライセンス) の AI で評価する。
AI は、提示されたカードとスキップなどの選択肢のそれぞれを選ぶ確率と、その局面の勝ちやすさの値を出す。

評価は評価サーバー (`evaluator/`、Python) が行い、backend (Go) はその結果を保存する。
mod は評価に要る値を `card_reward_offered` (`api.md「card_reward_offered」`) で送るだけで、評価には関わらない。
画面の表示は `run-overview.md「AI の評価」` にある。

## 評価の流れ

backend は、`POST /sessions/{id}/events` で `card_reward_offered` を 1 件でも保存したら、応答を返したあとで評価を始める。
backend はそのセッションの `card_reward_offered` をすべて、保存した順に評価サーバーの `POST /evaluate` に送る。
返ってきた結果のうち、まだ保存していない event の結果だけを `card_reward_evaluations` に保存する。

`card_reward_offered` を送らない mod (このイベントを足す前のバージョン) のランと、履歴から取り込んだラン (`POST /imports/run`) には、`card_reward_offered` が無い。
backend はこれらのランでは評価サーバーを呼ばず、`card_reward_evaluations` に行を作らない。
マルチプレイでは、ホストの mod が全員分の `card_reward_offered` を送るので、ほかのプレイヤーが mod を入れていなくても評価できる。

評価サーバーへの送信が失敗したら、backend はログに書き、そのセッションの次の `card_reward_offered` を受け取ったときに送り直す。
評価の失敗は、mod からの event の保存には影響しない。
環境変数 `EVALUATOR_URL` が空なら、backend は評価をしない。

## 評価サーバーの API

評価サーバーは Fly の内部ネットワークにだけ公開する。インターネットからは呼べない。

### `POST /evaluate`

```json
{
  "game_version": "v0.114.0",
  "events": [
    {"id": 123, "player_id": "76561199204788207", "floor": 2, "payload": { "...": "card_reward_offered の payload" }}
  ]
}
```

`events` は 1 つのセッションの `card_reward_offered` を保存した順に並べたもの。`id` は backend の `events.id`。
評価サーバーは、同じプレイヤーの同じ階で先に開いたカード報酬を、後のカード報酬の評価の入力から外す。
AlphaDefect は、開いて取らずに閉じたカード報酬を、同じ報酬画面のほかの判断の入力に含めずに学習しているためである。

応答は、`events` と同じ順に 1 件ずつ結果を返す。

```json
{
  "results": [
    {"id": 123, "status": "ok", "model": "fool_ema_v24181", "value": -0.323,
     "card_probs": [0.0001, 0.9998, 0.0001], "alternative_probs": {"Skip": 0.0000}}
  ]
}
```

| 項目 | 意味 |
|---|---|
| `status` | 下の表のいずれか。`ok` のときだけ `value`・`card_probs`・`alternative_probs` がある |
| `model` | 評価に使った重みの名前。`unsupported_version`・`unsupported_character` のときは無い |
| `value` | 局面の勝ちやすさ。-1 から 1 |
| `card_probs` | payload の `offered` の順に、各カードを選ぶ確率 |
| `alternative_probs` | payload の `alternatives` の各選択肢を選ぶ確率 |

| `status` | いつ |
|---|---|
| `ok` | 評価した |
| `unsupported_version` | 評価サーバーが、そのゲームのバージョンの表を持っていない。セッションの `game_version` が無いときも含む |
| `unsupported_character` | 評価サーバーが、そのキャラクターのモデルを持っていない |
| `unknown_card` | 提示されたカードに、モデルが学習していないカードがある |
| `invalid_payload` | payload の形が、評価サーバーの知っている形と違う。mod のバージョンが評価サーバーより新しいか古いときに起きる。同じセッションのほかの event は評価する |

### `GET /healthz`

評価サーバーが動いていれば `200 ok` を返す。

## 保存する表

backend は `card_reward_evaluations` に、`card_reward_offered` の event 1 件につき 1 行を保存する。

| 列 | 中身 |
|---|---|
| `event_id` | `events.id` (主キー) |
| `status` | 評価サーバーの `status` |
| `model` | 評価サーバーの `model` |
| `value` | 評価サーバーの `value` |
| `card_probs_json` | 評価サーバーの `card_probs` を JSON にしたもの |
| `alternative_probs_json` | 評価サーバーの `alternative_probs` を JSON にしたもの |
| `evaluated_at` | 保存した時刻 |

## 評価サーバーが持つデータ

評価サーバーは、ゲームのバージョンごとの表を `evaluator/data/<バージョン>/` に、キャラクターごとのモデルを `evaluator/models/` に持つ。
表とモデルの作り方は `evaluator/README.md` にある。

| ファイル | 中身 |
|---|---|
| `data/<バージョン>/vocab.txt`・`aliases.txt`・`no_downside_relics.txt` | AlphaDefect の `engine/catalog/` の写し |
| `data/<バージョン>/tables.json` | カードの記録・ポーション・幕の敵の一覧など、ゲームのデータで決まる値。AlphaDefect のエンジンから書き出す |
| `data/<バージョン>/enchant_effects.json.gz` | エンチャントがカードの記録を変える量 |
| `models/<キャラクター>.onnx` | AlphaDefect の重みを ONNX に変換したもの。リポジトリには入れない |
| `models/manifest.json` | キャラクターごとの、モデルのファイル・重みの名前・学習した語彙の数・観測の形式 |

## 既知の制約

- AlphaDefect の重みは v0.114.0 で学習されていない。v0.114.0 で作り直されたフールのカードは、AI が古い効果のつもりで評価する。
