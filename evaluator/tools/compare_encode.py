"""エンジンが作った観測と、encode.py が事実から組み立てた観測を比べる。
使い方: python -I compare_encode.py <AlphaDefect の game-<バージョン> フォルダ> <collect_reward_states.py の出力フォルダ> <evaluator/data/<バージョン>> [--mod]
--mod を付けると、エンジンの事実の代わりに、mod と同じコードで作った payload（事実の "mod"）を from_mod.py で直して使う。
"""
import collections
import gzip
import json
import struct
import sys
from pathlib import Path

import numpy as np

root, probe, data_dir = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
sys.path.insert(0, str(root / "python"))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from sts2env.protocol import parse  # noqa: E402
from sts2eval import from_mod  # noqa: E402
from sts2eval.encode import Encoder, Vocab  # noqa: E402

use_mod = "--mod" in sys.argv
no_downside = {l.strip() for l in (root / "engine" / "catalog" / "no_downside_relics.txt").read_text(encoding="utf-8").splitlines()
               if l.strip() and not l.startswith("#")}

# collate_ragged がネットワークに渡す頭部の列（features.py）
HEADER_USED = [1, 2, 3, 11, 16, 17, 18, 19, 20, 21, 22, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33]

tables = json.loads((data_dir / "tables.json").read_text(encoding="utf-8"))
with gzip.open(data_dir / "enchant_effects.json.gz", "rt", encoding="utf-8") as f:
    tables["enchant_effects"] = json.load(f)
enc = Encoder(tables, Vocab(root / "engine" / "catalog"))
diffs = collections.Counter()
examples = {}
n_frames = n_exact = 0
for bin_path in sorted(probe.glob("*.bin")):
    ch = bin_path.stem
    data = bin_path.read_bytes()
    facts = [json.loads(l) for l in (probe / f"{ch}.facts.jsonl").read_text(encoding="utf-8").splitlines()]
    i = k = 0
    opened, opened_floor = [], None
    while i < len(data):
        n = struct.unpack_from("<I", data, i)[0]
        ref = parse(data[i + 4:i + 4 + n]); i += 4 + n
        fk = facts[k]; k += 1
        if use_mod:
            if fk["mod"] is None:      # 報酬画面から開いていないカード報酬は、mod と同じ形の payload を作れない
                continue
            # 同じ報酬画面（同じラン・同じ階）で先に開いたカード報酬。階が変われば空にする
            if fk["floor"] != opened_floor:
                opened, opened_floor = [], fk["floor"]
            mod = fk["mod"]
            fk = from_mod.facts(mod, fk["floor"], no_downside, opened)
            opened.append(from_mod.offered_ids(mod))
        got = enc.encode(fk)
        n_frames += 1
        bad = []
        for c in HEADER_USED:
            if ref.header[c] != got.header[c]:
                bad.append(f"header[{c}] {ref.header[c]}→{got.header[c]}")
        if ref.tok_ints.shape != got.tok_ints.shape:
            bad.append(f"tokens {ref.tok_ints.shape[0]}→{got.tok_ints.shape[0]}")
        else:
            for r in np.flatnonzero((ref.tok_ints != got.tok_ints).any(1)):
                bad.append(f"tok_ints kind {ref.tok_ints[r, 0]} {ref.tok_ints[r].tolist()}→{got.tok_ints[r].tolist()}")
            for r, c in zip(*np.nonzero(~np.isclose(ref.tok_feats, got.tok_feats, atol=1e-5))):
                bad.append(f"tok_feats kind {ref.tok_ints[r, 0]} col {c} {ref.tok_feats[r, c]:g}→{got.tok_feats[r, c]:g}")
        if ref.cand_ints.shape != got.cand_ints.shape or (ref.cand_ints != got.cand_ints).any():
            bad.append(f"cand_ints {ref.cand_ints.tolist()}→{got.cand_ints.tolist()}")
        if (ref.cand_feats != got.cand_feats).any():
            bad.append("cand_feats")
        if not bad:
            n_exact += 1
        for b in bad:
            key = b.split(" ")[0] + " " + " ".join(b.split(" ")[1:3]) if b.startswith("tok_") else b.split(" ")[0]
            diffs[key] += 1
            examples.setdefault(key, f"{ch} #{k - 1}: {b}")
print(f"{n_exact}/{n_frames} frames identical")
for key, cnt in diffs.most_common(40):
    print(f"{cnt:5d}  {examples[key]}")
