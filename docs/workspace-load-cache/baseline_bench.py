#!/usr/bin/env python3
"""Normative workspace-load baseline client (baseline-benchmark-1).

Timing rules and pins live in baseline-benchmark.md. Do not add flags that
override SHA, timeouts, or which samples enter the median.
"""

from __future__ import annotations

import argparse
import ctypes
import json
import math
import os
import platform
import queue
import re
import shutil
import signal
import subprocess
import sys
import threading
import time
from ctypes import wintypes
from datetime import datetime, timezone
from pathlib import Path

SPEC_TOKEN = "baseline-benchmark-1"
WARM_ATTEMPTS = 10
CONSECUTIVE_FAILURES = 3
RESTORE_TIMEOUT_S = 3600
LOAD_TIMEOUT_S = 2700
SEMANTIC_TIMEOUT_S = 1200
INIT_TIMEOUT_S = 120
TRACE_READY_S = 45
MIN_BENCH_FREE = 20 * 1024**3
MIN_OUT_FREE = 4 * 1024**3
RESPONSE_CHAR_CAP = 2_000_000

CORPORA = (
    {
        "id": "orchard-wide",
        "url": "https://github.com/OrchardCMS/OrchardCore.git",
        "sha": "b9c4b2f23e56ef11fbdbd28603c871d1b0fc9deb",
        "tag": "v3.0.1",
        "workspace": "OrchardCore.sln",
        "symbols": ("ShellSettings", "ManifestConstants"),
    },
    {
        "id": "roslyn-deep",
        "url": "https://github.com/dotnet/roslyn.git",
        "sha": "cf91b80aa2f0eb5b64d7bdb544170d9744883f72",
        "tag": "",
        "workspace": "src/Compilers/CSharp/Portable/Microsoft.CodeAnalysis.CSharp.csproj",
        "symbols": ("CSharpCompilation", "Binder"),
    },
)

CREATE_NEW_PROCESS_GROUP = 0x00000200
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_READ = 0x0010


class PROCESS_MEMORY_COUNTERS_EX(ctypes.Structure):
    _fields_ = [
        ("cb", wintypes.DWORD),
        ("PageFaultCount", wintypes.DWORD),
        ("PeakWorkingSetSize", ctypes.c_size_t),
        ("WorkingSetSize", ctypes.c_size_t),
        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
        ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
        ("PagefileUsage", ctypes.c_size_t),
        ("PeakPagefileUsage", ctypes.c_size_t),
        ("PrivateUsage", ctypes.c_size_t),
    ]


class MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [
        ("dwLength", wintypes.DWORD),
        ("dwMemoryLoad", wintypes.DWORD),
        ("ullTotalPhys", ctypes.c_ulonglong),
        ("ullAvailPhys", ctypes.c_ulonglong),
        ("ullTotalPageFile", ctypes.c_ulonglong),
        ("ullAvailPageFile", ctypes.c_ulonglong),
        ("ullTotalVirtual", ctypes.c_ulonglong),
        ("ullAvailVirtual", ctypes.c_ulonglong),
        ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
    ]


def _win_libs():
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    kernel32.OpenProcess.restype = ctypes.c_void_p
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
    kernel32.GlobalMemoryStatusEx.argtypes = [ctypes.POINTER(MEMORYSTATUSEX)]
    kernel32.GlobalMemoryStatusEx.restype = wintypes.BOOL
    psapi.GetProcessMemoryInfo.argtypes = [
        ctypes.c_void_p,
        ctypes.POINTER(PROCESS_MEMORY_COUNTERS_EX),
        wintypes.DWORD,
    ]
    psapi.GetProcessMemoryInfo.restype = wintypes.BOOL
    return kernel32, psapi


def total_ram_bytes() -> int | None:
    kernel32, _ = _win_libs()
    stat = MEMORYSTATUSEX()
    stat.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
    if not kernel32.GlobalMemoryStatusEx(ctypes.byref(stat)):
        return None
    return int(stat.ullTotalPhys)


def sample_memory(pid: int, kernel32, psapi) -> tuple[int, int] | None:
    handle = kernel32.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid)
    if not handle:
        return None
    try:
        counters = PROCESS_MEMORY_COUNTERS_EX()
        counters.cb = ctypes.sizeof(PROCESS_MEMORY_COUNTERS_EX)
        if not psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
            return None
        return int(counters.WorkingSetSize), int(counters.PrivateUsage)
    finally:
        kernel32.CloseHandle(handle)


def memory_sampler(pid: int, stop: threading.Event, peak: dict) -> None:
    kernel32, psapi = _win_libs()
    while not stop.is_set():
        reading = sample_memory(pid, kernel32, psapi)
        if reading is not None:
            ws, priv = reading
            with peak["lock"]:
                peak["ws"] = max(peak["ws"], ws)
                peak["priv"] = max(peak["priv"], priv)
        stop.wait(2.0)


