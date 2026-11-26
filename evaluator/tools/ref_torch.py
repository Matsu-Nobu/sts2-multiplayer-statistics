"""PyTorch で、保存したフレームすべての推論結果（候補のロジット、価値）を出す。ONNX と比べる正解の値。
使い方: python -I ref_torch.py <game-v0.114.0 を写したフォルダ> <重み.pt> <フレーム.bin> <出力.npz>
"""
import struct
import sys
import time
from pathlib import Path

root = Path(sys.argv[1])
sys.path.insert(0, str(root / "python"))
import numpy as np  # noqa: E402
import torch  # noqa: E402
from sts2env.protocol import parse  # noqa: E402
from sts2rl.catalog import Catalog  # noqa: E402
from sts2rl.features import Ragged, collate_ragged  # noqa: E402
from sts2rl.model import ModelConfig, PolicyValueNet, load_state, zero_new_vocab  # noqa: E402

VOCAB_N_LEGACY = 1873
MODE_RUN = 1  # sts2rl.actor.MODE_RUN（0 は戦闘だけを練習するモード）

blob = torch.load(sys.argv[2], map_location="cpu", weights_only=True)
net = PolicyValueNet(ModelConfig(**blob["config"]["model"])).eval()
load_state(net, blob["state"], blob.get("content_names"))
old_n, vocab_n = blob.get("vocab_n") or VOCAB_N_LEGACY, Catalog().vocab_size
if old_n < vocab_n:
    with torch.no_grad():
        zero_new_vocab(net, old_n, vocab_n)
print("obs_format", blob.get("obs_format"), "version", blob.get("version"), "vocab", old_n, "->", vocab_n)

data = Path(sys.argv[3]).read_bytes()
frames, i = [], 0
while i < len(data):
    n = struct.unpack_from("<I", data, i)[0]
    frames.append(data[i + 4:i + 4 + n]); i += 4 + n

logits, values, kinds, t = [], [], [], []
for buf in frames:
    o = parse(buf)
    r = Ragged.from_obs([o], np.array([MODE_RUN]))
    r.tok_priv[:] = 0
    b = collate_ragged(r).to("cpu")
    t0 = time.perf_counter()
    with torch.no_grad():
        lg, v, _phi, _extra = net.forward_all(b)
    t.append(time.perf_counter() - t0)
    logits.append(lg[0, :o.num_actions].double().numpy())
    values.append(float(v[0]))
    kinds.append(int(o.header[1]))
np.savez(sys.argv[4], logits=np.array(logits, dtype=object), values=np.array(values), kinds=np.array(kinds))
print(f"{len(frames)} frames, forward median {np.median(t) * 1000:.1f} ms, max {np.max(t) * 1000:.1f} ms")
