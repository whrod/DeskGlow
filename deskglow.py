import ctypes
import json
import secrets
import socket
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


TOKEN = secrets.token_urlsafe(24)
PAGE = Path(__file__).with_name("index.html")
VOLUME_KEYS = {"down": 0xAE, "up": 0xAF, "mute": 0xAD}


def execute(action):
    if action == "site":
        return webbrowser.open("https://www.google.com/")
    key = VOLUME_KEYS.get(action)
    if key is None:
        return False
    keyboard = ctypes.windll.user32
    keyboard.keybd_event(key, 0, 0, 0)
    keyboard.keybd_event(key, 0, 2, 0)
    return True


class Handler(BaseHTTPRequestHandler):
    def reply(self, status, content_type, body):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/":
            self.reply(200, "text/html; charset=utf-8", PAGE.read_bytes())
        else:
            self.send_error(404)

    def do_POST(self):
        if self.path != "/action":
            self.send_error(404)
            return
        supplied = self.headers.get("Authorization", "").encode()
        if not secrets.compare_digest(supplied, f"Bearer {TOKEN}".encode()):
            self.send_error(401)
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 128:
                raise ValueError
            action = json.loads(self.rfile.read(length))
            if not isinstance(action, str) or action not in (*VOLUME_KEYS, "site"):
                raise ValueError
        except (ValueError, UnicodeError):
            self.send_error(400)
            return
        if execute(action):
            self.reply(200, "application/json", b'{"ok":true}')
        else:
            self.send_error(500)


if __name__ == "__main__":
    addresses = set(socket.gethostbyname_ex(socket.gethostname())[2])
    with ThreadingHTTPServer(("0.0.0.0", 8765), Handler) as server:
        for address in [*sorted(addresses - {"127.0.0.1"}), "127.0.0.1"]:
            print(f"http://{address}:8765/#{TOKEN}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