def file_version(path: Path) -> str:
    quoted = str(path).replace("'", "''")
    command = f"(Get-Item -LiteralPath '{quoted}').VersionInfo.FileVersion"
    try:
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-Command", command],
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as ex:
        return f"unknown ({ex.__class__.__name__})"
    value = (completed.stdout or "").strip()
    return value or "unknown"


def send_message(proc: subprocess.Popen, message: dict) -> None:
    body = json.dumps(message).encode("utf-8")
    header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
    assert proc.stdin is not None
    proc.stdin.write(header)
    proc.stdin.write(body)
    proc.stdin.flush()


def _read_frames(stdout, out_q: queue.Queue) -> None:
    buf = b""
    try:
        while True:
            chunk = stdout.read(65536)
            if not chunk:
                out_q.put(None)
                return
            buf += chunk
            while True:
                sep = buf.find(b"\r\n\r\n")
                if sep < 0:
                    break
                header = buf[:sep].decode("ascii", "replace")
                length = None
                for line in header.split("\r\n"):
                    if line.lower().startswith("content-length:"):
                        length = int(line.split(":", 1)[1].strip())
                if length is None:
                    buf = buf[sep + 4 :]
                    continue
                start = sep + 4
                if len(buf) < start + length:
                    break
                body = buf[start : start + length]
                buf = buf[start + length :]
                out_q.put(json.loads(body.decode("utf-8")))
    except Exception as ex:  # noqa: BLE001 — surface to the attempt record
        out_q.put(ex)


def drain_stderr(pipe, bucket: list[str]) -> None:
    try:
        for raw in iter(pipe.readline, b""):
            bucket.append(raw.decode("utf-8", "replace").rstrip())
            if len(bucket) > 400:
                del bucket[:200]
    except Exception:
        return


def wait_response(messages: queue.Queue, expect_id: int, timeout_s: float) -> dict:
    deadline = time.monotonic() + timeout_s
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError(f"id {expect_id}")
        try:
            item = messages.get(timeout=remaining)
        except queue.Empty as ex:
            raise TimeoutError(f"id {expect_id}") from ex
        if item is None:
            raise EOFError("server stdout closed")
        if isinstance(item, Exception):
            raise item
        if item.get("id") == expect_id:
            return item


def extract_tool(message: dict) -> tuple[str, bool]:
    if "error" in message:
        return json.dumps(message["error"], ensure_ascii=False), True
    result = message.get("result") or {}
    parts: list[str] = []
    for block in result.get("content") or []:
        if isinstance(block, dict) and block.get("type") == "text":
            parts.append(str(block.get("text") or ""))
    text = "\n".join(parts)
    return text, bool(result.get("isError"))


def classify_load(text: str, is_error: bool) -> str:
    if is_error:
        return "failed"
    if "Successfully loaded workspace." in text:
        return "success"
    if "Opened MSBuild graph." in text or "Semantic workspace is unavailable" in text:
        return "graph-only"
    return "failed"


def semantic_ok(text: str, is_error: bool) -> bool:
    return (not is_error) and "declaration symbol(s) matching" in text and "Found " in text


def project_count(text: str) -> int | None:
    match = re.search(r"Found (\d+) projects:", text)
    if not match:
        return None
    return int(match.group(1))


def listed_tfms(text: str) -> list[str]:
    tfms: list[str] = []
    capturing = False
    for line in text.splitlines():
        if "Target frameworks from nearest" in line:
            capturing = True
            continue
        if not capturing:
            continue
        match = re.match(r"- `([^`]+)`\s*$", line.strip())
        if match:
            tfms.append(match.group(1))
            continue
        if tfms:
            break
    return tfms


def choose_tfm(tfms: list[str]) -> str | None:
    if "net10.0" in tfms:
        return "net10.0"
    return tfms[0] if tfms else None


def kill_tree(pid: int | None) -> None:
    if not pid:
        return
    subprocess.run(
        ["taskkill", "/F", "/T", "/PID", str(pid)],
        capture_output=True,
        text=True,
        check=False,
    )


def git(repo: Path, args: list[str], env: dict[str, str], timeout: float = 600) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=str(repo) if repo.exists() else None,
        env=env,
        capture_output=True,
        text=True,
        timeout=timeout,
        check=False,
    )


