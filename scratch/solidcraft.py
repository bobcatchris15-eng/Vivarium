import socket
import json
import os
import sys

class SolidCraftClient:
    def __init__(self, port=7878, host='127.0.0.1'):
        self.host = host
        self.port = port
        self.sock = None
        self.reader = None
        self.msg_id = 0
        self.token = self._load_token()
        self._connect()

    def _load_token(self):
        token_env = os.environ.get("SOLIDCRAFT_CONTROL_TOKEN")
        if token_env:
            return token_env.strip()
        token_file = os.path.expandvars(rf'%LOCALAPPDATA%\SolidCraft\control\{self.port}.token')
        if os.path.exists(token_file):
            with open(token_file, 'r', encoding='utf-8') as f:
                return f.read().strip()
        raise RuntimeError(f"Token file not found at {token_file} and SOLIDCRAFT_CONTROL_TOKEN not set")

    def _connect(self):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.settimeout(60.0)
        self.sock.connect((self.host, self.port))
        self.reader = self.sock.makefile('r', encoding='utf-8')
        # First handshake call with token
        self.msg_id += 1
        payload = {
            "id": self.msg_id,
            "method": "ui.inspect",
            "params": {},
            "token": self.token
        }
        self.sock.sendall((json.dumps(payload) + "\n").encode('utf-8'))
        line = self.reader.readline()
        if not line:
            raise ConnectionError("Server closed connection during handshake")
        res = json.loads(line)
        if not res.get("ok"):
            raise RuntimeError(f"Auth error: {res.get('error')}")

    def call(self, method, params=None):
        if params is None:
            params = {}
        self.msg_id += 1
        payload = {
            "id": self.msg_id,
            "method": method,
            "params": params
        }
        self.sock.sendall((json.dumps(payload) + "\n").encode('utf-8'))
        line = self.reader.readline()
        if not line:
            raise ConnectionError("Server closed connection")
        res = json.loads(line)
        if not res.get("ok"):
            raise RuntimeError(f"SolidCraft error in {method}: {res.get('error')}")
        return res.get("result")

    def execute(self, command, params=None):
        return self.call("engine.execute", {"command": command, "params": params or {}})

    def batch(self, commands):
        return self.call("engine.batch", {"commands": commands, "rollback": True})

    def inspect(self):
        return self.call("document.inspect")

    def ui_inspect(self):
        return self.call("ui.inspect")

    def commands(self):
        return self.call("engine.commands")

    def screenshot(self, path):
        abs_path = os.path.abspath(path).replace('\\', '/')
        return self.call("ui.screenshot", {"path": abs_path})

    def render(self, path, width=1280, height=720):
        abs_path = os.path.abspath(path).replace('\\', '/')
        return self.call("ui.render", {"path": abs_path, "width": width, "height": height})

    def close(self):
        if self.sock:
            self.sock.close()
            self.sock = None
