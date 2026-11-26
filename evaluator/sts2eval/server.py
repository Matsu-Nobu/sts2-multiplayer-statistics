"""評価サーバーの HTTP の入り口 (docs/spec/card-evaluation.md「評価サーバーの API」)。

使い方: python -m sts2eval.server
環境変数 PORT (既定 8080)、EVALUATOR_DATA (既定 data)、EVALUATOR_MODELS (既定 models)。
"""
from __future__ import annotations

import json
import logging
import os
import socket
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from .evaluate import Evaluator

log = logging.getLogger("sts2eval")

# 1 つのランの card_reward_offered は、1 件数 KB で多くても数十件
MAX_BODY = 8 * 1024 * 1024


def make_handler(evaluator: Evaluator) -> type[BaseHTTPRequestHandler]:
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            if self.path == "/healthz":
                self._send(200, b"ok", "text/plain")
            else:
                self._send(404, b"not found", "text/plain")

        def do_POST(self) -> None:
            if self.path != "/evaluate":
                self._send(404, b"not found", "text/plain")
                return
            # backend からの要求だけを受けるが、壊れた要求でサーバーが落ちないよう、本文は検証してから使う
            try:
                length = int(self.headers.get("Content-Length", "0"))
                if not 0 < length <= MAX_BODY:
                    raise ValueError(f"本文の長さ {length} が範囲外")
                req = json.loads(self.rfile.read(length))
                game_version = req["game_version"]
                events = req["events"]
                if not (game_version is None or isinstance(game_version, str)) or not isinstance(events, list):
                    raise ValueError("game_version か events の型が違う")
            except (ValueError, KeyError, TypeError) as ex:
                self._send(400, str(ex).encode(), "text/plain")
                return
            try:
                results = evaluator.evaluate(game_version or "", events)
            except (KeyError, TypeError, ValueError) as ex:
                log.exception("evaluate failed")
                self._send(422, f"{type(ex).__name__}: {ex}".encode(), "text/plain")
                return
            self._send(200, json.dumps({"results": results}).encode(), "application/json")

        def _send(self, code: int, body: bytes, content_type: str) -> None:
            self.send_response(code)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, fmt: str, *args) -> None:
            log.info("%s %s", self.address_string(), fmt % args)

    return Handler


class DualStackServer(ThreadingHTTPServer):
    # Fly の内部ネットワークは IPv6 なので、IPv6 で待ち受ける。Linux の既定では IPv4 の接続も同じソケットで受ける
    address_family = socket.AF_INET6


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    evaluator = Evaluator(Path(os.environ.get("EVALUATOR_DATA", "data")), Path(os.environ.get("EVALUATOR_MODELS", "models")))
    port = int(os.environ.get("PORT", "8080"))
    server = DualStackServer(("::", port), make_handler(evaluator))
    log.info("listening on [::]:%d, versions %s", port, evaluator.supported_versions())
    server.serve_forever()


if __name__ == "__main__":
    main()
