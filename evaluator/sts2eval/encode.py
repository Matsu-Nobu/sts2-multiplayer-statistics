"""カード報酬の局面の事実（mod が送る JSON）から、AlphaDefect のネットワークの入力（観測）を組み立てる。

AlphaDefect の観測の符号化（engine/Sts2Headless/Engine/Observation.cs ほか、MIT ライセンス）のうち、
戦闘の外のカード報酬の判断に使う部分を Python に移したもの。ゲームのデータで決まる値は tables.json から引く。
"""
from __future__ import annotations

from dataclasses import dataclass
from fractions import Fraction
from pathlib import Path

import numpy as np

TOK_INTS, TOK_FEATS, TOK_PRIV = 5, 30, 2
CAND_INTS, CAND_FEATS = 5, 20
HEADER_INTS = 34

# Observation.cs の TokenKind と Zone、Decision.cs の ActionKind、RunSession の DecisionKind の値
TK_GLOBAL, TK_CREATURE, TK_POWER, TK_CARD, TK_RELIC, TK_POTION, TK_OPTION, TK_ENCOUNTER_POOL = 1, 2, 3, 4, 5, 6, 9, 16
Z_NONE, Z_ALLY, Z_DECK, Z_OFFER, Z_REWARD_GROUP1 = 0, 1, 7, 8, 13
AK_PICK_CARD_REWARD, AK_CARD_REWARD_ALT = 7, 8
DK_CARD_REWARD = 3
RELIC_ACTIVE, RELIC_DISABLED = 1, 2
ENCHANT_DISABLED = 1

# Cards.cs の AddCard が、カードのキーワードを書く列
KEYWORD_COLUMNS = {"Ethereal": 9, "Retain": 10, "Exhaust": 11, "Unplayable": 12, "Innate": 15, "Sly": 18, "Eternal": 19}

# Format.cs の FormatFor（キャラクターごとの観測の形式）
FORMAT_FOR = {"CHARACTER.DEFECT": 19, "CHARACTER.REGENT": 20, "CHARACTER.SILENT": 23, "CHARACTER.FOOL": 27,
              "CHARACTER.IRONCLAD": 28, "CHARACTER.NECROBINDER": 28}


@dataclass
class Obs:
    """sts2env.protocol.Obs と同じ並び（parse した後の形。特徴の列は TOK_FEATS、CAND_FEATS まで 0 で埋める）。"""
    header: np.ndarray
    tok_ints: np.ndarray
    tok_feats: np.ndarray
    tok_priv: np.ndarray
    cand_ints: np.ndarray
    cand_feats: np.ndarray
    labels: list
    extra: dict | None


class Vocab:
    """Vocab.cs と同じ ID の付け方：vocab.txt の行番号（先頭 2 行は <pad> と <unk>）。aliases.txt の新しい名前は古い名前の ID にする。"""

    def __init__(self, catalog_dir: Path):
        lines = (catalog_dir / "vocab.txt").read_text(encoding="utf-8").split("\n")
        self.ids = {k: i for i, k in enumerate(lines) if k and i >= 2}
        self.aliases = {}
        alias_path = catalog_dir / "aliases.txt"
        if alias_path.exists():
            for raw in alias_path.read_text(encoding="utf-8").split("\n"):
                f = raw.split("#")[0].split()
                if len(f) == 2 and f[1] in self.ids:
                    self.aliases[f[0]] = self.ids[f[1]]

    def id(self, key: str | None) -> int:
        if key is None:
            return 0
        if key in self.aliases:
            return self.aliases[key]
        return self.ids.get(key, 1)


