"""Observe Codex app-server MCP discovery without creating threads or running a model."""

import argparse
import json
import os
from pathlib import Path
import queue
import signal
import subprocess
import threading
import time
from datetime import datetime, timezone

from measure import ROOT, alive, ordinary, write


def run(codex, output, event):
    directory = output / event
    directory.mkdir()
    probe = ROOT / "spikes/HostSurvival/bin/Release/net10.0/HostSurvival.exe"
    probe_arguments = ["serve", str(directory)] + ([] if event == "discovery-completed" else ["hold"])
    arguments = [str(codex), "-c", "mcp_servers.host_survival.command=" + json.dumps(str(probe)),
                 "-c", "mcp_servers.host_survival.args=" + json.dumps(probe_arguments),
                 "app-server", "--stdio"]
    process = subprocess.Popen(arguments, cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE)
    messages = queue.Queue()
    errors = []
    threading.Thread(target=ordinary._read_frames, args=(process.stdout, messages, [0]), daemon=True).start()
    threading.Thread(target=ordinary.drain_stderr, args=(process.stderr, errors), daemon=True).start()
    observation = {"event": event, "harnessPid": process.pid}
    try:
        ordinary.send_message(process, {"id": 1, "method": "initialize", "params": {
            "clientInfo": {"name": "host-survival", "version": "1"},
            "capabilities": {"experimentalApi": True}}})
        response = ordinary.wait_response(messages, 1, 30)
        assert "result" in response, "Codex initialize must succeed"
        ordinary.send_message(process, {"method": "initialized"})
        ordinary.send_message(process, {"id": 2, "method": "mcpServerStatus/list",
                                       "params": {"serverName": "host_survival"}})
        if event == "discovery-completed":
            response = ordinary.wait_response(messages, 2, 60)
            assert "result" in response, "Codex MCP discovery must succeed"
            observation["discovery"] = response["result"]
        else:
            deadline = time.monotonic() + 20
            while time.monotonic() < deadline:
                logs = [path.read_text() for path in directory.glob("*.jsonl")]
                if any('"discovery-hold"' in log for log in logs) and len(logs) >= 3:
                    break
                time.sleep(0.1)
            else:
                raise TimeoutError("The live MCP and both child processes did not reach the observation barrier")
            observation["discovery"] = "pending at resources/list; no model or thread created"
        time.sleep(1)
        starts = {}
        for path in directory.glob("*.jsonl"):
            events = [json.loads(line) for line in path.read_text().splitlines()]
            first = events[0]
            starts[first["pid"]] = {"pid": first["pid"], "kind": first["data"].get("kind"),
                                    "mode": first["data"]["mode"]}
            for record in events:
                if record["name"] == "child-start" and record["data"]["pid"]:
                    child = record["data"]
                    starts.setdefault(child["pid"], {"pid": child["pid"], "kind": child["kind"], "mode": "child"})
        assert any(record["mode"] == "serve" for record in starts.values()), "Codex must start the probe"
        observation["beforeEvent"] = [dict(record, alive=alive(record["pid"])) for record in starts.values()]
        if event != "discovery-completed":
            assert all(record["alive"] for record in observation["beforeEvent"]), "Processes must be alive at the event"
        observation["eventUtc"] = datetime.now(timezone.utc).isoformat()
        started = time.monotonic()
        if event == "mcp-kill":
            mcp = next(record for record in starts.values() if record["mode"] == "serve")
            assert alive(mcp["pid"]), "The recorded MCP must still be alive before termination"
            # Only the MCP is terminated: backend lifetime must not substitute for MCP lifetime.
            os.kill(mcp["pid"], signal.SIGTERM)
            time.sleep(2)
            assert process.poll() is None, "Codex backend must remain alive during this observation"
            observation["backendAliveAfterMcpKill"] = True
            observation["afterEvent"] = [dict(record, alive=alive(pid)) for pid, record in starts.items()]
            assert not alive(mcp["pid"]), "The MCP must have exited"
            return observation
        elif event in ("stdio-eof", "discovery-completed"):
            process.stdin.close()
        else:
            process.kill()
        try:
            process.wait(timeout=10)
            observation["harnessExitMs"] = round((time.monotonic() - started) * 1000, 1)
        except subprocess.TimeoutExpired:
            observation["harnessExitMs"] = None
            observation["harnessExitTimeoutSeconds"] = 10
        time.sleep(2)
        observation["afterEvent"] = [{"pid": pid, "alive": alive(pid)} for pid in starts]
        return observation
    finally:
        if process.poll() is None:
            process.kill()
            process.wait(timeout=10)
        (directory / "stop").touch()
        deadline = time.monotonic() + 5
        while any(alive(int(path.stem)) for path in directory.glob("*.jsonl")) and time.monotonic() < deadline:
            time.sleep(0.1)
        assert not any(alive(int(path.stem)) for path in directory.glob("*.jsonl")), "All probe processes must stop"
        observation["cleanupComplete"] = True


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--codex", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--event", choices=["mcp-kill"])
    args = parser.parse_args()
    args.out = args.out.resolve()
    args.out.mkdir(parents=True, exist_ok=False)
    results = []
    version = subprocess.run([str(args.codex), "--version"], capture_output=True, text=True, timeout=5, check=True).stdout.strip()
    for event in ([args.event] if args.event else ("discovery-completed", "stdio-eof", "harness-kill")):
        results.append(run(args.codex, args.out, event))
        results[-1]["codexVersion"] = version
        write(args.out / "codex-lifecycle.json", results)
        print(f"Codex {event} observed", flush=True)
