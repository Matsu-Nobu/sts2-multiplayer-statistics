"""mod が送る card_reward_offered の payload（docs/spec/api.md「card_reward_offered」）を、encode.py が読む局面の事実に直す。

payload はゲームの値をそのまま持つ。値の無い項目は、mod が JSON にするときに省く。ここで、AlphaDefect の学習のときの扱いに合わせる。
- 名前で送られた列挙値（部屋の種類、状態など）を、ゲームの列挙値の番号に直す。
- ID に語彙の分類（CARD. など）を付ける。
- AlphaDefect のエンジンはゴールドの報酬と、悪い効果の無いレリックの報酬を自動で受け取る（RewardsFlow.cs）。
  学習のときの局面には、受け取っていないこれらの報酬が出てこないので、受け取ったものとして扱う。
- AlphaDefect のエンジンは、一度開いて取らずに閉じたカード報酬を、同じ報酬画面のほかの判断の入力から外す（RewardsFlow.Unclaimed）。
  一度開いたかどうかは、同じ報酬画面で先に届いた card_reward_offered のカードと見比べて決める。
"""
from __future__ import annotations

ROOM_TYPES = ["Unassigned", "Monster", "Elite", "Boss", "Treasure", "Shop", "Event", "RestSite", "Map"]
RELIC_STATUS = ["Normal", "Active", "Disabled"]
ENCHANT_STATUS = ["Normal", "Disabled"]
CARD_TYPES = ["None", "Attack", "Skill", "Power", "Status", "Curse", "Quest"]
RIDERS = ["None", "Sapping", "Violence", "Choking", "Energized", "Wisdom", "Chaos", "Expertise", "Curious", "Improvement"]
ENCOUNTER_ROOMS = {"Monster", "Elite", "Boss"}


def card(c: dict) -> dict:
    enc, aff, tk = c.get("enchantment"), c.get("affliction"), c.get("tinker")
    return {
        "id": "CARD." + c["card_id"], "upgrade": c["upgrade_level"],
        "enchantment": {"id": "ENCHANTMENT." + enc["id"], "amount": enc["amount"], "status": ENCHANT_STATUS.index(enc["status"])} if enc else None,
        "affliction": {"id": "AFFLICTION." + aff["id"], "amount": aff["amount"]} if aff else None,
        "tinker": {"type": CARD_TYPES.index(tk["type"]), "rider": RIDERS.index(tk["rider"])} if tk else None,
        "vars": c["vars"], "keywords": c["keywords"], "cost": c.get("cost"), "star_cost": c["star_cost"],
    }


def relic(r: dict) -> dict:
    return {"id": "RELIC." + r["relic_id"], "amount": r["counter"], "show_counter": r["show_counter"], "used_up": r["used_up"],
            "wax": r["wax"], "melted": r["melted"], "status": RELIC_STATUS.index(r["status"]), "wuppy_max_hp": r.get("max_hp")}


def room(r: dict) -> dict:
    t = r["room_type"]
    mid = r.get("model_id")
    if mid is not None:
        mid = ("ENCOUNTER." if t in ENCOUNTER_ROOMS else "EVENT.") + mid
    return {"room_type": ROOM_TYPES.index(t), "model_id": mid}


def offered_ids(p: dict) -> list[str]:
    """このカード報酬のカードの ID の並び。ほかの card_reward_offered の報酬と見比べるときに使う。"""
    return [c["card_id"] for c in p["offered"]]


def facts(p: dict, floor: int, no_downside: set[str], opened: list[list[str]]) -> dict:
    """floor は event の floor（今の階）。no_downside は悪い効果の無いレリックの ID（no_downside_relics.txt、分類なし）。
    opened は、同じ報酬画面で先に開いたカード報酬のカードの ID の並び（offered_ids）。"""
    gold = p["gold"]
    relics = [relic(r) for r in p["relics"]]
    others = []
    card_rewards = [r for r in p["rewards"] if r["type"] == "CardReward"]
    for r in p["rewards"]:
        if r["is_this"] or r["taken"]:
            continue
        kind = r["type"]
        if kind == "GoldReward":
            gold += r["amount"]
        elif kind == "RelicReward" and not r["linked"] and r["relic"]["relic_id"] in no_downside:
            relics.append(relic(r["relic"]))
        elif kind == "CardReward":
            if [c["card_id"] for c in r["cards"]] in opened:
                continue
            others.append({"type": kind, "group": card_rewards.index(r), "cards": [card(c) for c in r["cards"]]})
        elif kind == "PotionReward":
            others.append({"type": kind, "potion": "POTION." + r["potion_id"]})
        elif kind == "RelicReward":
            others.append({"type": kind, "relic": relic(r["relic"])})
        else:
            others.append({"type": kind})
    return {
        "character": "CHARACTER." + p["character_id"], "ascension": p["ascension"],
        "act_index": p["act_index"], "act": "ACT." + p["act_id"], "floor": floor,
        "gold": gold, "hp": p["hp"], "max_hp": p["max_hp"],
        "room_type": ROOM_TYPES.index(p["room_type"]) if p.get("room_type") else 0,
        "bosses": [("ENCOUNTER." + b) if b else None for b in p["boss_ids"]],
        "unknown_odds": [p["unknown_room_odds"]["monster"], p["unknown_room_odds"]["treasure"], p["unknown_room_odds"]["shop"]],
        "potion_reward_odds": p["potion_reward_odds"], "potion_slots": p["potion_slots"],
        "potions": [{"id": "POTION." + x["potion_id"], "slot": x["slot"]} for x in p["potions"]],
        "deck": [card(c) for c in p["deck"]], "relics": relics,
        "card_removals_used": p["card_shop_removals_used"],
        "rooms_this_act": [room(r) for r in p["rooms_this_act"]] if "rooms_this_act" in p else None,
        "offered": [card(c) for c in p["offered"]],
        "alternatives": ["ALT:" + a.upper() for a in p["alternatives"]],
        "other_rewards": others,
    }