class Encoder:
    def __init__(self, tables: dict, vocab: Vocab):
        self.t = tables
        self.v = vocab

    def encode(self, f: dict) -> Obs:
        fmt = FORMAT_FOR[f["character"]]
        v27 = fmt >= 27
        ti: list[list[int]] = []
        tf: list[list[float]] = []

        def emit(ints: list[int], feats: dict[int, float] | list[float]) -> int:
            row = [0.0] * TOK_FEATS
            items = feats.items() if isinstance(feats, dict) else enumerate(feats)
            for k, x in items:
                row[k] = float(x)
            ti.append(list(ints)); tf.append(row)
            return len(ti) - 1

        def add_card(card: dict, zone: int) -> int:
            """Cards.cs の AddCard と同じ順に記録を足す：カード、苦痛、エンチャントの附帯の記録（無効、再演の回数）。"""
            if card.get("tinker"):      # マッドサイエンス：型と追加効果ごとの記録
                ups = self.t["mad_science"][str(fmt)][f'{card["tinker"]["type"]}.{card["tinker"]["rider"]}']
            else:
                ups = self.t["cards"][card["id"]][str(fmt)]
            records = ups[min(card["upgrade"], len(ups) - 1)]
            first = len(ti)
            ints, feats = records[0]
            emit([ints[0], ints[1], zone, -1, ints[4]], feats)
            # ランの途中で値が変わるカード（入った部屋の数を覚えるダウジングなど）は、送られたカードの数値で上書きする。
            # 牌組や供選択のカードでは、16、17 列はダメージ、ブロックと同じ値になる（Cards.cs の AddCard）
            if not card.get("tinker"):
                for name, col in self.t["var_columns"][card["id"]][str(fmt)]:
                    if name in card.get("vars", {}):
                        tf[first][col] = card["vars"][name]
                        if col in (5, 6):
                            tf[first][col + 11] = card["vars"][name]
            enc, aff = card.get("enchantment"), card.get("affliction")
            effect = self.enchant_effect(card, fmt) if enc else None
            if enc:
                ti[first][4] = self.v.id(enc["id"])
                if effect:
                    a = enc["amount"]
                    for col, d in effect["d1"].items():
                        tf[first][int(col)] += d
                    for col, d in effect["slope"].items():
                        tf[first][int(col)] += (a - 1) * d
                    if enc["status"] == ENCHANT_DISABLED:
                        for col, d in effect["dis"].items():
                            tf[first][int(col)] += d
                else:
                    tf[first][13] = enc["amount"]
            # コストとキーワードはエンチャントやイベントでも変わるので、送られたカードの今の値で上書きする（AddCard の 0、2、9–12、15、18、19 列）
            if "keywords" in card:
                if card["cost"] is not None:
                    tf[first][0] = card["cost"]
                if tf[first][1] != 2:      # 星の X コスト（列 1 が 2）は列 2 を 0 にする
                    tf[first][2] = card["star_cost"]
                for kw, col in KEYWORD_COLUMNS.items():
                    tf[first][col] = 1.0 if kw in card["keywords"] else 0.0
            if aff:
                tf[first][14] = aff["amount"]
                emit([TK_POWER, self.v.id(aff["id"]), zone, first, 0], {0: aff["amount"], 1: 1})
            if effect:
                disabled = enc["status"] == ENCHANT_DISABLED
                extra = effect["dis_extra"] if disabled else effect["extra"]
                for k, (e_ints, e_feats) in enumerate(extra):
                    row = list(e_feats)
                    if not disabled:
                        for col, d in effect["extra_slope"][k].items():
                            row[int(col)] += (enc["amount"] - 1) * d
                    emit([e_ints[0], e_ints[1], zone, first, e_ints[4]], row)
            elif enc and enc["status"] == ENCHANT_DISABLED:
                emit([TK_POWER, self.v.id(enc["id"]), zone, first, 0], {0: enc["amount"], 3: 1})
            for ints, feats in records[1:]:
                emit([ints[0], ints[1], zone, first, ints[4]], feats)
            return first

        # 全体の記録（Observation.AddGlobal）
        odds = f["unknown_odds"]
        emit([TK_GLOBAL, self.v.id(f["character"]), Z_NONE, -1, self.v.id(f["act"])],
             {0: f["act_index"], 1: f["floor"], 2: f["ascension"], 3: f["gold"], 4: f["hp"], 5: f["max_hp"],
              11: f["potion_slots"], 12: len(f["deck"]), 14: f["room_type"], 15: 0,
              16: max(0, odds[0]), 17: max(0, odds[1]), 18: max(0, odds[2]), 19: f["potion_reward_odds"]})
        # 戦闘の外：プレイヤーの生物の記録と牌組（AddOutOfCombat）
        emit([TK_CREATURE, self.v.id(f["character"]), Z_ALLY, -1, 0], {0: f["hp"], 1: f["max_hp"], 3: 1, 5: 1})
        for c in f["deck"]:
            add_card(c, Z_DECK)
        def add_relic(r: dict, zone: int, wax_order: int = 0) -> None:
            feats = {0: r["amount"], 1: int(r["show_counter"]), 2: int(r["used_up"]), 3: int(r["wax"]),
                     4: int(r["melted"] or r["status"] == RELIC_DISABLED), 5: int(r["status"] == RELIC_ACTIVE), 7: wax_order,
                     9: r.get("wuppy_max_hp") or 0}
            emit([TK_RELIC, self.v.id(r["id"]), zone, -1, 0], feats)

        # レリック（AddRelic）。蝋のレリックは、まだ溶けていないものに先頭から 1, 2, … と順番を付ける
        wax = 0
        for r in f["relics"]:
            if r["wax"] and not r["melted"]:
                wax += 1
            add_relic(r, Z_ALLY, wax if r["wax"] and not r["melted"] else 0)
        # ポーション（AddPotion）
        for p in f["potions"]:
            emit([TK_POTION, self.v.id(p["id"]), Z_ALLY, -1, 0], {0: p["slot"], 1: int(self.t["potions"][p["id"]])})
        # 同じ報酬画面でまだ受け取っていない他の報酬（EncodeBody の d.Shown と Ref）
        for r in f.get("other_rewards", []):
            kind = r["type"]
            if kind == "CardReward":
                emit([TK_OPTION, self.v.id("REWARD:CardReward"), Z_REWARD_GROUP1 + min(r["group"], 2), -1, 0], {})
                for c in r["cards"]:
                    add_card(c, Z_REWARD_GROUP1 + min(r["group"], 2))
            elif kind == "GoldReward":
                emit([TK_OPTION, self.v.id("REWARD:GoldReward"), Z_OFFER, -1, 0], {0: r["amount"]})
            elif kind == "PotionReward":
                emit([TK_POTION, self.v.id(r["potion"]), Z_OFFER, -1, 0], {0: -1, 1: int(self.t["potions"][r["potion"]])})
            elif kind == "RelicReward":
                add_relic(r["relic"], Z_OFFER)
            else:
                emit([TK_OPTION, self.v.id("REWARD:" + kind), Z_OFFER, -1, 0], {})
        # 選択肢（AddChoices）：3 枚はそれぞれのカードの記録を指し、スキップなどは何も指さない
        ci = []
        for c in f["offered"]:
            ci.append([AK_PICK_CARD_REWARD, add_card(c, Z_OFFER), -1, 0, 0])
        for key in f["alternatives"]:
            ci.append([AK_CARD_REWARD_ALT, -1, -1, self.v.id(key), 0])
        visited = self.visited(f)
        self.encounter_pool(f, visited, emit)

        h = np.zeros(HEADER_INTS, np.int32)
        h[0] = fmt; h[1] = DK_CARD_REWARD; h[2] = len(ti); h[3] = len(ci)
        h[6] = f["act_index"]; h[7] = f["floor"]; h[8] = f["hp"]; h[9] = f["max_hp"]; h[10] = f["gold"]
        h[16] = self.v.id(f["bosses"][0]); h[17] = self.v.id(f["bosses"][1])
        act = self.t["acts"][f["act"]]
        h[18] = len(visited["normal"]); h[19] = len(visited["elite"]); h[20] = max(0, act["n_weak"] - len(visited["normal"]))
        h[21], h[22] = self.last_encounters(f)
        total = 2 if f["bosses"][1] else 1
        done = sum(1 for r in f["rooms_this_act"] or [] if r["room_type"] == ROOM_BOSS)
        h[24] = min(done, total); h[25] = max(0, total - done)
        h[26] = 1; h[28] = f["card_removals_used"]; h[29] = removal_price(f)
        h[30] = sum(1 for r in f.get("other_rewards", []) if r["type"] == "CardReward")

        n = len(ti)
        tok_feats = np.zeros((n, TOK_FEATS), np.float32); tok_feats[:] = tf
        if not v27:
            tok_feats[:, 20:] = 0
        cand_ints = np.array(ci, np.int32).reshape(-1, CAND_INTS)
        return Obs(h, np.array(ti, np.int32).reshape(-1, TOK_INTS), tok_feats, np.zeros((n, TOK_PRIV), np.float32),
                   cand_ints, np.zeros((len(ci), CAND_FEATS), np.float32), [], None)

    def enchant_effect(self, card: dict, fmt: int) -> dict | None:
        """エンチャントの効果の表（build_enchant_table.py）の、このカードと強化の回数の分。表に無い組み合わせは None。"""
        by_card = self.t.get("enchant_effects", {}).get(card["enchantment"]["id"], {})
        by_fmt = by_card.get(card["id"])
        if not by_fmt:
            return None
        ups = by_fmt["28" if fmt == 28 else "19"]
        return ups[min(card["upgrade"], len(ups) - 1)]

    def visited(self, f: dict) -> dict:
        """この幕で入った普通戦・精英戦の遭遇（入った順）。ゲームの階ごとの記録の部屋から取る。"""
        act = self.t["acts"][f["act"]]
        pool_normal, pool_elite = set(act["weak"]) | set(act["regular"]), set(act["elite"])
        normal, elite = [], []
        for r in f["rooms_this_act"] or []:
            # イベントの中の戦闘（部屋の種類は普通戦）は、幕の出現リストから引かれないので数えない
            if r["room_type"] == ROOM_MONSTER and r["model_id"] in pool_normal:
                normal.append(r["model_id"])
            elif r["room_type"] == ROOM_ELITE and r["model_id"] in pool_elite:
                elite.append(r["model_id"])
        return {"normal": normal, "elite": elite}

    def last_encounters(self, f: dict) -> tuple[int, int]:
        """この幕で最後に入った普通戦・精英戦の遭遇（RunInfo.cs の ActHistory）。こちらはイベントの中の戦闘も数える。"""
        last = {ROOM_MONSTER: 0, ROOM_ELITE: 0}
        for r in f["rooms_this_act"] or []:
            if r["model_id"] and r["room_type"] in last:
                last[r["room_type"]] = self.v.id(r["model_id"])
        return last[ROOM_MONSTER], last[ROOM_ELITE]

    def encounter_pool(self, f: dict, visited: dict, emit) -> None:
        """この幕の出現リストの記録（RunInfo.cs の EncounterPool）。"""
        act = self.t["acts"][f["act"]]
        tags = self.t["encounter_tags"]
        n_weak = act["n_weak"]
        normal, elite = visited["normal"], visited["elite"]

        def bag(pool: list[str], drawn: list[str]) -> list[str]:
            b: list[str] = []
            for e in drawn:
                if not b:
                    b.extend(pool)
                if e in b:
                    b.remove(e)
            if not b:
                b.extend(pool)
            return b

        def shares(a: str, b: str | None) -> bool:
            return b is not None and bool(set(tags.get(a, [])) & set(tags.get(b, [])))

        def emit3(pool: list[str], slot: int, drawn: list[str], b: list[str] | None, last: str | None) -> None:
            nxt = [e for e in b if e != last and not shares(e, last)] if b is not None else None
            for e in pool:
                feats = {slot: 1, 3: drawn.count(e)}
                if b is not None:
                    feats[4] = int(e in b)
                    feats[5] = 1.0 / len(nxt) if nxt and e in nxt else 0
                emit([TK_ENCOUNTER_POOL, self.v.id(e), Z_NONE, -1, 0], feats)

        last_n = normal[-1] if normal else None
        in_weak = len(normal) < n_weak
        emit3(act["weak"], 0, normal, bag(act["weak"], normal[:n_weak]) if in_weak else None, last_n)
        emit3(act["regular"], 1, normal, None if in_weak else bag(act["regular"], normal[n_weak:]), last_n)
        emit3(act["elite"], 2, elite, bag(act["elite"], elite), elite[-1] if elite else None)


# ゲームの RoomType の値
ROOM_MONSTER, ROOM_ELITE, ROOM_BOSS = 1, 2, 3


def removal_price(f: dict) -> int:
    """商店でカードを削除する次の価格（MerchantCardRemovalEntry.CalcCost）。アセンションの Inflation で基本の価格と値上がり幅が変わる。"""
    inflation = f["ascension"] >= ASCENSION_INFLATION
    price = (100 if inflation else 75) + (50 if inflation else 25) * f["card_removals_used"]
    # 商店の価格を変えるレリック（Hook.ModifyMerchantPrice）。ゲームは掛けた後で整数に切り捨てる
    for r in f["relics"]:
        if r["id"] in MERCHANT_PRICE_FACTOR:
            price *= MERCHANT_PRICE_FACTOR[r["id"]]
    return int(price)


# TheCourier は 20% 引き、MembershipCard は半額（各レリックの Discount）
MERCHANT_PRICE_FACTOR = {"RELIC.THE_COURIER": Fraction(4, 5), "RELIC.MEMBERSHIP_CARD": Fraction(1, 2)}


ASCENSION_INFLATION = 6      # AscensionLevel.Inflation
