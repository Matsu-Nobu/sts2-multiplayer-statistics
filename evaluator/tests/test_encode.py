"""mod の payload から作った観測が、AlphaDefect のエンジンが同じ局面で作った観測と一致するかを比べる。

fixtures/<キャラクター>.frames.jsonl.gz の各行は、エンジンで集めたカード報酬の局面 1 つ。
frame はエンジンが書き出した観測、mod は同じ局面で mod と同じコード (CardRewardPayload.cs) が作った payload。
"""
import base64
import gzip
import json
import unittest
from pathlib import Path

import numpy as np

from sts2env.protocol import parse
from sts2eval import from_mod
from sts2eval.evaluate import Evaluator

HERE = Path(__file__).parent
ROOT = HERE.parent
CHARACTERS = ["DEFECT", "REGENT", "SILENT", "FOOL", "IRONCLAD", "NECROBINDER"]
# collate_ragged がネットワークに渡す、観測の頭部の列 (sts2rl/features.py)
HEADER_USED = [1, 2, 3, 11, 16, 17, 18, 19, 20, 21, 22, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33]


class EncodeTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = Evaluator(ROOT / "data", ROOT / "models")._version("v0.114.0")

    def test_matches_engine_observation(self):
        for ch in CHARACTERS:
            rows = [json.loads(line) for line in gzip.open(HERE / "fixtures" / f"{ch}.frames.jsonl.gz", "rt")]
            opened, floor = [], None
            for k, row in enumerate(rows):
                # 同じ報酬画面で先に開いたカード報酬。fixtures はランごとに階の順に並び、隣り合うランは階が違う
                if row["floor"] != floor:
                    opened, floor = [], row["floor"]
                with self.subTest(character=ch, row=k):
                    ref = parse(base64.b64decode(row["frame"]))
                    got = self.data.encoder.encode(from_mod.facts(row["mod"], row["floor"], self.data.no_downside, opened))
                    np.testing.assert_array_equal(ref.header[HEADER_USED], got.header[HEADER_USED])
                    np.testing.assert_array_equal(ref.tok_ints, got.tok_ints)
                    np.testing.assert_allclose(ref.tok_feats, got.tok_feats, atol=1e-5)
                    np.testing.assert_array_equal(ref.cand_ints, got.cand_ints)
                    np.testing.assert_array_equal(ref.cand_feats, got.cand_feats)
                opened.append(from_mod.offered_ids(row["mod"]))


if __name__ == "__main__":
    unittest.main()
