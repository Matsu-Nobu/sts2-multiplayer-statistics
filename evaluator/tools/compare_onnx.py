"""ONNX Runtime と NumPy だけで推論し、ref_torch.py の結果と比べる。PyTorch は読み込まない。
使い方: python -I -X utf8 compare_onnx.py <game-v0.114.0 を写したフォルダ> <モデル.onnx> <フレーム.bin> <ref.npz>
"""
import struct
import sys
import time
from pathlib import Path

root = Path(sys.argv[1])
sys.path.insert(0, str(root / "python"))
import numpy as np  # noqa: E402
import onnxruntime as ort  # noqa: E402
from sts2env.protocol import parse  # noqa: E402
from sts2rl.features import Ragged, collate_ragged  # noqa: E402

model = Path(sys.argv[2])
names = model.with_suffix(".inputs.txt").read_text(encoding="utf-8").split("\n")
sess = ort.InferenceSession(str(model), providers=["CPUExecutionProvider"])
ref = np.load(sys.argv[4], allow_pickle=True)

data = Path(sys.argv[3]).read_bytes()
i = k = 0
max_dlogit = max_dprob = max_dvalue = 0.0
argmax_diff = 0
errors = []
t = []
while i < len(data):
    n = struct.unpack_from("<I", data, i)[0]
    o = parse(data[i + 4:i + 4 + n]); i += 4 + n
    r = Ragged.from_obs([o], np.array([1]))
    r.tok_priv[:] = 0
    b = collate_ragged(r)
    feed = {nm: getattr(b, nm) for nm in names}
    t0 = time.perf_counter()
    try:
        lg, v = sess.run(["logits", "value"], feed)
    except Exception as e:  # noqa: BLE001  失敗したフレームの入力の大きさを記録して続ける
        errors.append((k, {nm: feed[nm].shape for nm in names}, str(e)[-120:]))
        k += 1
        continue
    t.append(time.perf_counter() - t0)
    lg = lg[0, :o.num_actions].astype(np.float64)
    rl = ref["logits"][k]
    fin = np.isfinite(rl)
    max_dlogit = max(max_dlogit, float(np.max(np.abs(lg[fin] - rl[fin]))) if fin.any() else 0.0)
    p = np.exp(lg - lg.max()); p /= p.sum()
    q = np.exp(rl - rl.max()); q /= q.sum()
    max_dprob = max(max_dprob, float(np.max(np.abs(p - q))))
    max_dvalue = max(max_dvalue, abs(float(v[0]) - float(ref["values"][k])))
    argmax_diff += int(np.argmax(p) != np.argmax(q))
    k += 1
print(f"{k} frames: max |dlogit| {max_dlogit:.2e}, max |dprob| {max_dprob:.2e}, max |dvalue| {max_dvalue:.2e}, "
      f"argmax differs {argmax_diff}; onnx median {np.median(t) * 1000:.1f} ms, max {np.max(t) * 1000:.1f} ms")
print("errors", len(errors))
for e in errors[:3]: print(e[0], {a: b for a, b in e[1].items() if 0 in b or 1 in b}, e[2])
print("torch loaded:", "torch" in sys.modules)
