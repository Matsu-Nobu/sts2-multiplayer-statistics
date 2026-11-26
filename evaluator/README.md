# 評価サーバー

カード報酬の画面を開いた時点の状態 (`card_reward_offered`) を、AlphaDefect の AI で評価する HTTP サーバー。
評価の流れと API は [`docs/spec/card-evaluation.md`](../docs/spec/card-evaluation.md) にある。

## 構成

| パス | 中身 |
|---|---|
| `sts2eval/` | HTTP の入り口 (`server.py`)、評価 (`evaluate.py`)、payload から観測を作る処理 (`from_mod.py`、`encode.py`) |
| `sts2env/`・`sts2rl/` | AlphaDefect の `python/` から写した `protocol.py` と `features.py` (MIT ライセンス、`sts2env/LICENSE.AlphaDefect`) |
| `data/<ゲームのバージョン>/` | ゲームのバージョンごとの表 |
| `models/` | キャラクターごとの ONNX のモデルと `manifest.json`。`.onnx` はリポジトリに入れない |
| `tools/` | 表とモデルを作る道具 |
| `tests/` | 試験 |

`encode.py` は、AlphaDefect のエンジンの観測の符号化 (`Observation.cs` ほか) のうち、戦闘の外のカード報酬の判断に使う部分を Python に移したもの。

## 開発

Python 3.12 で動かす。ONNX Runtime は Python 3.15 向けの配布物が無い。

```bash
cd evaluator
uv venv --python 3.12 .venv
uv pip install --python .venv/bin/python -r requirements.txt
.venv/bin/python -m unittest discover -s tests -t .
EVALUATOR_DATA=data EVALUATOR_MODELS=models .venv/bin/python -m sts2eval.server
```

試験には `models/` の ONNX のモデルが要る。

## モデルの作り方

AlphaDefect の重み (`.pt`) を ONNX に変換する。変換には PyTorch と AlphaDefect の `python/` が要る。

1. `tools/collect_reward_states.py` で、エンジンからカード報酬の局面をキャラクターごとに集める (`<キャラクター>.bin`)。変換は、集めた局面で大きさが変わる入力の次元だけを可変にする。
2. `tools/export_onnx.py <game フォルダ> <重み.pt> <キャラクター>.bin models/<キャラクター>.onnx` で変換する。
3. `tools/ref_torch.py` で PyTorch の結果を出し、`tools/compare_onnx.py` で ONNX の結果と比べる。一番に選ぶ候補がすべての局面で一致することを確認する。
4. `models/manifest.json` に、重みの名前・学習した語彙の数 (重みの `vocab_n`、`ref_torch.py` が表示する) ・観測の形式を書く。

## 表の作り方

表は、AlphaDefect のエンジンに `tools/engine-patch/` の変更を当てて書き出す。

1. AlphaDefect の `game-<バージョン>/engine/Sts2Headless/` に、`engine-patch/StatsProbe.cs` を `Engine/` として置き、`engine-patch/engine.diff` を当てる。
2. `dotnet build -c Release -p:Sts2GameVersion=<バージョン> -p:StsStatsModSrc=<このリポジトリ>/mod/src` でビルドする。mod の `CardRewardPayload.cs` を一緒にビルドし、エンジンの中で mod と同じ payload を作る。
3. `tools/dump_tables.py <game フォルダ> <出力フォルダ>` で `tables.json` と `tables.enchanted.json` を書き出す。
4. `tools/build_enchant_table.py` で `enchant_effects.json` を作り、gzip で圧縮して `data/<バージョン>/` に置く。
5. AlphaDefect の `engine/catalog/` から `vocab.txt`・`aliases.txt`・`no_downside_relics.txt` を写す。
6. `tools/collect_reward_states.py` で局面と payload を集め、`tools/compare_encode.py <game フォルダ> <フォルダ> --mod` で、すべての局面の観測がエンジンと一致することを確認する。

エンジンのいくつかのカードは際限なく強化できるので、`StatsProbe.cs` は強化の回数を 20 で止める。止めないと書き出しが数 GB になる。

## デプロイ

Fly のアプリ `sts2stats-eval` に置く。インターネット向けの IP は割り当てず、Fly の内部ネットワークの IP (Flycast) だけを割り当てる。
backend は `EVALUATOR_URL=http://sts2stats-eval.flycast` で呼ぶ。

```bash
cd evaluator
fly deploy --no-public-ips
fly ips allocate-v6 --private   # 初回だけ
```
