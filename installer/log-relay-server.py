"""Receives DiscordBar's power.log from the Ally over the LAN so it can be tailed live here.

POST /log  (body = whole log file)  -> written to E:\claudw\inbox\ally-power.log
GET  /     -> "ok"
Run:  python -I log-relay-server.py   (listens on 0.0.0.0:8099)
"""
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from datetime import datetime

OUT = r"E:\claudw\inbox\ally-power.log"
PORT = 8099


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):  # quiet
        pass

    def do_GET(self):
        self.send_response(200); self.end_headers(); self.wfile.write(b"ok")

    def do_POST(self):
        n = int(self.headers.get("Content-Length", "0") or 0)
        body = self.rfile.read(n) if n else b""
        with open(OUT, "wb") as f:
            f.write(body)
        print(f"{datetime.now():%H:%M:%S} received {n} bytes from {self.client_address[0]}", flush=True)
        self.send_response(200); self.end_headers(); self.wfile.write(b"ok")


if __name__ == "__main__":
    print(f"relay listening on 0.0.0.0:{PORT} -> {OUT}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), H).serve_forever()
