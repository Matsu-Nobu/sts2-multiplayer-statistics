"""AlphaDefect のエンジン (engine-patch を当てたもの) から、評価サーバーの表を書き出す。
使い方: python -I dump_tables.py <AlphaDefect の game-<バージョン> フォルダ> <出力フォルダ>
出力フォルダに tables.json と tables.enchanted.json を書く。続けて build_enchant_table.py で enchant_effects.json を作る。
"""
import struct
import sys
from pathlib import Path

root, out = Path(sys.argv[1]), Path(sys.argv[2]).resolve()
sys.path.insert(0, str(root / "python"))
from sts2env.env import Sts2Env  # noqa: E402

out.mkdir(parents=True, exist_ok=True)
with Sts2Env() as env:
    for req in ({"op": "reset", "mode": "run", "character": "IRONCLAD", "ascension": 0, "seed": "T0", "obs_format": 28},
                {"op": "stats_tables", "path": str(out / "tables.json")}):
        env._proc.send(req)
        buf = env._proc.recv()
        if struct.unpack_from("<i", buf, 0)[0] == -1:
            raise RuntimeError(buf[4:].decode("utf-8", "replace")[:300])
print("wrote", out / "tables.json", out / "tables.enchanted.json")
