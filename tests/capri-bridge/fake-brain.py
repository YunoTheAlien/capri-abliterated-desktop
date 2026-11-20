#!/usr/bin/env python3
"""A pretend brain server for testing Capri without a real model or key, on 127.0.0.1 only.

  python3 fake-brain.py [port] [dump-folder]      (default 8765; with a folder, each request body is saved there)

Point a test profile at it (cross.json: "BrainServer": "http://127.0.0.1:8765", "ServerModel": "fake"). It answers the
OpenAI-style /v1/models and /v1/chat/completions the way a self-hosted model would, with fixed choices so a test is
repeatable:
  "list windows"                        -> list_windows
  "switch to X" / "minimize X" / "close X" -> app_window {name: X, action}
  a tool result                         -> "Done: <start of the result>"
  anything else                         -> "Fake brain here."
"""
import json, re, sys, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
DUMP = sys.argv[2] if len(sys.argv) > 2 else None
COUNT = [0]


def text_of(message):
    content = message.get("content")
    if isinstance(content, list):
        return " ".join(part.get("text", "") for part in content if isinstance(part, dict))
    return content or ""


def choose(messages):
    last = messages[-1] if messages else {}
    if last.get("role") == "tool":
        return {"content": "Done: " + text_of(last)[:160]}
    said = ""
    for m in reversed(messages):
        if m.get("role") == "user":
            said = text_of(m)
            break
    said_low = said.lower()
    if "list windows" in said_low:
        return {"tool": ("list_windows", {})}
    found = re.search(r"\b(switch to|minimi[sz]e|close)\s+(?:the\s+)?(.+?)[.!?]*\s*$", said_low)
    if found:
        verb = found.group(1)
        action = "switch_to" if verb == "switch to" else "minimize" if verb.startswith("minimi") else "close"
        return {"tool": ("app_window", {"name": found.group(2), "action": action})}
    return {"content": "Fake brain here."}


class Brain(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        sys.stderr.write("fake-brain: " + (fmt % args) + "\n")

    def reply(self, body, kind="application/json"):
        data = body.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path.rstrip("/").endswith("/v1/models"):
            return self.reply(json.dumps({"object": "list", "data": [{"id": "fake", "object": "model"}]}))
        self.send_error(404)

    def do_POST(self):
        if not self.path.rstrip("/").endswith("/chat/completions"):
            return self.send_error(404)
        raw = self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}"
        asked = json.loads(raw)
        COUNT[0] += 1
        if DUMP:
            with open("%s/request-%03d.json" % (DUMP, COUNT[0]), "wb") as f:
                f.write(raw)
        picked = choose(asked.get("messages", []))
        message = {"role": "assistant", "content": picked.get("content")}
        finish = "stop"
        if "tool" in picked:
            name, args = picked["tool"]
            message["content"] = None
            message["tool_calls"] = [{"id": "call_%d" % int(time.time() * 1000), "type": "function",
                                      "function": {"name": name, "arguments": json.dumps(args)}}]
            finish = "tool_calls"
        sys.stderr.write("fake-brain: answered %s\n" % json.dumps(message)[:300])
        if asked.get("stream"):
            delta = dict(message)
            if "tool_calls" in delta:
                delta["tool_calls"] = [dict(delta["tool_calls"][0], index=0)]
            chunks = [{"choices": [{"index": 0, "delta": delta, "finish_reason": None}]},
                      {"choices": [{"index": 0, "delta": {}, "finish_reason": finish}]}]
            body = "".join("data: " + json.dumps(dict(c, id="fake", object="chat.completion.chunk", model="fake")) + "\n\n" for c in chunks)
            return self.reply(body + "data: [DONE]\n\n", "text/event-stream")
        self.reply(json.dumps({"id": "fake", "object": "chat.completion", "model": "fake",
                               "choices": [{"index": 0, "message": message, "finish_reason": finish}],
                               "usage": {"prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2}}))


if __name__ == "__main__":
    ThreadingHTTPServer(("127.0.0.1", PORT), Brain).serve_forever()