def ensure_clone(corpus: dict, dest: Path, env: dict[str, str]) -> str:
    expected = corpus["sha"]
    if (dest / ".git").is_dir():
        head = git(dest, ["rev-parse", "HEAD"], env)
        actual = (head.stdout or "").strip()
        if head.returncode == 0 and actual == expected:
            return actual
        raise RuntimeError(
            f"clone at {dest} is {actual or 'unreadable'}, pin is {expected}; refusing to delete"
        )
    if dest.exists() and any(dest.iterdir()):
        raise RuntimeError(f"{dest} exists and is not the pinned clone; refusing to delete")
    dest.mkdir(parents=True, exist_ok=True)
    init = git(dest, ["init"], env)
    if init.returncode != 0:
        raise RuntimeError(init.stderr.strip() or "git init failed")
    git(dest, ["config", "core.longpaths", "true"], env)
    remote = git(dest, ["remote", "add", "origin", corpus["url"]], env)
    if remote.returncode != 0:
        raise RuntimeError(remote.stderr.strip() or "git remote add failed")
    fetched = git(dest, ["fetch", "--depth", "1", "origin", expected], env, timeout=3600)
    if fetched.returncode != 0:
        raise RuntimeError((fetched.stderr or fetched.stdout or "git fetch failed").strip())
    checked = git(dest, ["checkout", "--force", "FETCH_HEAD"], env)
    if checked.returncode != 0:
        raise RuntimeError((checked.stderr or "git checkout failed").strip())
    head = git(dest, ["rev-parse", "HEAD"], env)
    actual = (head.stdout or "").strip()
    if actual != expected:
        raise RuntimeError(f"HEAD {actual} != pin {expected}")
    return actual


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    capped = text
    if len(capped) > RESPONSE_CHAR_CAP:
        capped = capped[:RESPONSE_CHAR_CAP] + "\n\n[truncated by baseline client]\n"
    path.write_text(capped, encoding="utf-8")


def scan_inventory(root: Path) -> dict:
    multi = 0
    razor_or_web = 0
    analyzer_items = 0
    csproj = 0
    skip = {".git", "bin", "obj", "artifacts", "node_modules"}
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [name for name in dirnames if name not in skip]
        for name in filenames:
            if not name.endswith(".csproj"):
                continue
            csproj += 1
            try:
                text = Path(dirpath, name).read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            if "<TargetFrameworks>" in text:
                multi += 1
            if "Microsoft.NET.Sdk.Razor" in text or "Microsoft.NET.Sdk.Web" in text:
                razor_or_web += 1
            if 'OutputItemType="Analyzer"' in text or "OutputItemType='Analyzer'" in text:
                analyzer_items += 1
    return {
        "csprojFiles": csproj,
        "csprojWithTargetFrameworksElement": multi,
        "csprojSdkRazorOrWeb": razor_or_web,
        "csprojOutputItemTypeAnalyzer": analyzer_items,
        "directoryBuildProps": (root / "Directory.Build.props").is_file(),
        "nugetConfig": (root / "NuGet.config").is_file() or (root / "nuget.config").is_file(),
    }


def read_global_json(root: Path) -> str:
    path = root / "global.json"
    if not path.is_file():
        return ""
    return path.read_text(encoding="utf-8", errors="replace")


def dotnet_version(dotnet: str, cwd: Path, env: dict[str, str]) -> tuple[int, str]:
    completed = subprocess.run(
        [dotnet, "--version"],
        cwd=str(cwd),
        env=env,
        capture_output=True,
        text=True,
        timeout=120,
        check=False,
    )
    text = ((completed.stdout or "") + (completed.stderr or "")).strip()
    return completed.returncode, text


def restore(dotnet: str, workspace: Path, cwd: Path, env: dict[str, str], log_path: Path) -> tuple[int, float]:
    started = time.perf_counter()
    completed = subprocess.run(
        [dotnet, "restore", str(workspace)],
        cwd=str(cwd),
        env=env,
        capture_output=True,
        text=True,
        timeout=RESTORE_TIMEOUT_S,
        check=False,
    )
    elapsed_ms = (time.perf_counter() - started) * 1000
    write_text(
        log_path,
        (completed.stdout or "") + "\n----- stderr -----\n" + (completed.stderr or ""),
    )
    return completed.returncode, elapsed_ms


def start_trace(pid: int, out_path: Path) -> tuple[subprocess.Popen | None, str]:
    exe = shutil.which("dotnet-trace")
    if not exe:
        return None, "not-installed"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        proc = subprocess.Popen(
            [
                exe,
                "collect",
                "-p",
                str(pid),
                "--profile",
                "cpu-sampling",
                "-o",
                str(out_path),
            ],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            creationflags=CREATE_NEW_PROCESS_GROUP,
            text=True,
        )
    except OSError as ex:
        return None, f"failed-to-start: {ex}"
    ready = False
    assert proc.stdout is not None
    deadline = time.monotonic() + TRACE_READY_S

    def watch() -> None:
        nonlocal ready
        assert proc.stdout is not None
        for line in proc.stdout:
            if "Press " in line or "Output:" in line or out_path.name in line:
                ready = True

    thread = threading.Thread(target=watch, daemon=True)
    thread.start()
    while time.monotonic() < deadline and proc.poll() is None and not ready:
        time.sleep(0.2)
    if proc.poll() is not None or not ready:
        kill_tree(proc.pid)
        return None, "failed-to-start"
    return proc, "started"


