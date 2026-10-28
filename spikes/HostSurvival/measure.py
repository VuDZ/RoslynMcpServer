"""Windows memory observations and a bounded lifecycle probe; no production changes."""

import argparse
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import queue
import re
import subprocess
import threading
import time
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "ordinary", ROOT / "scripts/workspace-state-cache/ordinary_baseline.py"
)
ordinary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ordinary)


class Session:
    def __init__(self, executable, cwd, gc_mode, hook=None, inventory=None):
        env = os.environ.copy()
        # Keep the host environment, but remove inherited experiment overrides.
        for name in list(env):
            if name.upper().startswith(("ROSLYN_MCP_", "DOTNET_GC", "COMPLUS_GC")):
                del env[name]
        env.pop("DOTNET_STARTUP_HOOKS", None)
        env["DOTNET_gcServer"] = str(gc_mode)
        env["ROSLYN_MCP_TOOL_PROFILE"] = "full"
        if hook:
            env["DOTNET_STARTUP_HOOKS"] = str(hook)
            env["HOST_SURVIVAL_INVENTORY"] = str(inventory)
        self.process = subprocess.Popen(
            [str(executable)], cwd=cwd, env=env,
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        )
        self.messages = queue.Queue()
        self.request_id = 0
        self.stderr = []
        threading.Thread(target=ordinary._read_frames,
                         args=(self.process.stdout, self.messages, [0]), daemon=True).start()
        threading.Thread(target=ordinary.drain_stderr,
                         args=(self.process.stderr, self.stderr), daemon=True).start()
        try:
            self.request("initialize", {
                "protocolVersion": "2025-11-25", "capabilities": {},
                "clientInfo": {"name": "host-survival-memory", "version": "1"},
            })
            ordinary.send_message(self.process, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        except BaseException:
            self.close()
            raise

    def request(self, method, params):
        self.request_id += 1
        ordinary.send_message(self.process, {
            "jsonrpc": "2.0", "id": self.request_id, "method": method, "params": params,
        })
        result = ordinary.wait_response(self.messages, self.request_id, 300)
        if "error" in result:
            raise RuntimeError("MCP request failed: " + method)
        return result

    def tool(self, name, arguments):
        text, error = ordinary.extract_tool(self.request("tools/call", {"name": name, "arguments": arguments}))
        if error:
            raise RuntimeError("MCP tool failed: " + name)
        return text

    def sample(self, stage):
        values = ordinary.sample_memory(self.process.pid)
        if values is None:
            raise RuntimeError("Process memory is unavailable")
        return {"stage": stage, "utc": datetime.now(timezone.utc).isoformat(),
                "pid": self.process.pid, "workingSet": values[0], "privateBytes": values[1]}

    def close(self):
        if self.process.poll() is None:
            self.process.stdin.close()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                ordinary.kill_tree(self.process.pid)
                self.process.wait(timeout=10)


def write(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def memory(args):
    result = {"utc": datetime.now(timezone.utc).isoformat(),
              "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
              "serverVersion": ordinary.file_version(args.server), "server": str(args.server),
              "workspace": str(args.workspace), "profile": "full", "prewarm": "default on",
              "method": "GetProcessMemoryInfo WorkingSetSize/PrivateUsage; server PID only",
              "settleSeconds": 10, "idleSeconds": 60, "runs": []}
    destination = args.out / "memory.json"
    for run in range(1, 4):
        for mode in ([1, 0] if run % 2 else [0, 1]):
            session = Session(args.server, args.out, mode)
            try:
                time.sleep(10)
                samples = [session.sample("empty-10s")]
                text = session.tool("load_workspace", {
                    "workspacePath": str(args.workspace), "configuration": "Release", "briefOutput": True,
                })
                outcome = ordinary.classify_load(text, False)
                assert outcome == "success", "The measured workspace must load successfully"
                project_count = ordinary.project_count(text)
                time.sleep(10)
                samples.append(session.sample("loaded-10s"))
                query = session.tool("find_symbol_definition", {"symbolName": "SolutionManager", "maxResults": 5})
                assert "SolutionManager" in query, "The semantic query must return its declaration"
                samples.append(session.sample("after-query"))
                time.sleep(60)
                samples.append(session.sample("idle-60s"))
                session.tool("reset_workspace", {})
                time.sleep(60)
                samples.append(session.sample("reset-idle-60s"))
                result["runs"].append({"run": run, "requestedServerGc": bool(mode),
                                       "projectCount": project_count, "samples": samples})
                write(destination, result)
                print(f"memory run={run} gc={mode} complete", flush=True)
            finally:
                session.close()

    for mode in (1, 0):
        inventory = args.out / f"assemblies-gc{mode}.json"
        hook = ROOT / "spikes/HostSurvival/InventoryHook/bin/Release/net10.0/InventoryHook.dll"
        session = Session(args.server, args.out, mode, hook, inventory)
        try:
            deadline = time.monotonic() + 40
            while not inventory.exists() and time.monotonic() < deadline:
                time.sleep(1)
            observed = json.loads(inventory.read_text(encoding="utf-8"))
            assert observed["serverGc"] == bool(mode), "The runtime must honour the requested GC mode"
        finally:
            session.close()


def cohort(args):
    sessions = []
    records = []
    try:
        for index in range(20):
            session = Session(args.server, args.out, 1)
            sessions.append(session)
            workspace = args.workspace if index < 8 else args.second_workspace if index < 16 else None
            if workspace:
                text = session.tool("load_workspace", {
                    "workspacePath": str(workspace), "configuration": "Release", "briefOutput": True,
                })
                assert ordinary.classify_load(text, False) == "success", "Cohort load must succeed"
            records.append({"index": index, "pid": session.process.pid,
                            "workspace": str(workspace) if workspace else None,
                            "loadArguments": {"configuration": "Release", "briefOutput": True} if workspace else None,
                            "lastWork": time.monotonic()})
            print(f"cohort {index + 1}/20 ready", flush=True)
        time.sleep(60)
        for session, record in zip(sessions, records):
            record["idleSeconds"] = round(time.monotonic() - record.pop("lastWork"), 1)
            record.update(session.sample("cohort-idle"))
        write(args.out / "cohort.json", {"synthetic": True, "serverVersion": ordinary.file_version(args.server),
                                       "serverGc": True, "profile": "full", "processes": records})
    finally:
        for session in sessions:
            session.close()


def lifecycle(args):
    executable = args.probe
    observations = []
    for event in ("eof", "parent-kill"):
        directory = args.out / event
        directory.mkdir()
        process = subprocess.Popen([str(executable), "serve", str(directory)], stdin=subprocess.PIPE,
                                   stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        children = []
        try:
            ordinary.send_message(process, {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
                "protocolVersion": "2025-11-25", "capabilities": {},
                "clientInfo": {"name": "control", "version": "1"}}})
            assert json.loads(process.stdout.readline())["result"]["serverInfo"]["name"] == "host-survival-probe"
            for method, field in (("resources/list", "resources"), ("prompts/list", "prompts")):
                ordinary.send_message(process, {"jsonrpc": "2.0", "id": 2, "method": method})
                assert json.loads(process.stdout.readline())["result"][field] == []
            ordinary.send_message(process, {"jsonrpc": "2.0", "id": 3, "method": "unsupported"})
            assert json.loads(process.stdout.readline())["error"]["code"] == -32601
            time.sleep(2)
            events = [json.loads(line) for line in (directory / f"{process.pid}.jsonl").read_text().splitlines()]
            children = [event["data"]["pid"] for event in events
                        if event["name"] == "child-start" and event["data"]["pid"]]
            assert len(children) >= 2, "Ordinary and detached children must start"
            started = time.monotonic()
            if event == "eof":
                process.stdin.close()
            else:
                process.kill()
            process.wait(timeout=10)
            exit_ms = round((time.monotonic() - started) * 1000, 1)
            time.sleep(2)
            observations.append({"event": event, "pid": process.pid, "exitMs": exit_ms,
                                 "exitCode": process.returncode,
                                 "childrenAlive": {pid: alive(pid) for pid in children}})
            if event == "eof":
                assert '"stdin-eof"' in (directory / f"{process.pid}.jsonl").read_text()
                assert process.returncode == 0, "EOF must be distinguished from abrupt termination"
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=10)
            (directory / "stop").touch()
            deadline = time.monotonic() + 5
            while any(alive(pid) for pid in children) and time.monotonic() < deadline:
                time.sleep(0.1)
            assert not any(alive(pid) for pid in children), "Probe children must stop within the cleanup budget"
    write(args.out / "control-lifecycle.json", observations)
    print("lifecycle self-check PASS", flush=True)


def alive(pid):
    if os.name == "nt":
        kernel, _ = ordinary._win_libs()
        kernel.GetExitCodeProcess.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
        kernel.GetExitCodeProcess.restype = ctypes.c_int
        handle = kernel.OpenProcess(0x1000, False, pid)
        if not handle:
            return False
        try:
            code = ctypes.c_ulong()
            return bool(kernel.GetExitCodeProcess(handle, ctypes.byref(code))) and code.value == 259
        finally:
            kernel.CloseHandle(handle)
    try:
        return "\nState:\tZ" not in Path(f"/proc/{pid}/status").read_text()
    except FileNotFoundError:
        return False


def inventory(args):
    sessions = {}
    prefix = re.compile(r"^(\d{4}-\d{2}-\d{2} [\d:.]+ [+-]\d{2}:\d{2}) \[\w+\] \[([^]]+)\] (.*)$")
    for path in sorted(args.logs.glob("mcp-*.log")):
        for line in path.open(encoding="utf-8-sig", errors="replace"):
            match = prefix.match(line)
            if not match:
                continue
            stamp, token, body = match.groups()
            record = sessions.setdefault(token, {"token": token, "first": stamp, "loads": [], "versions": []})
            record["last"] = stamp
            version = re.search(r"Server \(RoslynMcpServer ([\d.]+)\)", body)
            if version and version[1] not in record["versions"]:
                record["versions"].append(version[1])
            if "MCP ← request" not in body or "method=tools/call params=" not in body:
                continue
            try:
                request = json.loads(body.split("params=", 1)[1])
            except json.JSONDecodeError:
                continue
            record["lastTool"] = {"utcOffsetTime": stamp, "name": request.get("name")}
            if request.get("name") == "load_workspace":
                # Whitelist request fields. Never persist client metadata or arbitrary tool arguments.
                arguments = request.get("arguments", {})
                record["loads"].append({"utcOffsetTime": stamp, "arguments": {
                    key: arguments[key] for key in ("workspacePath", "configuration", "platform", "targetFramework",
                                                    "shadowCopyInSolutionAnalyzers", "briefOutput") if key in arguments
                }, "buildArgsPresent": "buildArgs" in arguments})
    assert prefix.match("2026-10-28 01:43:07.650 +03:00 [INF] [codex-test] message")
    assert not prefix.match("unstructured output")
    write(args.out / "historical-sessions.json", {
        "method": "Whitelisted log requests; session intervals do not prove concurrent process liveness",
        "files": [path.name for path in sorted(args.logs.glob("mcp-*.log"))],
        "sessions": list(sessions.values()),
    })
    print(f"log sessions={len(sessions)}; with load requests={sum(bool(r['loads']) for r in sessions.values())}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("memory", "cohort", "lifecycle", "inventory"))
    parser.add_argument("--server", type=Path, default=ROOT / "bin/Release/net10.0/RoslynMcpServer.exe")
    parser.add_argument("--workspace", type=Path, default=ROOT / "RoslynMcpServer.sln")
    parser.add_argument("--second-workspace", type=Path, default=ROOT / "samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--logs", type=Path)
    parser.add_argument("--probe", type=Path,
                        default=ROOT / "spikes/HostSurvival/bin/Release/net10.0" /
                        ("HostSurvival.exe" if os.name == "nt" else "HostSurvival"))
    arguments = parser.parse_args()
    arguments.out = arguments.out.resolve()
    arguments.out.mkdir(parents=True, exist_ok=False)
    if arguments.mode == "inventory" and arguments.logs is None:
        parser.error("inventory requires --logs")
    globals()[arguments.mode](arguments)
