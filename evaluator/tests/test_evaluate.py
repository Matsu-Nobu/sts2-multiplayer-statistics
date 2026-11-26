"""Evaluator.evaluate と HTTP の入り口の試験。models/ に ONNX のモデルが要る (README.md「モデルの作り方」)。"""
import copy
import http.client
import json
import threading
import unittest
from pathlib import Path

from sts2eval.evaluate import INVALID_PAYLOAD, OK, UNKNOWN_CARD, UNSUPPORTED_CHARACTER, UNSUPPORTED_VERSION, Evaluator
from sts2eval.server import DualStackServer, make_handler

HERE = Path(__file__).parent
ROOT = HERE.parent
VERSION = "v0.114.0"

# 実際のゲーム (v0.114.0、フール、アセンション 10、2 階) で mod が送った card_reward_offered。
# 提示されたカードは SORROWFUL_STRIKE・CHO_BARBS・ALL_IS_WELL で、プレイヤーは CHO_BARBS を取った。
GAME_EVENT = json.loads((HERE / "fixtures" / "game_event_fool_f2.json").read_text(encoding="utf-8"))


def event(payload: dict, id_: int = 1, floor: int = 2) -> dict:
    return {"id": id_, "player_id": GAME_EVENT["player_id"], "floor": floor, "payload": payload}


class EvaluateTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.evaluator = Evaluator(ROOT / "data", ROOT / "models")

    def test_game_event(self):
        [r] = self.evaluator.evaluate(VERSION, [event(GAME_EVENT["payload"])])
        self.assertEqual(r["status"], OK)
        self.assertEqual(r["model"], "fool_ema_v24181")
        self.assertEqual(len(r["card_probs"]), 3)
        self.assertEqual(list(r["alternative_probs"]), ["Skip"])
        self.assertAlmostEqual(sum(r["card_probs"]) + sum(r["alternative_probs"].values()), 1.0, places=6)
        self.assertEqual(max(range(3), key=lambda i: r["card_probs"][i]), 1)  # CHO_BARBS
        self.assertTrue(-1.0 <= r["value"] <= 1.0)

    def test_unsupported_version(self):
        [r] = self.evaluator.evaluate("v0.0.0", [event(GAME_EVENT["payload"])])
        self.assertEqual(r, {"id": 1, "status": UNSUPPORTED_VERSION})

    def test_unsupported_character(self):
        payload = copy.deepcopy(GAME_EVENT["payload"])
        payload["character_id"] = "UNKNOWN_CHARACTER"
        [r] = self.evaluator.evaluate(VERSION, [event(payload)])
        self.assertEqual(r, {"id": 1, "status": UNSUPPORTED_CHARACTER})

    def test_card_added_after_training(self):
        # OPTIMISM は v0.114.0 で増えたカードで、フールのモデルの語彙 (2,557) より後の ID を持つ
        payload = copy.deepcopy(GAME_EVENT["payload"])
        payload["offered"][0]["card_id"] = "OPTIMISM"
        [r] = self.evaluator.evaluate(VERSION, [event(payload)])
        self.assertEqual(r, {"id": 1, "status": UNKNOWN_CARD, "model": "fool_ema_v24181"})

    def test_card_not_in_vocabulary(self):
        payload = copy.deepcopy(GAME_EVENT["payload"])
        payload["offered"][0]["card_id"] = "NO_SUCH_CARD"
        [r] = self.evaluator.evaluate(VERSION, [event(payload)])
        self.assertEqual(r["status"], UNKNOWN_CARD)

    def test_card_reward_opened_earlier_on_same_floor(self):
        # 同じ報酬画面にカード報酬が 2 つあり、1 つ目を開いて閉じてから 2 つ目を開いた場面
        gold, card_reward = GAME_EVENT["payload"]["rewards"]
        cards_a = GAME_EVENT["payload"]["offered"]
        cards_b = GAME_EVENT["payload"]["deck"][-4:-1]

        def opening(offered: list, a_is_this: bool) -> dict:
            p = copy.deepcopy(GAME_EVENT["payload"])
            p["offered"] = offered
            p["rewards"] = [gold, {**card_reward, "is_this": a_is_this, "cards": cards_a},
                            {**card_reward, "is_this": not a_is_this, "cards": cards_b}]
            return p

        first, second = opening(cards_a, True), opening(cards_b, False)
        alone = self.evaluator.evaluate(VERSION, [event(second, 2)])[0]
        after_first = self.evaluator.evaluate(VERSION, [event(first, 1), event(second, 2)])[1]
        other_floor = self.evaluator.evaluate(VERSION, [event(first, 1, floor=1), event(second, 2)])[1]
        # 先に開いたカード報酬は入力から外れるので、同じ階で先に開いたときだけ評価が変わる
        self.assertNotEqual(alone["value"], after_first["value"])
        self.assertEqual(alone["value"], other_floor["value"])


class ServerTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = DualStackServer(("::1", 0), make_handler(Evaluator(ROOT / "data", ROOT / "models")))
        threading.Thread(target=cls.server.serve_forever, daemon=True).start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()

    def request(self, method: str, path: str, body: bytes | None = None) -> tuple[int, bytes]:
        conn = http.client.HTTPConnection("::1", self.server.server_address[1])
        conn.request(method, path, body=body, headers={"Content-Type": "application/json"})
        resp = conn.getresponse()
        return resp.status, resp.read()

    def test_healthz(self):
        self.assertEqual(self.request("GET", "/healthz"), (200, b"ok"))

    def test_evaluate(self):
        body = json.dumps({"game_version": VERSION, "events": [event(GAME_EVENT["payload"], 7)]}).encode()
        status, data = self.request("POST", "/evaluate", body)
        self.assertEqual(status, 200)
        [r] = json.loads(data)["results"]
        self.assertEqual((r["id"], r["status"]), (7, OK))

    def test_session_without_game_version(self):
        body = json.dumps({"game_version": None, "events": [event(GAME_EVENT["payload"], 7)]}).encode()
        status, data = self.request("POST", "/evaluate", body)
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(data)["results"], [{"id": 7, "status": UNSUPPORTED_VERSION}])

    def test_broken_json(self):
        self.assertEqual(self.request("POST", "/evaluate", b"{")[0], 400)

    def test_missing_events(self):
        self.assertEqual(self.request("POST", "/evaluate", json.dumps({"game_version": VERSION}).encode())[0], 400)

    def test_payload_of_another_mod_version(self):
        # 形の合わない event だけが invalid_payload になり、同じランのほかの event は評価する
        events = [event({"character_id": "FOOL"}, 1), event(GAME_EVENT["payload"], 2)]
        status, data = self.request("POST", "/evaluate", json.dumps({"game_version": VERSION, "events": events}).encode())
        self.assertEqual(status, 200)
        self.assertEqual([(r["id"], r["status"]) for r in json.loads(data)["results"]], [(1, INVALID_PAYLOAD), (2, OK)])

    def test_event_without_id(self):
        body = json.dumps({"game_version": VERSION, "events": [{"payload": GAME_EVENT["payload"]}]}).encode()
        self.assertEqual(self.request("POST", "/evaluate", body)[0], 422)


if __name__ == "__main__":
    unittest.main()