def stop_trace(proc: subprocess.Popen | None, nettrace: Path, summary_path: Path) -> str:
    if proc is None:
        return "not-run"
    previous = signal.getsignal(signal.SIGBREAK)
    try:
        signal.signal(signal.SIGBREAK, signal.SIG_IGN)
        os.kill(proc.pid, signal.CTRL_BREAK_EVENT)
    except OSError:
        kill_tree(proc.pid)
    finally:
        signal.signal(signal.SIGBREAK, previous)
    try:
        proc.wait(timeout=90)
    except subprocess.TimeoutExpired:
        kill_tree(proc.pid)
        return "failed"
    if not nettrace.is_file():
        return "failed"
    reporter = shutil.which("dotnet-trace")
    if not reporter:
        return "trace-file-only"
    completed = subprocess.run(
        [reporter, "report", str(nettrace), "topN", "-n", "25"],
        capture_output=True,
        text=True,
        timeout=180,
        check=False,
    )
    write_text(summary_path, (completed.stdout or "") + (completed.stderr or ""))
    if completed.returncode != 0:
        return "trace-file-only"
    return "report-written"


def run_attempt(
    *,
    exe: Path,
    repo_root: Path,
    workspace: Path,
    symbols: tuple[str, ...],
    env: dict[str, str],
    series: str,
    index: int,
    target_framework: str | None,
    profile: bool,
    response_dir: Path,
    corpus_id: str,
) -> dict:
    record: dict = {
        "corpus": corpus_id,
        "series": series,
        "index": index,
        "targetFramework": target_framework,
        "loadOutcome": "crashed",
        "semanticOutcome": "skipped",
        "symbol": None,
        "startupMs": None,
        "loadMs": None,
        "semanticMs": None,
        "usefulMs": None,
        "pidMs": None,
        "projectCount": None,
        "peakWorkingSetBytes": None,
        "peakPrivateBytes": None,
        "profile": "not-requested",
        "error": None,
    }
    proc: subprocess.Popen | None = None
    messages: queue.Queue = queue.Queue()
    stderr_lines: list[str] = []
    stop_mem = threading.Event()
    peak = {"ws": 0, "priv": 0, "lock": threading.Lock()}
    trace_proc: subprocess.Popen | None = None
    nettrace = response_dir / f"{corpus_id}-{series}-{index}.nettrace"
    next_id = 1
    load_text = ""
    try:
        proc = subprocess.Popen(
            [str(exe)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            cwd=str(repo_root),
            env=env,
        )
        assert proc.stdout is not None and proc.stderr is not None
        threading.Thread(target=_read_frames, args=(proc.stdout, messages), daemon=True).start()
        threading.Thread(target=drain_stderr, args=(proc.stderr, stderr_lines), daemon=True).start()
        threading.Thread(
            target=memory_sampler, args=(proc.pid, stop_mem, peak), daemon=True
        ).start()
        started = time.perf_counter()
        init_id = next_id
        next_id += 1
        send_message(
            proc,
            {
                "jsonrpc": "2.0",
                "id": init_id,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "workspace-load-baseline", "version": SPEC_TOKEN},
                },
            },
        )
        init_resp = wait_response(messages, init_id, INIT_TIMEOUT_S)
        record["startupMs"] = (time.perf_counter() - started) * 1000
        if "error" in init_resp:
            record["loadOutcome"] = "failed"
            record["error"] = json.dumps(init_resp["error"])[:2000]
            return record
        send_message(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        if profile:
            trace_proc, record["profile"] = start_trace(proc.pid, nettrace)
        arguments: dict = {"workspacePath": str(workspace), "briefOutput": True}
        if target_framework:
            arguments["targetFramework"] = target_framework
        load_id = next_id
        next_id += 1
        load_started = time.perf_counter()
        send_message(
            proc,
            {
                "jsonrpc": "2.0",
                "id": load_id,
                "method": "tools/call",
                "params": {"name": "load_workspace", "arguments": arguments},
            },
        )
        load_resp = wait_response(messages, load_id, LOAD_TIMEOUT_S)
        record["loadMs"] = (time.perf_counter() - load_started) * 1000
        load_text, load_error = extract_tool(load_resp)
        write_text(response_dir / f"{corpus_id}-{series}-{index}-load.txt", load_text)
        record["loadOutcome"] = classify_load(load_text, load_error)
        record["projectCount"] = project_count(load_text)
        if record["loadOutcome"] != "success":
            record["error"] = " ".join(load_text.split())[:500]
            return record
        semantic_text = ""
        for symbol in symbols:
            sem_id = next_id
            next_id += 1
            sem_started = time.perf_counter()
            send_message(
                proc,
                {
                    "jsonrpc": "2.0",
                    "id": sem_id,
                    "method": "tools/call",
                    "params": {
                        "name": "find_symbol_definition",
                        "arguments": {"symbolName": symbol},
                    },
                },
            )
            sem_resp = wait_response(messages, sem_id, SEMANTIC_TIMEOUT_S)
            elapsed = (time.perf_counter() - sem_started) * 1000
            semantic_text, sem_error = extract_tool(sem_resp)
            write_text(
                response_dir / f"{corpus_id}-{series}-{index}-semantic-{symbol}.txt",
                semantic_text,
            )
            if semantic_ok(semantic_text, sem_error):
                record["semanticOutcome"] = "success"
                record["semanticMs"] = elapsed
                record["symbol"] = symbol
                record["usefulMs"] = record["loadMs"] + elapsed
                record["pidMs"] = record["startupMs"] + record["usefulMs"]
                return record
        record["semanticOutcome"] = "not-found"
        record["error"] = " ".join(semantic_text.split())[:500]
        return record
    except TimeoutError as ex:
        record["loadOutcome"] = "timeout" if record["loadMs"] is None else record["loadOutcome"]
        if record["loadMs"] is not None and record["semanticOutcome"] == "skipped":
            record["semanticOutcome"] = "timeout"
        record["error"] = str(ex)
        return record
    except (EOFError, OSError, json.JSONDecodeError) as ex:
        record["loadOutcome"] = "crashed"
        record["error"] = f"{ex.__class__.__name__}: {ex}"
        return record
    finally:
        if trace_proc is not None or record["profile"] == "started":
            summary = response_dir / f"{corpus_id}-{series}-{index}-trace.txt"
            try:
                record["profile"] = stop_trace(trace_proc, nettrace, summary)
            except Exception as ex:  # noqa: BLE001 — profile shutdown must not drop timings
                record["profile"] = f"failed: {ex.__class__.__name__}"
        stop_mem.set()
        with peak["lock"]:
            record["peakWorkingSetBytes"] = peak["ws"] or None
            record["peakPrivateBytes"] = peak["priv"] or None
        if proc is not None:
            kill_tree(proc.pid)
        if stderr_lines and record["loadOutcome"] in {"failed", "crashed", "timeout", "graph-only"}:
            err_path = response_dir / f"{corpus_id}-{series}-{index}-stderr.txt"
            write_text(err_path, "\n".join(stderr_lines[-200:]))
        record["tfmCandidates"] = listed_tfms(load_text)


def full_ok(attempt: dict) -> bool:
    return attempt["loadOutcome"] == "success" and attempt["semanticOutcome"] == "success"


def median(values: list[float]) -> float | None:
    xs = sorted(values)
    count = len(xs)
    if count == 0:
        return None
    if count % 2 == 1:
        return xs[count // 2]
    return (xs[count // 2 - 1] + xs[count // 2]) / 2.0


def p95(values: list[float]) -> float | None:
    xs = sorted(values)
    count = len(xs)
    if count == 0:
        return None
    rank = math.ceil(0.95 * count)
    return xs[rank - 1]


def fmt_s(ms: float | None) -> str:
    if ms is None:
        return "—"
    return f"{ms / 1000:.2f} s"


def fmt_mb(value: int | None) -> str:
    if not value:
        return "—"
    return f"{value / (1024 * 1024):.0f} MB"


def stat_line(label: str, values: list[float]) -> str:
    if len(values) < WARM_ATTEMPTS:
        status = f"insufficient ({len(values)}/{WARM_ATTEMPTS})"
    else:
        status = f"n={len(values)}"
    return (
        f"| {label} | {status} | {fmt_s(median(values))} | {fmt_s(p95(values))} | "
        f"{fmt_s(min(values) if values else None)} | {fmt_s(max(values) if values else None)} |"
    )


def child_env(repo_root: Path) -> dict[str, str]:
    env = os.environ.copy()
    env["ROSLYN_MCP_WORKSPACE"] = str(repo_root)
    env["DOTNET_CLI_UI_LANGUAGE"] = "en"
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    env["GIT_TERMINAL_PROMPT"] = "0"
    return env


def run_corpus(
    corpus: dict,
    *,
    exe: Path,
    bench_root: Path,
    out_dir: Path,
    dotnet: str,
) -> dict:
    corpus_id = corpus["id"]
    dest = bench_root / "clones" / corpus_id
    response_dir = out_dir / corpus_id
    response_dir.mkdir(parents=True, exist_ok=True)
    section: dict = {
        "id": corpus_id,
        "url": corpus["url"],
        "pin": corpus["sha"],
        "tag": corpus["tag"],
        "workspace": corpus["workspace"],
        "blocker": None,
        "sha": None,
        "dotnetVersion": None,
        "globalJson": "",
        "inventory": None,
        "restoreExit": None,
        "restoreMs": None,
        "targetFramework": None,
        "attempts": [],
    }
    env = child_env(dest)
    try:
        section["sha"] = ensure_clone(corpus, dest, env)
    except (RuntimeError, subprocess.TimeoutExpired, OSError) as ex:
        section["blocker"] = f"clone: {ex}"
        return section
    env = child_env(dest)
    workspace = dest / corpus["workspace"]
    if not workspace.is_file():
        section["blocker"] = f"workspace file missing: {workspace}"
        return section
    section["globalJson"] = read_global_json(dest)
    section["inventory"] = scan_inventory(dest)
    code, version_text = dotnet_version(dotnet, dest, env)
    section["dotnetVersion"] = version_text
    if code != 0:
        section["blocker"] = f"dotnet --version exited {code}: {version_text[:1500]}"
        return section
    try:
        restore_code, restore_ms = restore(
            dotnet, workspace, dest, env, response_dir / "restore.log"
        )
    except subprocess.TimeoutExpired:
        section["blocker"] = f"dotnet restore exceeded {RESTORE_TIMEOUT_S}s"
        return section
    section["restoreExit"] = restore_code
    section["restoreMs"] = restore_ms
    if restore_code != 0:
        section["blocker"] = f"dotnet restore exited {restore_code}; see {corpus_id}/restore.log"
        return section

    tfm: str | None = None
    first = run_attempt(
        exe=exe,
        repo_root=dest,
        workspace=workspace,
        symbols=corpus["symbols"],
        env=env,
        series="post-restore",
        index=0,
        target_framework=None,
        profile=False,
        response_dir=response_dir,
        corpus_id=corpus_id,
    )
    section["attempts"].append(first)
    if (
        first["loadOutcome"] == "failed"
        and "missing Compile target" in (first.get("error") or "")
        and first.get("tfmCandidates")
    ):
        chosen = choose_tfm(first["tfmCandidates"])
        if chosen:
            tfm = chosen
            retried = run_attempt(
                exe=exe,
                repo_root=dest,
                workspace=workspace,
                symbols=corpus["symbols"],
                env=env,
                series="post-restore",
                index=1,
                target_framework=tfm,
                profile=False,
                response_dir=response_dir,
                corpus_id=corpus_id,
            )
            section["attempts"].append(retried)
            first = retried
    section["targetFramework"] = tfm
    if not full_ok(first) and first["loadOutcome"] != "success":
        section["blocker"] = f"post-restore load {first['loadOutcome']}: {first.get('error')}"
        return section

    if shutil.which("dotnet-trace"):
        profiled = run_attempt(
            exe=exe,
            repo_root=dest,
            workspace=workspace,
            symbols=corpus["symbols"],
            env=env,
            series="profiled-warm",
            index=0,
            target_framework=tfm,
            profile=True,
            response_dir=response_dir,
            corpus_id=corpus_id,
        )
        section["attempts"].append(profiled)
    else:
        section["profileNote"] = "dotnet-trace not on PATH; profiled-warm skipped"

    consecutive = 0
    for index in range(WARM_ATTEMPTS):
        attempt = run_attempt(
            exe=exe,
            repo_root=dest,
            workspace=workspace,
            symbols=corpus["symbols"],
            env=env,
            series="warm",
            index=index,
            target_framework=tfm,
            profile=False,
            response_dir=response_dir,
            corpus_id=corpus_id,
        )
        section["attempts"].append(attempt)
        if full_ok(attempt):
            consecutive = 0
        else:
            consecutive += 1
            if consecutive >= CONSECUTIVE_FAILURES:
                section["stoppedEarly"] = (
                    f"stopped warm series after {CONSECUTIVE_FAILURES} consecutive non-success attempts"
                )
                break
    return section


def render_attempt_row(attempt: dict) -> str:
    useful = attempt.get("usefulMs")
    return (
        f"| {attempt['series']} | {attempt['index']} | {attempt['loadOutcome']} | "
        f"{attempt['semanticOutcome']} | {fmt_s(attempt.get('startupMs'))} | "
        f"{fmt_s(attempt.get('loadMs'))} | {fmt_s(attempt.get('semanticMs'))} | "
        f"{fmt_s(useful)} | {fmt_mb(attempt.get('peakWorkingSetBytes'))} | "
        f"{attempt.get('symbol') or '—'} | {attempt.get('targetFramework') or '—'} |"
    )


def render_section(section: dict) -> str:
    lines = [
        f"## {section['id']}",
        "",
        f"- URL: `{section['url']}`",
        f"- Pin: `{section['pin']}`" + (f" (tag `{section['tag']}`)" if section.get("tag") else ""),
        f"- Checked out: `{section.get('sha') or '—'}`",
        f"- Workspace: `{section['workspace']}`",
        f"- targetFramework passed: `{section.get('targetFramework') or '(omitted)'}`",
        f"- dotnet --version: `{section.get('dotnetVersion') or '—'}`",
        f"- Restore exit: `{section.get('restoreExit')}` in {fmt_s(section.get('restoreMs'))}",
    ]
    if section.get("blocker"):
        lines.append(f"- Blocker: {section['blocker']}")
    if section.get("stoppedEarly"):
        lines.append(f"- {section['stoppedEarly']}")
    if section.get("profileNote"):
        lines.append(f"- {section['profileNote']}")
    inventory = section.get("inventory") or {}
    if inventory:
        lines.extend(
            [
                f"- csproj files: {inventory['csprojFiles']}",
                f"- csproj with TargetFrameworks: {inventory['csprojWithTargetFrameworksElement']}",
                f"- Razor/Web SDK csproj: {inventory['csprojSdkRazorOrWeb']}",
                f"- Analyzer OutputItemType csproj: {inventory['csprojOutputItemTypeAnalyzer']}",
                f"- Directory.Build.props: {inventory['directoryBuildProps']}",
                f"- NuGet.config: {inventory['nugetConfig']}",
            ]
        )
    project_counts = [
        attempt["projectCount"]
        for attempt in section["attempts"]
        if attempt.get("projectCount") is not None
    ]
    if project_counts:
        lines.append(f"- Roslyn project instances on a load response: {project_counts[0]}")
    compare = "|".join(
        [
            section["pin"],
            str(section.get("dotnetVersion") or ""),
            str(section.get("targetFramework") or ""),
        ]
    )
    lines.append(f"- Compare key (sha|dotnet|tfm): `{compare}`")
    lines.append("")
    if section.get("globalJson"):
        lines.extend(["### global.json", "", "```json", section["globalJson"].rstrip(), "```", ""])
    warm = [attempt for attempt in section["attempts"] if attempt["series"] == "warm"]
    load_vals = [a["loadMs"] for a in warm if a["loadOutcome"] == "success" and a.get("loadMs") is not None]
    sem_vals = [a["semanticMs"] for a in warm if a["semanticOutcome"] == "success" and a.get("semanticMs") is not None]
    useful_vals = [a["usefulMs"] for a in warm if full_ok(a) and a.get("usefulMs") is not None]
    lines.extend(
        [
            "### Warm median",
            "",
            "В выборку входят только `series=warm`. `post-restore` и `profiled-warm` ниже отдельно.",
            "Median/p95 считаются при n ≥ 10 успешных значений; иначе строка помечена insufficient.",
            "p95 — nearest-rank, `ceil(0.95 * n)`.",
            "",
            "| Метрика | n | median | p95 | min | max |",
            "|---|---|---|---|---|---|",
            stat_line("load_workspace", load_vals),
            stat_line("find_symbol_definition", sem_vals),
            stat_line("useful (load + semantic)", useful_vals),
            "",
        ]
    )
    load_med = median(load_vals) if len(load_vals) >= WARM_ATTEMPTS else None
    sem_med = median(sem_vals) if len(sem_vals) >= WARM_ATTEMPTS else None
    if load_med is not None and sem_med is not None and (load_med + sem_med) > 0:
        share = 100.0 * sem_med / (load_med + sem_med)
        lines.append(
            f"Доля semantic в сумме warm median load+semantic: **{share:.0f}%** "
            f"(load {fmt_s(load_med)}, semantic {fmt_s(sem_med)})."
        )
        lines.append("")
    lines.extend(
        [
            "### Attempts",
            "",
            "| series | # | load | semantic | startup | load | semantic | useful | peak WS | symbol | TFM |",
            "|---|---|---|---|---|---|---|---|---|---|---|",
        ]
    )
    for attempt in section["attempts"]:
        lines.append(render_attempt_row(attempt))
    lines.append("")
    return "\n".join(lines)


def render_report(meta: dict, sections: list[dict]) -> str:
    lines = [
        "# Workspace load baseline",
        "",
        f"Spec token: `{SPEC_TOKEN}`.",
        "",
        "U-ARB-03 остаётся открытым. Этот прогон не утверждает численный budget,",
        "hit-rate и public activation. Медианы двух корпусов не складываются.",
        "",
        "## Host",
        "",
        f"- When: `{meta['when']}`",
        f"- Machine: `{meta['machine']}`",
        f"- OS: `{meta['os']}`",
        f"- Processors: `{meta['processors']}`",
        f"- RAM: `{fmt_mb(meta.get('ramBytes'))}`",
        f"- Server: `{meta['server']}`",
        f"- Server file version: `{meta['fileVersion']}`",
        f"- Parent MSBUILD_EXE_PATH set: `{meta['msbuildExeSet']}`",
        f"- Parent MSBuildSDKsPath set: `{meta['msbuildSdksSet']}`",
        f"- DOTNET_ROOT: `{meta.get('dotnetRoot') or '(unset)'}`",
        "",
    ]
    for section in sections:
        lines.append(render_section(section))
    lines.extend(
        [
            "## Не измерялось",
            "",
            "Cache miss/hit, capture, edit, build и live-сценарии из verification §5",
            "в этом прогоне отсутствуют: дискового кеша загрузки ещё нет.",
            "",
        ]
    )
    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description="Workspace load baseline (do not override pins).")
    parser.add_argument("--repo", type=Path, default=repo)
    parser.add_argument("--bench-root", type=Path, default=repo.parent / "roslyn-mcp-bench")
    parser.add_argument("--out", type=Path, default=None)
    parser.add_argument("--server", type=Path, default=None)
    parser.add_argument("--corpus", choices=["all", "orchard-wide", "roslyn-deep"], default="all")
    return parser.parse_args()


def resolve_server(repo: Path, explicit: Path | None) -> Path:
    if explicit is not None:
        return explicit
    env = os.environ.get("ROSLYN_MCP_SERVER")
    if env:
        return Path(env)
    return repo / "bin" / "Release" / "net10.0" / "win-x64" / "publish" / "RoslynMcpServer.exe"


def main() -> int:
    if sys.platform != "win32":
        print("This baseline client is normative on Windows x64 only.", file=sys.stderr)
        return 1
    args = parse_args()
    repo = args.repo.resolve()
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S") + "-" + platform.node()
    safe_stamp = re.sub(r"[^A-Za-z0-9._-]+", "-", stamp)
    out_dir = (args.out or (repo / "artifacts" / "workspace-load-baseline" / safe_stamp)).resolve()
    bench_root = args.bench_root.resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    bench_root.mkdir(parents=True, exist_ok=True)
    if shutil.disk_usage(bench_root).free < MIN_BENCH_FREE:
        print(f"Need >= 20 GiB free on {bench_root}", file=sys.stderr)
        return 1
    if shutil.disk_usage(out_dir).free < MIN_OUT_FREE:
        print(f"Need >= 4 GiB free on {out_dir}", file=sys.stderr)
        return 1
    exe = resolve_server(repo, args.server)
    if not exe.is_file():
        print(f"Server exe not found: {exe}", file=sys.stderr)
        return 1
    dotnet = shutil.which("dotnet")
    if not dotnet:
        print("dotnet not on PATH", file=sys.stderr)
        return 1
    selected = [c for c in CORPORA if args.corpus == "all" or c["id"] == args.corpus]
    meta = {
        "when": datetime.now(timezone.utc).isoformat(),
        "machine": platform.node(),
        "os": platform.platform(),
        "processors": os.cpu_count(),
        "ramBytes": total_ram_bytes(),
        "server": str(exe),
        "fileVersion": file_version(exe),
        "msbuildExeSet": bool(os.environ.get("MSBUILD_EXE_PATH")),
        "msbuildSdksSet": bool(os.environ.get("MSBuildSDKsPath")),
        "dotnetRoot": os.environ.get("DOTNET_ROOT") or "",
        "spec": SPEC_TOKEN,
    }
    sections = [
        run_corpus(corpus, exe=exe, bench_root=bench_root, out_dir=out_dir, dotnet=dotnet)
        for corpus in selected
    ]
    report = render_report(meta, sections)
    report_path = out_dir / "report.md"
    report_path.write_text(report, encoding="utf-8")
    with (out_dir / "attempts.jsonl").open("w", encoding="utf-8") as handle:
        for section in sections:
            for attempt in section["attempts"]:
                handle.write(json.dumps(attempt, ensure_ascii=False) + "\n")
    (out_dir / "workload.json").write_text(
        json.dumps({"meta": meta, "sections": sections}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(f"REPORT {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
