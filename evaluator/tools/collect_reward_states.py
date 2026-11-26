"""ヘッドレスエンジンでランを進め、カード報酬の局面のフレームと事実を集める（サーバー側の変換の確認用）。
戦闘は手元のエンジンの resolve 操作で勝ったことにし、戦闘の外の選択はランダムに選ぶ。
使い方: python -I collect_reward_states.py <game-v0.114.0> <出力フォルダ> <ラン数>
"""
import random
import struct
import sys
from pathlib import Path

root = Path(sys.argv[1])
sys.path.insert(0, str(root / "python"))
from sts2env.env import Sts2Env, EngineError  # noqa: E402
from sts2env.protocol import parse, DecisionKind  # noqa: E402

out_dir, n_runs = Path(sys.argv[2]), int(sys.argv[3])
out_dir.mkdir(parents=True, exist_ok=True)
FORMAT = {"DEFECT": 19, "REGENT": 20, "SILENT": 23, "FOOL": 27, "IRONCLAD": 28, "NECROBINDER": 28}
rng = random.Random(1)
for f in list(out_dir.glob("*.bin")) + list(out_dir.glob("*.facts.jsonl")):
    f.unlink()
files = {}
n_frames = 0
with Sts2Env() as env:
    def req(r: dict) -> bytes:
        env._proc.send(r)
        buf = env._proc.recv()
        if struct.unpack_from("<i", buf, 0)[0] == -1:
            raise RuntimeError(buf[4:].decode("utf-8", "replace")[:300])
        return buf

    for k in range(n_runs):
        ch = list(FORMAT)[k % len(FORMAT)]
        if ch not in files:
            files[ch] = open(out_dir / f"{ch}.bin", "wb")
        buf = req({"op": "reset", "mode": "run", "character": ch, "ascension": rng.randint(0, 10), "seed": f"R{k}", "obs_format": FORMAT[ch]})
        for step in range(3000):
            o = parse(buf)
            if o.decision in (DecisionKind.GAME_OVER, DecisionKind.STUCK):
                break
            if o.decision == DecisionKind.COMBAT:
                hp = int(o.header[8])
                try:
                    buf = req({"op": "resolve", "hp": max(1, hp - rng.randint(0, 8))})
                except EngineError as ex:      # 勝ったことにできない戦闘（何度倒しても終わらないもの）はこのランを打ち切る
                    print("give up:", str(ex)[:80], flush=True)
                    break
                continue
            if o.decision == DecisionKind.CARD_REWARD:
                if req({"op": "stats_facts", "path": str((out_dir / f"{ch}.facts.jsonl").resolve())}) != buf:
                    raise RuntimeError("stats_facts の後のフレームが変わった")
                files[ch].write(struct.pack("<I", len(buf))); files[ch].write(buf)
                n_frames += 1
            buf = req({"op": "step", "action": rng.randrange(o.num_actions)})
        print(ch, "run", k, "floor", int(o.header[7]), "frames", n_frames, flush=True)
for f in files.values():
    f.close()
print("frames", n_frames)
