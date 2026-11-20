"""Run a list of MCP tool calls against one RoslynMcpServer process over stdio.

Usage:
    python3 mcp_calls.py <server-executable> <repository-root> <calls.json> <output-dir>

calls.json is a list of {"name": "<tool>", "arguments": {...}, "label": "<file label>"}.
"{root}" inside a string argument is replaced with <repository-root>.
Each response is written to <output-dir>/<NN>-<label>.txt.
"""
import json
import os
import subprocess
import sys

exe, root, calls_path, outdir = sys.argv[1:5]
exe = os.path.abspath(exe)
root = os.path.abspath(root)
os.makedirs(outdir, exist_ok=True)

env = dict(os.environ)
env["ROSLYN_MCP_WORKSPACE"] = root
# Keep logs and usage records of this run out of the user's normal data directory.
env["ROSLYN_MCP_HOME"] = os.path.abspath(os.path.join(outdir, "server-home"))

proc = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                        stderr=subprocess.DEVNULL, env=env, cwd=root, text=True, bufsize=1)
next_id = 0


def send(message):
    proc.stdin.write(json.dumps(message) + "\n")
    proc.stdin.flush()


def request(method, params):
    global next_id
    next_id += 1
    send({"jsonrpc": "2.0", "id": next_id, "method": method, "params": params})
    while True:
        line = proc.stdout.readline()
        if not line:
            raise SystemExit("server closed stdout")
        message = json.loads(line)
        if message.get("id") == next_id and ("result" in message or "error" in message):
            return message


def substitute(value):
    if isinstance(value, str):
        return value.replace("{root}", root)
    if isinstance(value, dict):
        return {k: substitute(v) for k, v in value.items()}
    if isinstance(value, list):
        return [substitute(v) for v in value]
    return value


init = request("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                              "clientInfo": {"name": "readme-examples", "version": "1"}})
print("server:", init["result"]["serverInfo"])
send({"jsonrpc": "2.0", "method": "notifications/initialized"})

with open(calls_path, encoding="utf-8") as f:
    calls = json.load(f)

for index, call in enumerate(calls):
    arguments = substitute(call.get("arguments", {}))
    response = request("tools/call", {"name": call["name"], "arguments": arguments})
    if "error" in response:
        text = "ERROR " + json.dumps(response["error"])
    else:
        text = "\n".join(c.get("text", "") for c in response["result"].get("content", []))
    label = call.get("label", call["name"])
    path = os.path.join(outdir, f"{index:02d}-{label}.txt")
    with open(path, "w", encoding="utf-8") as f:
        f.write(f"# {call['name']} {json.dumps(arguments)}\n\n{text}\n")
    print(f"{index:02d} {label} -> {path}")

proc.stdin.close()
try:
    proc.wait(timeout=30)
except subprocess.TimeoutExpired:
    proc.kill()
