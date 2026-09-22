#!/usr/bin/env python3
"""Normative workspace-load baseline client (baseline-benchmark-3).

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
import urllib.request
from ctypes import wintypes
from datetime import datetime, timezone
from pathlib import Path

SPEC_TOKEN = "baseline-benchmark-3"
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
HEARTBEAT_S = 10
SDK_INSTALL_URL = "https://dot.net/v1/dotnet-install.ps1"

_log_lock = threading.Lock()
_log_path: Path | None = None


def log(message: str) -> None:
    stamp = datetime.now(timezone.utc).strftime("%H:%M:%S")
    line = f"[{stamp}] {message}"
    with _log_lock:
        print(line, flush=True)
        if _log_path is not None:
            with _log_path.open("a", encoding="utf-8") as handle:
                handle.write(line + "\n")


def progress(phase: str, state: str, elapsed_s: float, detail: str = "") -> None:
    tail = f" {detail}" if detail else ""
    log(f"PROGRESS phase={phase} state={state} elapsed={elapsed_s:.0f}s{tail}")

CORPORA = (
    {
        "id": "orchard-wide",
        "url": "https://github.com/OrchardCMS/OrchardCore.git",
        "sha": "b9c4b2f23e56ef11fbdbd28603c871d1b0fc9deb",
        "tag": "v3.0.1",
        "workspace": "OrchardCore.slnx",
        "symbols": ("ShellSettings", "ManifestConstants"),
    },
    {
        "id": "roslyn-deep",
        "url": "https://github.com/dotnet/roslyn.git",
        "sha": "013d3a758df6c137497ff37a93f0d4bed103853a",
        "tag": "release/stable",
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
    # ModelContextProtocol 1.3 stdio is one JSON object per line, not Content-Length.
    body = json.dumps(message, separators=(",", ":")).encode("utf-8") + b"\n"
    assert proc.stdin is not None
    proc.stdin.write(body)
    proc.stdin.flush()


def _read_frames(stdout, out_q: queue.Queue) -> None:
    buf = b""
    try:
        while True:
            # BufferedReader.read(n) waits until n bytes or EOF. A JSON line is
            # far smaller than that, so the response sits in the pipe forever.
            read = getattr(stdout, "read1", None) or stdout.read
            chunk = read(65536)
            if not chunk:
                out_q.put(None)
                return
            buf += chunk
            while b"\n" in buf:
                raw, buf = buf.split(b"\n", 1)
                line = raw.strip()
                if not line:
                    continue
                out_q.put(json.loads(line.decode("utf-8")))
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


def wait_logged(messages: queue.Queue, expect_id: int, timeout_s: float, label: str) -> dict:
    started = time.perf_counter()
    stop = threading.Event()
    progress(label, "start", 0)

    def beat() -> None:
        while not stop.wait(HEARTBEAT_S):
            progress(label, "running", time.perf_counter() - started, "waiting for MCP response")

    threading.Thread(target=beat, daemon=True).start()
    try:
        return wait_response(messages, expect_id, timeout_s)
    finally:
        stop.set()
        progress(label, "done", time.perf_counter() - started)


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


def run_streaming(
    args: list[str],
    *,
    cwd: Path | None,
    env: dict[str, str],
    timeout_s: float,
    label: str,
    log_path: Path | None = None,
) -> int:
    progress(label, "start", 0, " ".join(args))
    started = time.perf_counter()
    proc = subprocess.Popen(
        args,
        cwd=str(cwd) if cwd else None,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    assert proc.stdout is not None
    pieces: list[str] = []
    last_output = {"at": time.perf_counter(), "text": ""}
    stop = threading.Event()

    def pump() -> None:
        buf = ""
        last_progress = 0.0
        while True:
            chunk = proc.stdout.read(256) if proc.stdout is not None else ""
            if not chunk:
                tail = buf.strip()
                if tail:
                    pieces.append(tail)
                    log(f"{label}: {tail}")
                return
            buf += chunk
            last_output["at"] = time.perf_counter()
            while True:
                newline = buf.find("\n")
                carriage = buf.find("\r")
                if newline < 0 and carriage < 0:
                    break
                if carriage >= 0 and (newline < 0 or carriage < newline):
                    cut = carriage
                    progress = True
                else:
                    cut = newline
                    progress = False
                piece = buf[:cut].strip()
                buf = buf[cut + 1 :]
                if not piece:
                    continue
                pieces.append(piece)
                last_output["text"] = piece[-160:]
                now = time.perf_counter()
                interesting = any(
                    token in piece.lower()
                    for token in ("error", "warning", "fail", "succeeded", "restored")
                )
                if not progress or interesting or now - last_progress >= 5:
                    last_progress = now
                    log(f"{label}: {piece}")

    def beat() -> None:
        while not stop.wait(HEARTBEAT_S):
            quiet = time.perf_counter() - last_output["at"]
            detail = f"quiet={quiet:.0f}s"
            if last_output["text"]:
                detail += f" last={last_output['text']}"
            progress(label, "running", time.perf_counter() - started, detail)

    pump_thread = threading.Thread(target=pump, daemon=True)
    threading.Thread(target=beat, daemon=True).start()
    pump_thread.start()
    timed_out = False
    try:
        code = proc.wait(timeout=timeout_s)
    except subprocess.TimeoutExpired:
        timed_out = True
        code = -1
        kill_tree(proc.pid)
        log(f"{label}: timed out after {timeout_s:.0f}s")
    finally:
        stop.set()
    pump_thread.join(timeout=5)
    elapsed = time.perf_counter() - started
    progress(label, "exit", elapsed, f"code={code}")
    log(f"{label}: exit {code} in {elapsed:.1f}s")
    if log_path is not None:
        write_text(log_path, "\n".join(pieces))
    if timed_out:
        raise subprocess.TimeoutExpired(args, timeout_s)
    return code


def ensure_clone(corpus: dict, dest: Path, env: dict[str, str]) -> str:
    corpus_id = corpus["id"]
    expected = corpus["sha"]
    log(f"{corpus_id} clone: dest={dest} url={corpus['url']} pin={expected}")
    if (dest / ".git").is_dir():
        head = git(dest, ["rev-parse", "HEAD"], env)
        actual = (head.stdout or "").strip()
        if head.returncode == 0 and actual == expected:
            log(f"{corpus_id} clone: reuse existing checkout HEAD {actual}")
            return actual
        message = (
            f"clone at {dest} is {actual or 'unreadable'}, pin is {expected}; "
            "delete that directory and rerun"
        )
        log(f"{corpus_id} clone: {message}")
        raise RuntimeError(message)
    if dest.exists() and any(dest.iterdir()):
        message = f"{dest} exists and is not the pinned clone; delete that directory and rerun"
        log(f"{corpus_id} clone: {message}")
        raise RuntimeError(message)
    dest.mkdir(parents=True, exist_ok=True)
    log(f"{corpus_id} clone: git init")
    init = git(dest, ["init"], env)
    if init.returncode != 0:
        raise RuntimeError(init.stderr.strip() or "git init failed")
    git(dest, ["config", "core.longpaths", "true"], env)
    remote = git(dest, ["remote", "add", "origin", corpus["url"]], env)
    if remote.returncode != 0:
        raise RuntimeError(remote.stderr.strip() or "git remote add failed")
    fetch_code = run_streaming(
        ["git", "fetch", "--progress", "--depth", "1", "origin", expected],
        cwd=dest,
        env=env,
        timeout_s=3600,
        label=f"{corpus_id} git-fetch",
    )
    if fetch_code != 0:
        raise RuntimeError(f"git fetch failed with exit {fetch_code}")
    checkout_code = run_streaming(
        ["git", "checkout", "--force", "FETCH_HEAD"],
        cwd=dest,
        env=env,
        timeout_s=1800,
        label=f"{corpus_id} git-checkout",
    )
    if checkout_code != 0:
        raise RuntimeError(f"git checkout failed with exit {checkout_code}")
    head = git(dest, ["rev-parse", "HEAD"], env)
    actual = (head.stdout or "").strip()
    if actual != expected:
        raise RuntimeError(f"HEAD {actual} != pin {expected}")
    log(f"{corpus_id} clone: HEAD {actual}")
    return actual


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    capped = text
    if len(capped) > RESPONSE_CHAR_CAP:
        capped = capped[:RESPONSE_CHAR_CAP] + "\n\n[truncated by baseline client]\n"
    path.write_text(capped, encoding="utf-8")


def scan_inventory(root: Path, label: str) -> dict:
    multi = 0
    razor_or_web = 0
    analyzer_items = 0
    csproj = 0
    dirs = 0
    started = time.perf_counter()
    last_tick = started
    progress(label, "start", 0, f"root={root}")
    skip = {".git", "bin", "obj", "artifacts", "node_modules"}
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [name for name in dirnames if name not in skip]
        dirs += 1
        now = time.perf_counter()
        if now - last_tick >= HEARTBEAT_S:
            last_tick = now
            progress(
                label,
                "running",
                now - started,
                f"dirs={dirs} csproj={csproj} dir={dirpath}",
            )
        for name in filenames:
            now = time.perf_counter()
            if now - last_tick >= HEARTBEAT_S:
                last_tick = now
                progress(
                    label,
                    "running",
                    now - started,
                    f"dirs={dirs} csproj={csproj} dir={dirpath}",
                )
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
    found = {
        "csprojFiles": csproj,
        "csprojWithTargetFrameworksElement": multi,
        "csprojSdkRazorOrWeb": razor_or_web,
        "csprojOutputItemTypeAnalyzer": analyzer_items,
        "directoryBuildProps": (root / "Directory.Build.props").is_file(),
        "nugetConfig": (root / "NuGet.config").is_file() or (root / "nuget.config").is_file(),
    }
    progress(label, "done", time.perf_counter() - started, f"dirs={dirs} csproj={csproj}")
    return found


def read_global_json(root: Path) -> str:
    path = root / "global.json"
    if not path.is_file():
        return ""
    return path.read_text(encoding="utf-8", errors="replace")


def dotnet_capture(dotnet: str, args: list[str], cwd: Path, env: dict[str, str]) -> tuple[int, str]:
    phase = "dotnet " + " ".join(args)
    started = time.perf_counter()
    stop = threading.Event()
    progress(phase, "start", 0, f"cwd={cwd}")

    def beat() -> None:
        while not stop.wait(HEARTBEAT_S):
            progress(phase, "running", time.perf_counter() - started)

    threading.Thread(target=beat, daemon=True).start()
    try:
        completed = subprocess.run(
            [dotnet, *args],
            cwd=str(cwd),
            env=env,
            capture_output=True,
            text=True,
            timeout=120,
            check=False,
        )
    finally:
        stop.set()
    text = ((completed.stdout or "") + (completed.stderr or "")).strip()
    progress(phase, "done", time.perf_counter() - started, f"exit={completed.returncode}")
    return completed.returncode, text


def parse_sdk(global_json: str) -> tuple[str | None, str | None]:
    if not global_json.strip():
        return None, None
    try:
        sdk = json.loads(global_json).get("sdk") or {}
    except json.JSONDecodeError as ex:
        log(f"global.json is not JSON ({ex}); SDK probe uses the PATH host only")
        return None, None
    version = sdk.get("version")
    roll = sdk.get("rollForward")
    return (
        version if isinstance(version, str) and version else None,
        roll if isinstance(roll, str) and roll else None,
    )


def isolate_dotnet(env: dict[str, str], dotnet_exe: Path) -> dict[str, str]:
    isolated = dict(env)
    root = str(dotnet_exe.parent)
    isolated["DOTNET_ROOT"] = root
    isolated["DOTNET_MULTILEVEL_LOOKUP"] = "0"
    isolated["PATH"] = root + os.pathsep + isolated.get("PATH", "")
    return isolated


def install_sdk(bench_root: Path, version: str) -> Path:
    install_dir = bench_root / "dotnet" / version
    exe = install_dir / "dotnet.exe"
    script = bench_root / "dotnet-install.ps1"
    script.parent.mkdir(parents=True, exist_ok=True)
    if not script.is_file():
        log(f"sdk: download {SDK_INSTALL_URL} -> {script}")
        download_started = time.perf_counter()
        seen = {"at": download_started, "pct": -1}

        def reporthook(count: int, block: int, total: int) -> None:
            got = count * block
            now = time.perf_counter()
            pct = int(100 * got / total) if total > 0 else -1
            if now - seen["at"] < HEARTBEAT_S and pct == seen["pct"]:
                return
            seen["at"] = now
            seen["pct"] = pct
            if total > 0:
                detail = f"{got}/{total} bytes {pct}%"
            else:
                detail = f"{got} bytes"
            progress("sdk-download", "running", now - download_started, detail)

        progress("sdk-download", "start", 0, SDK_INSTALL_URL)
        urllib.request.urlretrieve(SDK_INSTALL_URL, script, reporthook=reporthook)
        progress(
            "sdk-download",
            "done",
            time.perf_counter() - download_started,
            f"bytes={script.stat().st_size}",
        )
    else:
        log(f"sdk: reuse installer {script}")
    log(f"sdk: install exactly {version} into {install_dir}")
    code = run_streaming(
        [
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(script),
            "-Version",
            version,
            "-InstallDir",
            str(install_dir),
        ],
        cwd=bench_root,
        env=os.environ.copy(),
        timeout_s=1800,
        label="sdk-install",
        log_path=install_dir / "install.log",
    )
    if code != 0 or not exe.is_file():
        raise RuntimeError(f"SDK {version} install failed (exit {code}); expected {exe}")
    sdk_dir = install_dir / "sdk" / version
    log(f"sdk: dotnet.exe={exe.is_file()} sdk folder {sdk_dir} exists={sdk_dir.is_dir()}")
    if not sdk_dir.is_dir():
        raise RuntimeError(f"SDK install did not create {sdk_dir}")
    return exe


def describe_host(label: str, dotnet_exe: str, cwd: Path, env: dict[str, str]) -> tuple[int, str, str]:
    code, text = dotnet_capture(dotnet_exe, ["--version"], cwd, env)
    first = text.splitlines()[0].strip() if text else "(empty)"
    log(f"{label}: dotnet={dotnet_exe}")
    log(f"{label}: DOTNET_ROOT={env.get('DOTNET_ROOT') or '(unset)'} MULTILEVEL_LOOKUP={env.get('DOTNET_MULTILEVEL_LOOKUP') or '(unset)'}")
    log(f"{label}: dotnet --version exit {code}: {first}")
    list_code, sdks = dotnet_capture(dotnet_exe, ["--list-sdks"], cwd, env)
    for line in (sdks.splitlines() or ["(no sdk list)"]):
        log(f"{label}: list-sdks exit {list_code}: {line}")
    return code, first, sdks


def prepare_dotnet(
    corpus_id: str,
    bench_root: Path,
    clone: Path,
    global_json: str,
    env: dict[str, str],
    path_dotnet: str,
) -> tuple[dict[str, str], str, dict]:
    requested, roll = parse_sdk(global_json)
    info = {
        "sdkRequested": requested,
        "sdkRollForward": roll,
        "sdkSource": None,
        "dotnetExe": path_dotnet,
        "dotnetVersion": None,
        "sdkList": None,
    }
    log(
        f"{corpus_id} sdk: requested={requested or '(none)'} "
        f"rollForward={roll or '(host default)'}"
    )
    code, resolved, sdks = describe_host(f"{corpus_id} path-host", path_dotnet, clone, env)
    if code == 0:
        info["sdkSource"] = "PATH"
        info["dotnetVersion"] = resolved
        info["sdkList"] = sdks
        log(f"{corpus_id} sdk: PATH host satisfies global.json with {resolved}")
        return env, path_dotnet, info
    if not requested:
        raise RuntimeError(f"PATH dotnet cannot resolve an SDK and global.json has no sdk.version ({resolved})")
    exe = bench_root / "dotnet" / requested / "dotnet.exe"
    if exe.is_file():
        log(f"{corpus_id} sdk: versioned tree already present at {exe}")
    else:
        log(f"{corpus_id} sdk: PATH host does not satisfy global.json; installing {requested}")
        exe = install_sdk(bench_root, requested)
    isolated = isolate_dotnet(env, exe)
    code, resolved, sdks = describe_host(f"{corpus_id} versioned", str(exe), clone, isolated)
    if code != 0:
        log(f"{corpus_id} sdk: versioned tree failed the global.json probe; reinstalling once")
        exe = install_sdk(bench_root, requested)
        isolated = isolate_dotnet(env, exe)
        code, resolved, sdks = describe_host(f"{corpus_id} versioned-reinstall", str(exe), clone, isolated)
    sdk_dir = exe.parent / "sdk" / requested
    if code != 0 or not sdk_dir.is_dir():
        raise RuntimeError(
            f"SDK {requested} at {exe} does not satisfy global.json "
            f"(dotnet --version exit {code}, sdk folder exists={sdk_dir.is_dir()})"
        )
    info["sdkSource"] = "versioned-install"
    info["dotnetExe"] = str(exe)
    info["dotnetVersion"] = resolved
    info["sdkList"] = sdks
    log(f"{corpus_id} sdk: using isolated {exe} resolved={resolved}")
    return isolated, str(exe), info


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
        log(f"{corpus_id} {series}#{index}: server pid={proc.pid}")
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
        log(f"{corpus_id} {series}#{index}: initialize")
        init_resp = wait_logged(messages, init_id, INIT_TIMEOUT_S, f"{corpus_id} {series}#{index} initialize")
        record["startupMs"] = (time.perf_counter() - started) * 1000
        log(f"{corpus_id} {series}#{index}: initialize done in {record['startupMs'] / 1000:.1f}s")
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
        log(f"{corpus_id} {series}#{index}: load_workspace tfm={target_framework or '(omitted)'}")
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
        load_resp = wait_logged(
            messages, load_id, LOAD_TIMEOUT_S, f"{corpus_id} {series}#{index} load_workspace"
        )
        record["loadMs"] = (time.perf_counter() - load_started) * 1000
        load_text, load_error = extract_tool(load_resp)
        write_text(response_dir / f"{corpus_id}-{series}-{index}-load.txt", load_text)
        record["loadOutcome"] = classify_load(load_text, load_error)
        record["projectCount"] = project_count(load_text)
        log(
            f"{corpus_id} {series}#{index}: load_workspace {record['loadOutcome']} "
            f"in {record['loadMs'] / 1000:.1f}s projects={record['projectCount']}"
        )
        if record["loadOutcome"] != "success":
            record["error"] = " ".join(load_text.split())[:500]
            return record
        semantic_text = ""
        for symbol in symbols:
            sem_id = next_id
            next_id += 1
            log(f"{corpus_id} {series}#{index}: find_symbol_definition {symbol}")
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
            sem_resp = wait_logged(
                messages,
                sem_id,
                SEMANTIC_TIMEOUT_S,
                f"{corpus_id} {series}#{index} find_symbol_definition {symbol}",
            )
            elapsed = (time.perf_counter() - sem_started) * 1000
            log(f"{corpus_id} {series}#{index}: find_symbol_definition {symbol} returned in {elapsed / 1000:.1f}s")
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
    log(f"=== {corpus_id} === pin={corpus['sha']} workspace={corpus['workspace']}")
    env = child_env(dest)
    try:
        section["sha"] = ensure_clone(corpus, dest, env)
    except (RuntimeError, subprocess.TimeoutExpired, OSError) as ex:
        section["blocker"] = f"clone: {ex}"
        log(f"BLOCKER {corpus_id}: {section['blocker']}")
        return section
    env = child_env(dest)
    workspace = dest / corpus["workspace"]
    if not workspace.is_file():
        section["blocker"] = f"workspace file missing: {workspace}"
        log(f"BLOCKER {corpus_id}: {section['blocker']}")
        return section
    log(f"{corpus_id} workspace file: {workspace}")
    section["globalJson"] = read_global_json(dest)
    log(f"{corpus_id} inventory: scanning csproj files")
    section["inventory"] = scan_inventory(dest, f"{corpus_id} inventory")
    inventory = section["inventory"]
    log(
        f"{corpus_id} inventory: csproj={inventory['csprojFiles']} "
        f"TargetFrameworks={inventory['csprojWithTargetFrameworksElement']} "
        f"razor/web={inventory['csprojSdkRazorOrWeb']} "
        f"analyzer-items={inventory['csprojOutputItemTypeAnalyzer']}"
    )
    try:
        env, dotnet_exe, sdk_info = prepare_dotnet(corpus_id, bench_root, dest, section["globalJson"], env, dotnet)
    except (RuntimeError, subprocess.TimeoutExpired, OSError) as ex:
        section["blocker"] = f"sdk: {ex}"
        log(f"BLOCKER {corpus_id}: {section['blocker']}")
        return section
    section.update(sdk_info)
    restore_started = time.perf_counter()
    try:
        restore_code = run_streaming(
            [dotnet_exe, "restore", str(workspace)],
            cwd=dest,
            env=env,
            timeout_s=RESTORE_TIMEOUT_S,
            label=f"{corpus_id} dotnet-restore",
            log_path=response_dir / "restore.log",
        )
    except subprocess.TimeoutExpired:
        section["blocker"] = f"dotnet restore exceeded {RESTORE_TIMEOUT_S}s"
        log(f"BLOCKER {corpus_id}: {section['blocker']}")
        return section
    section["restoreExit"] = restore_code
    section["restoreMs"] = (time.perf_counter() - restore_started) * 1000
    if restore_code != 0:
        section["blocker"] = f"dotnet restore exited {restore_code}; see {corpus_id}/restore.log"
        log(f"BLOCKER {corpus_id}: {section['blocker']}")
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
            log(f"{corpus_id} post-restore: missing Compile target, retry targetFramework={tfm}")
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
        log(f"BLOCKER {corpus_id}: {section['blocker']}")
        return section

    if shutil.which("dotnet-trace"):
        log(f"{corpus_id} profiled-warm: dotnet-trace on PATH")
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
        log(f"{corpus_id} profiled-warm skipped: dotnet-trace not on PATH")

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
                log(f"{corpus_id} {section['stoppedEarly']}")
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
        f"- SDK requested: `{section.get('sdkRequested') or '—'}` rollForward `{section.get('sdkRollForward') or '—'}`",
        f"- SDK source: `{section.get('sdkSource') or '—'}`",
        f"- dotnet.exe: `{section.get('dotnetExe') or '—'}`",
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
    try:
        sys.stdout.reconfigure(line_buffering=True)
        sys.stderr.reconfigure(line_buffering=True)
    except (AttributeError, OSError):
        pass
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
    global _log_path
    _log_path = out_dir / "run.log"
    log(f"baseline {SPEC_TOKEN} out={out_dir}")
    log(f"bench-root={bench_root} free={shutil.disk_usage(bench_root).free / 1024**3:.1f} GiB")
    if shutil.disk_usage(bench_root).free < MIN_BENCH_FREE:
        log(f"BLOCKER need >= 20 GiB free on {bench_root}")
        return 1
    if shutil.disk_usage(out_dir).free < MIN_OUT_FREE:
        log(f"BLOCKER need >= 4 GiB free on {out_dir}")
        return 1
    exe = resolve_server(repo, args.server)
    if not exe.is_file():
        log(f"BLOCKER server exe not found: {exe}")
        return 1
    log(f"server={exe} fileVersion={file_version(exe)}")
    dotnet = shutil.which("dotnet")
    if not dotnet:
        log("BLOCKER dotnet not on PATH")
        return 1
    log(f"PATH dotnet={dotnet}")
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
    log(f"REPORT {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
