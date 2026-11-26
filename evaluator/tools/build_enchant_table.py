"""エンジンが書き出したエンチャント付きのカードの記録（tables.enchanted.json）を、エンチャントの効果の表にする。
使い方: python -I build_enchant_table.py <tables.json> <tables.enchanted.json> <出力.json>

効果は、エンチャントの無いカードの記録との差で表す。
  d1: 量 1 のときの、カードの記録の特徴の差（列 → 差）
  slope: 量が 1 増えるごとの特徴の差
  extra: 量 1 のときに足される附帯の記録（[整数, 特徴]）。extra_slope は附帯の記録の特徴が量 1 ごとに変わる分
  表は形式 19 と 28 の 2 つを持ち、形式 28 は "28"、それ以外の形式は "19" を使う。
  dis: 無効になった状態（量 1）と、量 1 の有効な状態との特徴の差。dis_extra は無効のときの附帯の記録
量 3 の記録が d1 と slope から計算した値と一致するか、形式 19 と 28 で差が同じかを確認し、合わないものを数えて表示する。
"""
import json
import sys

import numpy as np

tables = json.load(open(sys.argv[1], encoding="utf-8"))
enchanted = json.load(open(sys.argv[2], encoding="utf-8"))


def feats(rows):
    """記録の特徴を 30 列にそろえる（形式 19 は 20 列、形式 27 と 28 は 22 列で書き出される）。"""
    return [np.pad(np.array(f, np.float64), (0, 30 - len(f))) for _, f in rows]


def diff(a, b):
    return {int(c): float(a[c] - b[c]) for c in np.flatnonzero(~np.isclose(a, b, atol=1e-6))}


out = {}
problems = {"not_linear": 0, "format_differs": 0, "base_has_extra": 0, "extra_count_changes": 0}
examples = {}
for ench, by_card in enchanted.items():
    for card, by_fmt in by_card.items():
        per_fmt = {}
        for fmt, ups in by_fmt.items():
            per_up = []
            for up, variants in enumerate(ups):
                base = tables["cards"][card][fmt][up]
                v1, v2, v3, vd = variants
                if len(base) != 1:
                    problems["base_has_extra"] += 1; examples.setdefault("base_has_extra", (ench, card))
                if not (len(v1) == len(v2) == len(v3)):
                    problems["extra_count_changes"] += 1; examples.setdefault("extra_count_changes", (ench, card))
                    per_up.append(None); continue
                f0, f1, f2, f3, fd = feats(base)[0], feats(v1), feats(v2), feats(v3), feats(vd)
                slope = [b - a for a, b in zip(f1, f2)]
                if any(not np.allclose(a + 2 * s, c, atol=1e-5) for a, s, c in zip(f1, slope, f3)):
                    problems["not_linear"] += 1; examples.setdefault("not_linear", (ench, card, up))
                # カードそのものの附帯の記録（疯狂科学の型など）は、エンチャントの附帯の記録の後ろに来るので除く
                nb = len(base) - 1
                end1, endd = len(v1) - nb, len(vd) - nb
                e = {"d1": diff(f1[0], f0), "slope": diff(f2[0], f1[0]),
                     "extra": [[ints, list(map(float, f))] for (ints, _), f in zip(v1[1:end1], f1[1:end1])],
                     "extra_slope": [diff(b, a) for a, b in zip(f1[1:end1], f2[1:end1])],
                     "dis": diff(fd[0], f1[0]), "dis_extra": [[ints, list(map(float, f))] for (ints, _), f in zip(vd[1:endd], fd[1:endd])]}
                per_up.append(e)
            per_fmt[fmt] = per_up
        if json.dumps(per_fmt.get("19")) != json.dumps(per_fmt.get("28")):
            problems["format_differs"] += 1; examples.setdefault("format_differs", (ench, card))
        # 形式 28 だけが奥斯提の攻撃をカードのダメージに数える（Cards.cs の V22）ので、形式ごとに持つ
        out.setdefault(ench, {})[card] = per_fmt
json.dump(out, open(sys.argv[3], "w", encoding="utf-8"), separators=(",", ":"))
print(problems)
for k, v in examples.items():
    print(k, v)
print("enchantments", len(out), "pairs", sum(len(v) for v in out.values()))
