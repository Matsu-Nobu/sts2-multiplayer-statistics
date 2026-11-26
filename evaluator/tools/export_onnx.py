"""重みを ONNX に書き出す（バッチの大きさ 1、1 回の判断ずつ推論する前提）。
使い方: python -I -X utf8 export_onnx.py <game-v0.114.0 を写したフォルダ> <重み.pt> <フレーム.bin> <出力.onnx>
"""
import struct
import sys
from pathlib import Path

root = Path(sys.argv[1])
sys.path.insert(0, str(root / "python"))
import numpy as np  # noqa: E402
import torch  # noqa: E402
from sts2env.protocol import parse  # noqa: E402
from sts2rl.catalog import Catalog  # noqa: E402
from sts2rl.features import Ragged, collate_ragged  # noqa: E402
from sts2rl.model import ModelConfig, PolicyValueNet, load_state, zero_new_vocab  # noqa: E402

blob = torch.load(sys.argv[2], map_location="cpu", weights_only=True)
net = PolicyValueNet(ModelConfig(**blob["config"]["model"])).eval()
load_state(net, blob["state"], blob.get("content_names"))
old_n, vocab_n = blob.get("vocab_n") or 1873, Catalog().vocab_size
if old_n < vocab_n:
    with torch.no_grad():
        zero_new_vocab(net, old_n, vocab_n)
# PolicyValueNet._content は推論のとき重みのメモリの位置で計算結果を再利用するが、書き出しの途中ではその位置を読めない。
net._content = lambda: net.content_proj(net.content_table.to(net.content_proj.weight.dtype))


def batch_of(buf: bytes):
    o = parse(buf)
    r = Ragged.from_obs([o], np.array([1]))
    r.tok_priv[:] = 0
    return collate_ragged(r)


data = Path(sys.argv[3]).read_bytes()
frames, i = [], 0
while i < len(data):
    n = struct.unpack_from("<I", data, i)[0]
    frames.append(data[i + 4:i + 4 + n]); i += 4 + n

# 可変の次元がなるべく 2 以上になるフレームを見本にする（大きさ 0 と 1 は書き出しで定数に固定されやすい）。
batches = [batch_of(f) for f in frames]
first = batches[0]
NAMES = [k for k, v in first.__dict__.items() if isinstance(v, np.ndarray)]
ATTEND = first.attend


def score(b):
    return sum(1 for k in NAMES if all(d >= 2 for d in getattr(b, k).shape))


sample = max(batches, key=score)
print("sample: cand", sample.cand_ints.shape[0], "core", sample.core_idx.shape[1], "score", score(sample), "/", len(NAMES))


class Wrapper(torch.nn.Module):
    def __init__(self, net):
        super().__init__()
        self.net = net

    def forward(self, *xs):
        b = dict(zip(NAMES, xs))
        b["width"] = b["cand_ints"].shape[0]
        b["groups"] = [(torch.zeros(1, dtype=torch.long), b["core_idx"].shape[1])]
        b["attend"] = ATTEND
        logits, value, _phi, _extra = self.net.forward_all(b)
        return logits, value


args = tuple(torch.from_numpy(getattr(sample, k)) for k in NAMES)
# 集めたフレームの中で大きさが変わる次元だけを可変にする（特徴の列数のように変わらない次元は固定する）。
varying = {k: {d for d in range(getattr(first, k).ndim) if len({getattr(b, k).shape[d] for b in batches}) > 1} for k in NAMES}
dyn = tuple({d: torch.export.Dim.AUTO for d in varying[k]} for k in NAMES)
print("dynamic dims:", {k: sorted(v) for k, v in varying.items() if v})
from onnxscript import opset18 as op  # noqa: E402


def remainder_scalar(self, other):
    # 標準の変換は割る数が定数のときしか扱えない。整数の Mod（fmod=0）は Python の % と同じく割る数の符号になる。
    return op.Mod(self, op.CastLike(other, self))


prog = torch.onnx.export(Wrapper(net).eval(), args, dynamic_shapes=(dyn,), dynamo=True, input_names=NAMES,
                         output_names=["logits", "value"], opset_version=18,
                         custom_translation_table={torch.ops.aten.remainder.Scalar: remainder_scalar})
prog.save(sys.argv[4])
print("saved", sys.argv[4], "inputs", len(NAMES))
(Path(sys.argv[4]).with_suffix(".inputs.txt")).write_text("\n".join(NAMES), encoding="utf-8")
