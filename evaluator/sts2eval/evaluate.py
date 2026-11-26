"""card_reward_offered のイベントを AlphaDefect のネットワークで評価する。

ゲームのバージョンごとの表は data/<バージョン>/ に、キャラクターごとのモデルは models/ に置く（README.md）。
"""
from __future__ import annotations

import gzip
import json
import logging
import threading
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import onnxruntime as ort

from sts2rl.features import Ragged, collate_ragged

from . import from_mod
from .encode import Encoder, Vocab

# 結果の status。ok 以外のときは probs と value を返さない
OK = "ok"
UNSUPPORTED_VERSION = "unsupported_version"      # 表を持っていないゲームのバージョン
UNSUPPORTED_CHARACTER = "unsupported_character"  # モデルを持っていないキャラクター
UNKNOWN_CARD = "unknown_card"                    # 選択肢に、モデルが学習していないカードがある
INVALID_PAYLOAD = "invalid_payload"              # payload の形が、評価サーバーの知っている形と違う

log = logging.getLogger("sts2eval")


@dataclass
class VersionData:
    encoder: Encoder
    vocab: Vocab
    no_downside: set[str]


@dataclass
class Model:
    name: str
    session: ort.InferenceSession
    inputs: list[str]
    vocab_n: int
    obs_format: int


class Evaluator:
    def __init__(self, data_dir: Path, models_dir: Path):
        self.data_dir = data_dir
        self.models_dir = models_dir
        self.manifest = json.loads((models_dir / "manifest.json").read_text(encoding="utf-8"))
        self.versions: dict[str, VersionData] = {}
        # モデル 1 つで約 150MB のメモリを使うので、最後に使った 1 つだけを持つ
        self.model: Model | None = None
        self.lock = threading.Lock()

    def supported_versions(self) -> list[str]:
        return sorted(d.name for d in self.data_dir.iterdir() if (d / "tables.json").exists())

    def evaluate(self, game_version: str, events: list[dict]) -> list[dict]:
        """events は 1 つのランの card_reward_offered を、届いた順に並べたもの。各要素は id・player_id・floor・payload を持つ。
        同じプレイヤーの同じ階で先に開いたカード報酬を、後のカード報酬の評価の入力から外すため、ランのイベントをまとめて受け取る。"""
        with self.lock:
            data = self._version(game_version)
            results = []
            opened: dict[tuple[str, int], list[list[str]]] = {}
            for e in events:
                p = e["payload"]
                key = (e["player_id"], e["floor"])
                earlier = opened.setdefault(key, [])
                if data is None:
                    results.append({"id": e["id"], "status": UNSUPPORTED_VERSION})
                    continue
                # mod のバージョンによって payload の形が違うことがある。形の合わない event は、その event だけを結果に記録し、
                # 同じランのほかの event の評価は続ける
                try:
                    results.append({"id": e["id"], **self._evaluate_one(data, e["floor"], p, list(earlier))})
                    earlier.append(from_mod.offered_ids(p))
                except (KeyError, TypeError, ValueError, IndexError):
                    log.exception("invalid card_reward_offered payload: event %s", e["id"])
                    results.append({"id": e["id"], "status": INVALID_PAYLOAD})
            return results

    def _evaluate_one(self, data: VersionData, floor: int, p: dict, opened: list[list[str]]) -> dict:
        entry = self.manifest.get(p["character_id"])
        if entry is None:
            return {"status": UNSUPPORTED_CHARACTER}
        model = self._model(p["character_id"], entry)
        # 語彙に無いカードは 1（<unk>）になる。学習のあとで語彙に足されたカードは、モデルの語彙の数以上の ID になる
        if any(not 2 <= data.vocab.id("CARD." + c["card_id"]) < model.vocab_n for c in p["offered"]):
            return {"status": UNKNOWN_CARD, "model": model.name}
        obs = data.encoder.encode(from_mod.facts(p, floor, data.no_downside, opened))
        if int(obs.header[0]) != model.obs_format:
            raise RuntimeError(f"{p['character_id']}: 観測の形式 {int(obs.header[0])} がモデルの形式 {model.obs_format} と違う")
        ragged = Ragged.from_obs([obs], np.array([1]))
        ragged.tok_priv[:] = 0
        batch = collate_ragged(ragged)
        logits, value = model.session.run(["logits", "value"], {nm: getattr(batch, nm) for nm in model.inputs})
        lg = logits[0, :len(obs.cand_ints)].astype(np.float64)
        probs = np.exp(lg - lg.max())
        probs /= probs.sum()
        n = len(p["offered"])
        return {
            "status": OK, "model": model.name, "value": float(value[0]),
            # 候補は、提示されたカードを payload の offered の順に並べたあとに、alternatives の順に並ぶ（encode.py）
            "card_probs": probs[:n].tolist(),
            "alternative_probs": dict(zip(p["alternatives"], probs[n:].tolist())),
        }

    def _version(self, game_version: str) -> VersionData | None:
        if game_version not in self.versions:
            d = self.data_dir / game_version
            if not (d / "tables.json").exists():
                return None
            tables = json.loads((d / "tables.json").read_text(encoding="utf-8"))
            with gzip.open(d / "enchant_effects.json.gz", "rt", encoding="utf-8") as f:
                tables["enchant_effects"] = json.load(f)
            vocab = Vocab(d)
            no_downside = {s.strip() for s in (d / "no_downside_relics.txt").read_text(encoding="utf-8").splitlines()
                           if s.strip() and not s.startswith("#")}
            self.versions[game_version] = VersionData(Encoder(tables, vocab), vocab, no_downside)
        return self.versions[game_version]

    def _model(self, character: str, entry: dict) -> Model:
        if self.model is None or self.model.name != entry["name"]:
            self.model = None  # 新しいモデルを読む前に古いモデルのメモリを手放す
            path = self.models_dir / entry["file"]
            opts = ort.SessionOptions()
            opts.intra_op_num_threads = 1
            session = ort.InferenceSession(str(path), opts, providers=["CPUExecutionProvider"])
            inputs = path.with_suffix(".inputs.txt").read_text(encoding="utf-8").split("\n")
            self.model = Model(entry["name"], session, inputs, entry["vocab_n"], entry["obs_format"])
        return self.model
