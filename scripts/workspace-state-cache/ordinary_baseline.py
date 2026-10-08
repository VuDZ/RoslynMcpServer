#!/usr/bin/env python3
"""New-process ordinary baseline for one workspace and one semantic query.

Times tools/call load_workspace and the first useful find_symbol_definition
in a fresh OS process. Publish, restore, and OS file-cache policy stay outside
the timers. This file does not import or modify
docs/backlog/workspace-load-cache/baseline_bench.py.
"""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
import platform
import queue
import re
import subprocess
import sys
import threading
import time
from ctypes import wintypes
from datetime import datetime, timezone
from pathlib import Path

PROTOCOL_ID = "e0-ordinary-baseline-1"
INIT_TIMEOUT_S = 120
LOAD_TIMEOUT_S = 600
SEMANTIC_TIMEOUT_S = 600
HEARTBEAT_S = 10
RESPONSE_CHAR_CAP = 500_000
MIN_MEASURED = 5
DEFAULT_MEASURED = 5
DEFAULT_MAX_MEASURED = 8
CONSECUTIVE_FAILURES = 3

NOT_MEASURED_STAGES = (
    "inputEnumeration",
    "stat",
    "textHashing",
    "binarySample",
    "binaryRead",
    "decode",
    "hydrate",
    "prepare",
    "publication",
    "capture",
    "write",
    "visitedEntries",
    "bytesHashed",
    "uniqueBinaries",
    "dirtyOrReloadedProjects",
    "openOrDtbObservations",
    "diskEnvelope",
)

SKIP_DIRS = {".git", "bin", "obj", "artifacts", "node_modules", ".vs", "TestResults"}
HASH_SUFFIXES = {".cs", ".csproj", ".props", ".targets"}
HASH_NAMES = {
    "nuget.config",
    "global.json",
    "roslynmcp.jsonc",
    "plugin.json",
    "directory.build.props",
    "directory.build.targets",
}

PROJECT_REF_RE = re.compile(
    r"""<ProjectReference\b[^>]*\bInclude\s*=\s*["']([^"']+)["']""",
    re.IGNORECASE,
)
COMPILE_REMOVE_RE = re.compile(
    r"""<Compile\b[^>]*\bRemove\s*=\s*["']([^"']+)["']""",
    re.IGNORECASE,
)
HEALTH_RES = {
    "effectiveConfiguration": re.compile(r"\*\*MSBuild Configuration:\*\*\s+(.+)"),
    "effectivePlatform": re.compile(r"\*\*MSBuild Platform:\*\*\s+(.+)"),
    "effectiveTargetFramework": re.compile(r"\*\*MSBuild TargetFramework:\*\*\s+(.+)"),
    "globalJson": re.compile(r"\*\*global\.json:\*\*\s+(.+)"),
    "pinnedSdk": re.compile(r"\*\*Pinned SDK \(global\.json\):\*\*\s+(.+)"),
    "resolvedSdkDirectory": re.compile(r"\*\*Resolved SDK directory:\*\*\s+(.+)"),
    "restoreAssets": re.compile(r"\*\*Restore assets:\*\*\s+(.+)"),
    "projectsLoaded": re.compile(r"\*\*Projects loaded:\*\*\s+(\d+)"),
}
PROJECT_COUNT_RE = re.compile(r"Found (\d+) projects:")
FULL_NAME_RE = re.compile(r"Full name:\s*(.+)")
FILE_RE = re.compile(r"File:\s*(.+)")
LINE_RE = re.compile(r"Line:\s*(\d+)")
COLUMN_RE = re.compile(r"Column:\s*(\d+)")

CREATE_FLAGS = 0
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


def log(message: str, log_path: Path | None) -> None:
    stamp = datetime.now(timezone.utc).strftime("%H:%M:%S")
    line = f"[{stamp}] {message}"
    print(line, flush=True)
    if log_path is not None:
        with log_path.open("a", encoding="utf-8") as handle:
            handle.write(line + "\n")


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


def sample_memory(pid: int) -> tuple[int, int] | None:
    kernel32, psapi = _win_libs()
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
    while not stop.is_set():
        try:
            reading = sample_memory(pid)
        except Exception:
            reading = None
        if reading is not None:
            working_set, private = reading
            with peak["lock"]:
                peak["ws"] = max(peak["ws"], working_set)
                peak["priv"] = max(peak["priv"], private)
        stop.wait(2.0)


def file_version(path: Path) -> str:
    quoted = str(path).replace("'", "''")
    command = (
        "$i = [System.Diagnostics.FileVersionInfo]::GetVersionInfo("
        f"[System.IO.Path]::GetFullPath('{quoted}')); "
        "Write-Output $i.FileVersion; Write-Output $i.ProductVersion"
    )
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
    lines = [line.strip() for line in (completed.stdout or "").splitlines() if line.strip()]
    if not lines:
        return "unknown"
    if len(lines) == 1:
        return lines[0]
    return f"{lines[0]} (product {lines[1]})"


def cpu_name() -> str:
    command = "(Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)"
    try:
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-Command", command],
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        completed = None
    if completed is not None and completed.returncode == 0:
        name = (completed.stdout or "").strip()
        if name:
            return name
    return platform.processor() or "not-observed"


def run_capture(args: list[str], cwd: Path, timeout_s: float) -> tuple[int, str]:
    try:
        completed = subprocess.run(
            args,
            cwd=str(cwd),
            capture_output=True,
            text=True,
            timeout=timeout_s,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as ex:
        return 1, f"{ex.__class__.__name__}: {ex}"
    text = ((completed.stdout or "") + "\n" + (completed.stderr or "")).strip()
    return completed.returncode, text


def git_text(repo: Path, args: list[str]) -> tuple[int, str]:
    return run_capture(["git", *args], repo, 60)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def canonical_json(value: object) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def clean_md_value(value: str) -> str:
    text = value.strip()
    if len(text) >= 2 and text.startswith("`") and text.endswith("`"):
        text = text[1:-1]
    return text.strip()


def send_message(proc: subprocess.Popen, message: dict) -> None:
    body = json.dumps(message, separators=(",", ":")).encode("utf-8") + b"\n"
    assert proc.stdin is not None
    proc.stdin.write(body)
    proc.stdin.flush()


def _read_frames(stdout, out_q: queue.Queue, junk: list[int]) -> None:
    buf = b""
    try:
        while True:
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
                try:
                    item = json.loads(line.decode("utf-8"))
                except (UnicodeError, json.JSONDecodeError):
                    junk[0] += 1
                    continue
                if isinstance(item, dict):
                    out_q.put(item)
                else:
                    junk[0] += 1
    except Exception as ex:  # noqa: BLE001 — surface reader failure to the attempt
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
    return "\n".join(parts), bool(result.get("isError"))


def classify_load(text: str, is_error: bool) -> str:
    if is_error:
        return "failed"
    if "Successfully loaded workspace." in text:
        return "success"
    if "Opened MSBuild graph." in text or "Semantic workspace is unavailable" in text:
        return "graph-only"
    return "failed"


def parse_health(text: str) -> dict[str, str]:
    found: dict[str, str] = {}
    for key, pattern in HEALTH_RES.items():
        match = pattern.search(text)
        if match:
            found[key] = clean_md_value(match.group(1))
    return found


def project_count(text: str) -> int | None:
    match = PROJECT_COUNT_RE.search(text)
    if not match:
        return None
    return int(match.group(1))


def path_matches(actual: str, expected: Path, repo: Path) -> bool:
    actual_path = Path(actual.strip().strip("`"))
    candidates = [expected]
    if not expected.is_absolute():
        candidates.append(repo / expected)
    actual_parts = [part.casefold() for part in actual_path.parts]
    for candidate in candidates:
        cand_parts = [part.casefold() for part in candidate.parts]
        if actual_parts == cand_parts:
            return True
        if len(actual_parts) >= len(cand_parts) and actual_parts[-len(cand_parts) :] == cand_parts:
            return True
        try:
            if actual_path.resolve() == candidate.resolve():
                return True
        except OSError:
            continue
    return False


def find_expected_declaration(text: str, full_name: str, expect_file: Path, repo: Path) -> dict | None:
    pieces = text.split("Symbol:")
    for piece in pieces[1:]:
        name_match = FULL_NAME_RE.search(piece)
        file_match = FILE_RE.search(piece)
        if name_match is None or file_match is None:
            continue
        if name_match.group(1).strip() != full_name:
            continue
        file_text = file_match.group(1).strip()
        if not path_matches(file_text, expect_file, repo):
            continue
        line_match = LINE_RE.search(piece)
        column_match = COLUMN_RE.search(piece)
        return {
            "fullName": full_name,
            "file": file_text,
            "line": int(line_match.group(1)) if line_match else None,
            "column": int(column_match.group(1)) if column_match else None,
        }
    return None


def kill_tree(pid: int | None) -> None:
    if not pid:
        return
    subprocess.run(
        ["taskkill", "/F", "/T", "/PID", str(pid)],
        capture_output=True,
        text=True,
        check=False,
    )


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    capped = text
    if len(capped) > RESPONSE_CHAR_CAP:
        capped = capped[:RESPONSE_CHAR_CAP] + "\n\n[truncated by ordinary baseline runner]\n"
    path.write_text(capped, encoding="utf-8")


def median(values: list[float]) -> float | None:
    ordered = sorted(values)
    count = len(ordered)
    if count == 0:
        return None
    if count % 2 == 1:
        return ordered[count // 2]
    return (ordered[count // 2 - 1] + ordered[count // 2]) / 2.0


def fmt_ms(value: float | None) -> str:
    if value is None:
        return "—"
    return f"{value:.1f} ms"


def fmt_s(value: float | None) -> str:
    if value is None:
        return "—"
    return f"{value / 1000:.2f} s"


def stat_block(values: list[float]) -> dict:
    if not values:
        return {"n": 0, "median": None, "min": None, "max": None, "medianText": "—", "minText": "—", "maxText": "—"}
    mid = median(values)
    low = min(values)
    high = max(values)
    return {
        "n": len(values),
        "median": mid,
        "min": low,
        "max": high,
        "medianText": f"{fmt_ms(mid)} ({fmt_s(mid)})",
        "minText": f"{fmt_ms(low)} ({fmt_s(low)})",
        "maxText": f"{fmt_ms(high)} ({fmt_s(high)})",
    }


def find_repo(start: Path) -> Path | None:
    current = start if start.is_dir() else start.parent
    for candidate in [current, *current.parents]:
        if (candidate / ".git").exists():
            return candidate
    return None


def read_csproj_versions(repo: Path) -> dict[str, str]:
    path = repo / "RoslynMcpServer.csproj"
    found = {
        "sourceVersion": "not-observed",
        "assemblyVersion": "not-observed",
        "fileVersion": "not-observed",
        "roslynPackage": "not-observed",
        "msbuildPackage": "not-observed",
    }
    if not path.is_file():
        return found
    text = path.read_text(encoding="utf-8", errors="replace")
    for key, tag in (
        ("sourceVersion", "Version"),
        ("assemblyVersion", "AssemblyVersion"),
        ("fileVersion", "FileVersion"),
    ):
        match = re.search(rf"<{tag}>\s*([^<]+)\s*</{tag}>", text)
        if match:
            found[key] = match.group(1).strip()
    roslyn = re.search(
        r"""<PackageReference\s+Include="Microsoft\.CodeAnalysis\.CSharp"\s+Version="([^"]+)""",
        text,
    )
    if roslyn:
        found["roslynPackage"] = roslyn.group(1).strip()
    msbuild = re.search(
        r"""<PackageReference\s+Include="Microsoft\.Build"\s+Version="([^"]+)""",
        text,
    )
    if msbuild:
        found["msbuildPackage"] = msbuild.group(1).strip()
    return found


def remove_prefixes(csproj_text: str) -> list[str]:
    prefixes: list[str] = []
    for raw in COMPILE_REMOVE_RE.findall(csproj_text):
        text = raw.replace("\\", "/").strip()
        if text.endswith("/**"):
            text = text[: -len("/**")]
        text = text.strip("/")
        if text:
            prefixes.append(text)
    return prefixes


def should_hash(path: Path) -> bool:
    if path.suffix.casefold() in HASH_SUFFIXES:
        return True
    return path.name.casefold() in HASH_NAMES


def rel_posix(path: Path, root: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except (OSError, ValueError):
        return path.resolve().as_posix()


def excluded(rel: str, prefixes: list[str]) -> bool:
    for prefix in prefixes:
        if rel == prefix or rel.startswith(prefix + "/"):
            return True
    return False


def walk_project_inputs(project_file: Path, repo: Path, out_dir: Path, into: dict[str, str]) -> None:
    text = project_file.read_text(encoding="utf-8", errors="replace")
    prefixes = remove_prefixes(text)
    project_dir = project_file.parent
    out_resolved = out_dir.resolve()
    for dirpath, dirnames, filenames in os.walk(project_dir):
        current = Path(dirpath)
        kept: list[str] = []
        skip_dirs = {name.casefold() for name in SKIP_DIRS}
        for name in dirnames:
            if name.casefold() in skip_dirs:
                continue
            child = current / name
            try:
                if child.resolve() == out_resolved or out_resolved.is_relative_to(child.resolve()):
                    if child.resolve() == out_resolved:
                        continue
            except (OSError, ValueError):
                pass
            child_rel = rel_posix(child, project_dir)
            if excluded(child_rel, prefixes):
                continue
            kept.append(name)
        dirnames[:] = kept
        for name in filenames:
            file_path = current / name
            if not should_hash(file_path):
                continue
            file_rel = rel_posix(file_path, project_dir)
            if excluded(file_rel, prefixes):
                continue
            try:
                resolved = file_path.resolve()
                if resolved == out_resolved or resolved.is_relative_to(out_resolved):
                    continue
                into[rel_posix(resolved, repo)] = sha256_file(resolved)
            except (OSError, ValueError):
                continue
    directory = project_dir
    repo_resolved = repo.resolve()
    while True:
        for name in ("Directory.Build.props", "Directory.Build.targets", "NuGet.config", "nuget.config", "global.json"):
            candidate = directory / name
            if candidate.is_file():
                try:
                    resolved = candidate.resolve()
                    into[rel_posix(resolved, repo)] = sha256_file(resolved)
                except OSError:
                    continue
        if directory.resolve() == repo_resolved:
            break
        parent = directory.parent
        if parent == directory:
            break
        directory = parent


def content_manifest(workspace: Path, repo: Path, out_dir: Path) -> tuple[dict[str, str], str]:
    suffix = workspace.suffix.casefold()
    files: dict[str, str] = {}
    if suffix == ".slnf":
        raise RuntimeError("refusing a .slnf workspace")
    if suffix in {".sln", ".slnx"}:
        resolved = workspace.resolve()
        files[rel_posix(resolved, repo)] = sha256_file(resolved)
        return files, "solution-file-bytes-only"
    if suffix != ".csproj":
        resolved = workspace.resolve()
        files[rel_posix(resolved, repo)] = sha256_file(resolved)
        return files, "workspace-file-bytes-only"
    pending = [workspace.resolve()]
    seen: set[str] = set()
    while pending:
        project = pending.pop()
        key = str(project).casefold()
        if key in seen or not project.is_file():
            continue
        seen.add(key)
        walk_project_inputs(project, repo, out_dir, files)
        try:
            text = project.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for include in PROJECT_REF_RE.findall(text):
            referenced = (project.parent / include).resolve()
            pending.append(referenced)
    return files, "csproj-and-project-reference-sources"


def manifest_identity(files: dict[str, str]) -> str:
    lines = [f"{path}\t{digest}" for path, digest in sorted(files.items())]
    return sha256_text("\n".join(lines))


def diff_manifests(before: dict[str, str], after: dict[str, str]) -> dict[str, list[str]]:
    before_keys = set(before)
    after_keys = set(after)
    return {
        "added": sorted(after_keys - before_keys),
        "removed": sorted(before_keys - after_keys),
        "changed": sorted(path for path in before_keys & after_keys if before[path] != after[path]),
    }


def differences_empty(diff: dict[str, list[str]]) -> bool:
    return not diff["added"] and not diff["removed"] and not diff["changed"]


def child_env(repo: Path) -> dict[str, str]:
    env = os.environ.copy()
    env["ROSLYN_MCP_WORKSPACE"] = str(repo)
    env["DOTNET_CLI_UI_LANGUAGE"] = "en"
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    return env


def not_measured_map(peak: int | None) -> dict[str, str]:
    stages = {name: "not-measured" for name in NOT_MEASURED_STAGES}
    stages["peakWorkingSet"] = "measured" if peak else "not-measured"
    return stages


def run_attempt(
    *,
    exe: Path,
    repo: Path,
    workspace: Path,
    query: str,
    expect_full_name: str,
    expect_file: Path,
    env: dict[str, str],
    role: str,
    index: int,
    response_dir: Path,
    log_path: Path,
    baseline_files: dict[str, str],
    host_key: dict,
    loader_facts_value: dict,
    evaluated: dict,
) -> dict:
    stem = f"attempt-{index:02d}" + ("-warmup" if role == "warmup" else "")
    record: dict = {
        "protocol": PROTOCOL_ID,
        "role": role,
        "index": index,
        "includedInMedian": False,
        "pid": None,
        "newProcess": True,
        "ramHit": False,
        "fileCacheDropped": False,
        "scenario": "forced-ordinary-baseline",
        "cacheHit": False,
        "outcome": "error",
        "loadOutcome": "crashed",
        "semanticOutcome": "skipped",
        "loadMs": None,
        "semanticMs": None,
        "usefulMs": None,
        "startupMs": None,
        "query": query,
        "tool": "find_symbol_definition",
        "expectFullName": expect_full_name,
        "expectFile": str(expect_file),
        "declaration": None,
        "inputDifferences": {"added": [], "removed": [], "changed": []},
        "contentIdentity": None,
        "contentIdentityScope": None,
        "compareKey": None,
        "error": None,
        "timing": {
            "load": "perf_counter from tools/call load_workspace send until that response",
            "semantic": "perf_counter from tools/call find_symbol_definition send until that response",
            "startup": "process start until initialize response; not in load, semantic, or useful",
            "publishIncluded": False,
            "restoreIncluded": False,
            "firstUseCompilation": "inside the timed calls; not subtracted",
            "evaluatedInstance": "dotnet msbuild -getProperty once before attempts; not in load, semantic, or useful",
        },
        "loadArgumentsOmitted": ["configuration", "platform", "targetFramework", "buildArgs", "shadowCopyInSolutionAnalyzers"],
        "loadArgumentsSent": {"workspacePath": str(workspace), "briefOutput": True},
        "semanticArgumentsSent": {"symbolName": query},
    }
    try:
        files, scope = content_manifest(workspace, repo, response_dir)
    except (OSError, RuntimeError) as ex:
        record["error"] = f"content identity: {ex}"
        record["compareKey"] = dict(host_key)
        return record
    diff = diff_manifests(baseline_files, files)
    identity = manifest_identity(files)
    record["inputDifferences"] = diff
    record["contentIdentity"] = identity
    record["contentIdentityScope"] = scope
    record["inputFileCount"] = len(files)

    proc: subprocess.Popen | None = None
    messages: queue.Queue = queue.Queue()
    stderr_lines: list[str] = []
    junk = [0]
    stop_mem = threading.Event()
    peak = {"ws": 0, "priv": 0, "lock": threading.Lock()}
    load_text = ""
    try:
        proc = subprocess.Popen(
            [str(exe)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            cwd=str(repo),
            env=env,
        )
        record["pid"] = proc.pid
        log(f"{role}#{index}: server pid={proc.pid}", log_path)
        assert proc.stdout is not None and proc.stderr is not None
        threading.Thread(target=_read_frames, args=(proc.stdout, messages, junk), daemon=True).start()
        threading.Thread(target=drain_stderr, args=(proc.stderr, stderr_lines), daemon=True).start()
        threading.Thread(target=memory_sampler, args=(proc.pid, stop_mem, peak), daemon=True).start()
        started = time.perf_counter()
        send_message(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2025-11-25",
                    "capabilities": {},
                    "clientInfo": {"name": "workspace-state-ordinary-baseline", "version": PROTOCOL_ID},
                },
            },
        )
        init_resp = wait_response(messages, 1, INIT_TIMEOUT_S)
        record["startupMs"] = round((time.perf_counter() - started) * 1000, 1)
        if "error" in init_resp:
            record["loadOutcome"] = "failed"
            record["outcome"] = "error"
            record["error"] = json.dumps(init_resp["error"], ensure_ascii=False)[:2000]
            return record
        send_message(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        load_started = time.perf_counter()
        send_message(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 2,
                "method": "tools/call",
                "params": {
                    "name": "load_workspace",
                    "arguments": {"workspacePath": str(workspace), "briefOutput": True},
                },
            },
        )
        try:
            load_resp = wait_response(messages, 2, LOAD_TIMEOUT_S)
        except TimeoutError as ex:
            record["loadMs"] = round((time.perf_counter() - load_started) * 1000, 1)
            record["loadOutcome"] = "timeout"
            record["outcome"] = "timeout"
            record["error"] = str(ex)
            return record
        record["loadMs"] = round((time.perf_counter() - load_started) * 1000, 1)
        load_text, load_error = extract_tool(load_resp)
        write_text(response_dir / f"{stem}-load.txt", load_text)
        record["loadText"] = f"{stem}-load.txt"
        record["loadOutcome"] = classify_load(load_text, load_error)
        record["projectCount"] = project_count(load_text)
        health = parse_health(load_text)
        record["health"] = health
        log(
            f"{role}#{index}: load {record['loadOutcome']} in {record['loadMs']} ms projects={record['projectCount']}",
            log_path,
        )
        if record["loadOutcome"] != "success":
            record["outcome"] = "error"
            record["error"] = " ".join(load_text.split())[:500]
            return record
        semantic_started = time.perf_counter()
        send_message(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 3,
                "method": "tools/call",
                "params": {
                    "name": "find_symbol_definition",
                    "arguments": {"symbolName": query},
                },
            },
        )
        try:
            semantic_resp = wait_response(messages, 3, SEMANTIC_TIMEOUT_S)
        except TimeoutError as ex:
            record["semanticMs"] = round((time.perf_counter() - semantic_started) * 1000, 1)
            record["semanticOutcome"] = "timeout"
            record["outcome"] = "timeout"
            record["error"] = str(ex)
            return record
        record["semanticMs"] = round((time.perf_counter() - semantic_started) * 1000, 1)
        semantic_text, semantic_error = extract_tool(semantic_resp)
        write_text(response_dir / f"{stem}-semantic.txt", semantic_text)
        record["semanticText"] = f"{stem}-semantic.txt"
        declaration = find_expected_declaration(semantic_text, expect_full_name, expect_file, repo)
        record["declaration"] = declaration
        if semantic_error:
            record["semanticOutcome"] = "error"
            record["outcome"] = "error"
            record["error"] = " ".join(semantic_text.split())[:500]
            return record
        if declaration is None:
            record["semanticOutcome"] = "miss"
            record["outcome"] = "miss"
            record["error"] = "expected declaration was not in the response"
            return record
        record["semanticOutcome"] = "success"
        record["outcome"] = "success"
        record["usefulMs"] = round(record["loadMs"] + record["semanticMs"], 1)
        log(
            f"{role}#{index}: semantic success in {record['semanticMs']} ms useful={record['usefulMs']} ms",
            log_path,
        )
        return record
    except TimeoutError as ex:
        if record["loadMs"] is None:
            record["loadOutcome"] = "timeout"
        elif record["semanticOutcome"] == "skipped":
            record["semanticOutcome"] = "timeout"
        record["outcome"] = "timeout"
        record["error"] = str(ex)
        return record
    except (EOFError, OSError, json.JSONDecodeError) as ex:
        record["loadOutcome"] = "crashed" if record["loadMs"] is None else record["loadOutcome"]
        record["outcome"] = "error"
        record["error"] = f"{ex.__class__.__name__}: {ex}"
        return record
    finally:
        stop_mem.set()
        with peak["lock"]:
            record["peakWorkingSetBytes"] = peak["ws"] or None
            record["peakPrivateBytes"] = peak["priv"] or None
        record["stages"] = not_measured_map(record.get("peakWorkingSetBytes"))
        record["stdoutNonJsonLines"] = junk[0]
        if proc is not None:
            kill_tree(proc.pid)
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                kill_tree(proc.pid)
        if stderr_lines and record["outcome"] != "success":
            err_name = f"{stem}-stderr.txt"
            write_text(response_dir / err_name, "\n".join(stderr_lines[-200:]))
            record["stderrText"] = err_name
        health = record.get("health") or {}
        material = compare_material(host_key, record, health, loader_facts_value, evaluated)
        record["compareKey"] = material
        record["compareKeyId"] = sha256_text(canonical_json(material))


ABSENT = "absent"


def value_or_absent(sent: dict, omitted: set[str], name: str) -> str:
    if name in omitted or name not in sent:
        return ABSENT
    value = sent[name]
    if value is None or value == "":
        return ABSENT
    return str(value)


def raw_requested_level(record: dict) -> dict:
    omitted = set(record.get("loadArgumentsOmitted") or [])
    sent = record.get("loadArgumentsSent") or {}
    return {
        "configuration": value_or_absent(sent, omitted, "configuration"),
        "platform": value_or_absent(sent, omitted, "platform"),
        "targetFramework": value_or_absent(sent, omitted, "targetFramework"),
        "buildArgs": value_or_absent(sent, omitted, "buildArgs"),
        "shadowCopyInSolutionAnalyzers": value_or_absent(sent, omitted, "shadowCopyInSolutionAnalyzers"),
    }


def _jsonc_load_keys(path: Path) -> dict[str, str]:
    if not path.is_file():
        return {
            "present": ABSENT,
            "configuration": ABSENT,
            "platform": ABSENT,
            "targetFramework": ABSENT,
        }
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {
            "present": "unparsed",
            "configuration": "not-observed",
            "platform": "not-observed",
            "targetFramework": "not-observed",
        }
    if not isinstance(data, dict):
        return {
            "present": "unparsed",
            "configuration": "not-observed",
            "platform": "not-observed",
            "targetFramework": "not-observed",
        }

    def key(name: str) -> str:
        if name not in data or data[name] is None or str(data[name]).strip() == "":
            return ABSENT
        return str(data[name]).strip()

    return {
        "present": str(path),
        "configuration": key("configuration"),
        "platform": key("platform"),
        "targetFramework": key("target-framework"),
    }


def _prefer(*values: str) -> str:
    for value in values:
        if value != ABSENT:
            return value
    return ABSENT


def loader_facts(repo: Path, exe: Path) -> dict:
    """FileSettings and inherited Loaded* visible before MSBuild open.

    A measured attempt is a new process, so Loaded* starts absent.
    RoslynMcp.jsonc is read at the exe directory and the process cwd; cwd wins.
    """
    exe_keys = _jsonc_load_keys(exe.parent / "RoslynMcp.jsonc")
    cwd_keys = _jsonc_load_keys(repo / "RoslynMcp.jsonc")

    def file_level(name: str) -> str:
        if cwd_keys["present"] not in {ABSENT, "unparsed"} and cwd_keys[name] != ABSENT:
            return cwd_keys[name]
        if exe_keys["present"] not in {ABSENT, "unparsed"} and exe_keys[name] != ABSENT:
            return exe_keys[name]
        if cwd_keys["present"] == "unparsed" or exe_keys["present"] == "unparsed":
            return "not-observed"
        return ABSENT

    return {
        "process": "fresh",
        "inheritedLoaded": {
            "configuration": ABSENT,
            "platform": ABSENT,
            "targetFramework": ABSENT,
            "buildArgs": ABSENT,
        },
        "fileSettings": {
            "executableJsonc": exe_keys["present"],
            "workingDirectoryJsonc": cwd_keys["present"],
            "configuration": file_level("configuration"),
            "platform": file_level("platform"),
            "targetFramework": file_level("targetFramework"),
        },
    }


def merge_loader_globals(raw: dict, facts: dict) -> dict:
    inherited = facts["inheritedLoaded"]
    file_settings = facts["fileSettings"]
    return {
        "configuration": _prefer(raw["configuration"], inherited["configuration"], file_settings["configuration"]),
        "platform": _prefer(raw["platform"], inherited["platform"], file_settings["platform"]),
        "targetFramework": _prefer(
            raw["targetFramework"],
            inherited["targetFramework"],
            file_settings["targetFramework"],
        ),
        "buildArgs": _prefer(raw["buildArgs"], inherited["buildArgs"]),
        "precedence": "passed override, else inherited Loaded* from this process, else RoslynMcp.jsonc (cwd wins over exe)",
    }


def evaluated_instance(workspace: Path, repo: Path, raw: dict) -> dict:
    """Evaluated Configuration/Platform/TFM of the workspace project.

    This is not the load_workspace health line and is not inside loadMs.
    Globals are passed only when this same request sent them.
    """
    args = [
        "dotnet",
        "msbuild",
        str(workspace),
        "-getProperty:Configuration",
        "-getProperty:Platform",
        "-getProperty:TargetFramework",
        "-getProperty:TargetFrameworks",
        "-nologo",
    ]
    if raw["configuration"] != ABSENT:
        args.append(f"-p:Configuration={raw['configuration']}")
    if raw["platform"] != ABSENT:
        args.append(f"-p:Platform={raw['platform']}")
    if raw["targetFramework"] != ABSENT:
        args.append(f"-p:TargetFramework={raw['targetFramework']}")
    code, text = run_capture(args, repo, 180)
    props = None
    start = text.find("{")
    if start >= 0:
        try:
            parsed = json.loads(text[start:])
        except json.JSONDecodeError:
            parsed = None
        if isinstance(parsed, dict) and isinstance(parsed.get("Properties"), dict):
            props = parsed["Properties"]

    def prop(name: str) -> str:
        if props is None or name not in props or props[name] is None:
            return "not-observed"
        return str(props[name])

    return {
        "projectFile": str(workspace),
        "configuration": prop("Configuration"),
        "platform": prop("Platform"),
        "targetFramework": prop("TargetFramework"),
        "targetFrameworks": prop("TargetFrameworks"),
        "globalsPassed": ABSENT
        if raw["configuration"] == ABSENT and raw["platform"] == ABSENT and raw["targetFramework"] == ABSENT
        else "same-as-raw-requested",
        "method": "dotnet msbuild -getProperty; outside load, semantic, and useful",
        "exitCode": code,
    }


def compare_material(
    host_key: dict,
    record: dict,
    health: dict,
    facts: dict,
    evaluated: dict,
) -> dict:
    raw = raw_requested_level(record)
    return {
        "serverCommit": host_key["serverCommit"],
        "serverFileVersion": host_key["serverFileVersion"],
        "sourceVersion": host_key["sourceVersion"],
        "assemblyVersion": host_key["assemblyVersion"],
        "fileVersion": host_key["fileVersion"],
        "os": host_key["os"],
        "cpu": host_key["cpu"],
        "cpuCount": host_key["cpuCount"],
        "ramBytes": host_key["ramBytes"],
        "workspaceRevision": host_key["workspaceRevision"],
        "mutationsAtStart": host_key["mutationsAtStart"],
        "contentIdentity": record.get("contentIdentity"),
        "contentIdentityScope": record.get("contentIdentityScope"),
        "sdkVersion": host_key["sdkVersion"],
        "msbuildVersion": host_key["msbuildVersion"],
        "msbuildPackage": host_key["msbuildPackage"],
        "roslynPackage": host_key["roslynPackage"],
        "rawRequested": raw,
        "loaderGlobalsBeforeOpen": {
            "process": facts["process"],
            "inheritedLoaded": facts["inheritedLoaded"],
            "fileSettings": facts["fileSettings"],
            "merged": merge_loader_globals(raw, facts),
        },
        "evaluatedInstance": {
            "projectFile": evaluated.get("projectFile"),
            "configuration": evaluated.get("configuration", "not-observed"),
            "platform": evaluated.get("platform", "not-observed"),
            "targetFramework": evaluated.get("targetFramework", "not-observed"),
            "targetFrameworks": evaluated.get("targetFrameworks", "not-observed"),
            "globalsPassed": evaluated.get("globalsPassed", "not-observed"),
        },
        "serverHealthObservation": {
            "msbuildConfiguration": health.get("effectiveConfiguration", "not-observed"),
            "msbuildPlatform": health.get("effectivePlatform", "not-observed"),
            "msbuildTargetFramework": health.get("effectiveTargetFramework", "not-observed"),
            "resolvedSdkDirectory": health.get("resolvedSdkDirectory", "not-observed"),
            "pinnedSdk": health.get("pinnedSdk", "not-observed"),
            "role": "load response text; not raw requested, pre-open loader globals, or evaluated instance",
        },
        "semanticMode": "forced-ordinary-baseline",
        "overlayMode": "off",
        "binaryPolicy": "not-applicable",
        "binaryPolicyThreshold": "not-applicable",
        "binaryPolicyChunks": "not-applicable",
        "query": f"find_symbol_definition:{record['query']}",
        "expectFullName": record["expectFullName"],
        "expectFile": record["expectFile"],
        "h001": "external-xml-excluded",
        "h002": "strong-name-variant-B",
        "excludedEmitSigning": "excluded",
        "scenario": "forced-ordinary-baseline",
        "cacheHit": False,
        "xmlDocEquivalence": False,
        "strongNameEquivalence": False,
    }


def tally_outcome(outcome: str, counts: dict[str, int]) -> None:
    if outcome == "success":
        counts["successes"] += 1
    elif outcome == "timeout":
        counts["timeouts"] += 1
    else:
        counts["errors"] += 1
        counts["errorBreakdown"][outcome] = counts["errorBreakdown"].get(outcome, 0) + 1


def build_host_key(repo: Path, exe: Path, versions: dict[str, str]) -> dict:
    code, head = git_text(repo, ["rev-parse", "HEAD"])
    commit = head.strip() if code == 0 and head.strip() else "not-observed"
    status_code, status = git_text(repo, ["status", "--porcelain"])
    mutations = []
    if status_code == 0:
        mutations = [line for line in status.splitlines() if line.strip()]
    else:
        mutations = ["not-observed"]
    dotnet = "dotnet"
    sdk_code, sdk_text = run_capture([dotnet, "--version"], repo, 60)
    sdk_version = sdk_text.splitlines()[0].strip() if sdk_code == 0 and sdk_text else "not-observed"
    msbuild_code, msbuild_text = run_capture([dotnet, "msbuild", "-version"], repo, 120)
    msbuild_version = "not-observed"
    if msbuild_code == 0:
        for line in reversed([line.strip() for line in msbuild_text.splitlines() if line.strip()]):
            if re.search(r"\d+\.\d+", line):
                msbuild_version = line
                break
    return {
        "serverCommit": commit,
        "serverFileVersion": file_version(exe),
        "sourceVersion": versions["sourceVersion"],
        "assemblyVersion": versions["assemblyVersion"],
        "fileVersion": versions["fileVersion"],
        "os": platform.platform(),
        "cpu": cpu_name(),
        "cpuCount": os.cpu_count(),
        "ramBytes": total_ram_bytes(),
        "workspaceRevision": commit,
        "mutationsAtStart": mutations,
        "sdkVersion": sdk_version,
        "msbuildVersion": msbuild_version,
        "msbuildPackage": versions["msbuildPackage"],
        "roslynPackage": versions["roslynPackage"],
        "machine": platform.node(),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Time a new-process ordinary load and the first useful semantic query."
    )
    parser.add_argument("--workspace", type=Path, required=True, help="Solution or project file to load.")
    parser.add_argument("--query", required=True, help="symbolName for find_symbol_definition.")
    parser.add_argument("--server", type=Path, required=True, help="RoslynMcpServer.exe to start.")
    parser.add_argument("--expect-full-name", required=True, help="Declaration full name that counts as useful.")
    parser.add_argument("--expect-file", type=Path, required=True, help="Declaration file that counts as useful.")
    parser.add_argument("--out", type=Path, required=True, help="Directory for raw attempt JSON.")
    parser.add_argument("--repo", type=Path, default=None, help="Repository root. Default: walk up from the workspace.")
    parser.add_argument("--measured", type=int, default=DEFAULT_MEASURED, help="Measured attempts to aim for (minimum 5).")
    parser.add_argument(
        "--max-measured",
        type=int,
        default=DEFAULT_MAX_MEASURED,
        help="Stop after this many measured attempts, including failures.",
    )
    parser.add_argument(
        "--publish-record",
        type=Path,
        default=None,
        help="JSON written by the publisher. Copied into the summary; not added to timings.",
    )
    return parser.parse_args()


def load_publish_record(path: Path | None) -> dict | None:
    if path is None:
        return None
    if not path.is_file():
        raise SystemExit(f"publish record not found: {path}")
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise SystemExit("publish record must be a JSON object")
    data["includedInAttemptTimers"] = False
    return data


def main() -> int:
    try:
        sys.stdout.reconfigure(line_buffering=True)
    except (AttributeError, OSError):
        pass
    if sys.version_info < (3, 11):
        print("Python 3.11+ is required.", file=sys.stderr)
        return 1
    if sys.platform != "win32":
        print("This runner records Windows PIDs and RAM. Another OS needs its own report.", file=sys.stderr)
        return 1
    args = parse_args()
    if args.measured < MIN_MEASURED:
        print(f"--measured must be at least {MIN_MEASURED}.", file=sys.stderr)
        return 1
    if args.max_measured < args.measured:
        print("--max-measured must be >= --measured.", file=sys.stderr)
        return 1
    workspace = args.workspace.resolve()
    exe = args.server.resolve()
    expect_file = args.expect_file
    if workspace.suffix.casefold() == ".slnf":
        print("Refusing a .slnf workspace.", file=sys.stderr)
        return 1
    if not workspace.is_file():
        print(f"Workspace file not found: {workspace}", file=sys.stderr)
        return 1
    if not exe.is_file():
        print(f"Server exe not found: {exe}", file=sys.stderr)
        return 1
    repo = args.repo.resolve() if args.repo else find_repo(workspace)
    if repo is None or not repo.is_dir():
        print("Repository root not found. Pass --repo.", file=sys.stderr)
        return 1
    out_dir = args.out.resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    log_path = out_dir / "run.log"
    log(f"{PROTOCOL_ID} workspace={workspace}", log_path)
    log(f"server={exe}", log_path)
    log("file cache is not dropped; this is a new-process/warm-files run", log_path)
    publish = load_publish_record(args.publish_record.resolve() if args.publish_record else None)
    versions = read_csproj_versions(repo)
    host_key = build_host_key(repo, exe, versions)
    log(f"commit={host_key['serverCommit']} sdk={host_key['sdkVersion']} source={host_key['sourceVersion']}", log_path)
    try:
        baseline_files, scope = content_manifest(workspace, repo, out_dir)
    except (OSError, RuntimeError) as ex:
        print(f"content identity failed: {ex}", file=sys.stderr)
        return 1
    baseline_identity = manifest_identity(baseline_files)
    (out_dir / "content-identity.json").write_text(
        json.dumps(
            {
                "scope": scope,
                "identity": baseline_identity,
                "fileCount": len(baseline_files),
                "files": baseline_files,
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )
    env = child_env(repo)
    omitted_raw = {
        "configuration": ABSENT,
        "platform": ABSENT,
        "targetFramework": ABSENT,
        "buildArgs": ABSENT,
        "shadowCopyInSolutionAnalyzers": ABSENT,
    }
    facts = loader_facts(repo, exe)
    evaluated = evaluated_instance(workspace, repo, omitted_raw)
    log("evaluated instance observed outside attempt timers", log_path)
    (out_dir / "evaluated-instance.json").write_text(
        json.dumps(evaluated, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    attempts: list[dict] = []
    warmup = run_attempt(
        exe=exe,
        repo=repo,
        workspace=workspace,
        query=args.query,
        expect_full_name=args.expect_full_name,
        expect_file=expect_file,
        env=env,
        role="warmup",
        index=0,
        response_dir=out_dir,
        log_path=log_path,
        baseline_files=baseline_files,
        host_key=host_key,
        loader_facts_value=facts,
        evaluated=evaluated,
    )
    warmup["includedInMedian"] = False
    attempts.append(warmup)
    write_attempt(out_dir, warmup)

    counts = {"successes": 0, "errors": 0, "timeouts": 0, "errorBreakdown": {}}
    consecutive_failures = 0
    stopped_early = None
    measured_index = 0
    success_keys: dict[str, int] = {}
    while measured_index < args.max_measured:
        best_group = max(success_keys.values()) if success_keys else 0
        clean = counts["errors"] == 0 and counts["timeouts"] == 0
        if best_group >= args.measured and clean:
            break
        if best_group >= args.measured and measured_index >= args.measured:
            break
        measured_index += 1
        attempt = run_attempt(
            exe=exe,
            repo=repo,
            workspace=workspace,
            query=args.query,
            expect_full_name=args.expect_full_name,
            expect_file=expect_file,
            env=env,
            role="measured",
            index=measured_index,
            response_dir=out_dir,
            log_path=log_path,
            baseline_files=baseline_files,
            host_key=host_key,
            loader_facts_value=facts,
            evaluated=evaluated,
        )
        attempts.append(attempt)
        write_attempt(out_dir, attempt)
        tally_outcome(attempt["outcome"], counts)
        if attempt["outcome"] == "success":
            key = attempt.get("compareKeyId") or ""
            success_keys[key] = success_keys.get(key, 0) + 1
            consecutive_failures = 0
        else:
            consecutive_failures += 1
            if consecutive_failures >= CONSECUTIVE_FAILURES:
                stopped_early = (
                    f"stopped after {CONSECUTIVE_FAILURES} consecutive measured failures"
                )
                log(stopped_early, log_path)
                break

    measured = [item for item in attempts if item["role"] == "measured"]
    successes = [item for item in measured if item["outcome"] == "success"]
    groups: dict[str, list[dict]] = {}
    for item in successes:
        groups.setdefault(item["compareKeyId"], []).append(item)
    median_group: list[dict] = []
    median_key = None
    if groups:
        median_key = max(groups, key=lambda key: (len(groups[key]), -successes.index(groups[key][0])))
        median_group = groups[median_key]
        for item in median_group:
            item["includedInMedian"] = True
            write_attempt(out_dir, item)
    partial_reasons: list[str] = []
    if counts["errors"] or counts["timeouts"]:
        partial_reasons.append("a measured attempt failed, timed out, or missed the expected declaration")
    if len(median_group) < MIN_MEASURED:
        partial_reasons.append(
            f"fewer than {MIN_MEASURED} successful measured attempts share one compare key ({len(median_group)})"
        )
    if len(groups) > 1:
        partial_reasons.append("successful measured attempts do not all share one compare key")
    if stopped_early:
        partial_reasons.append(stopped_early)
    partial = bool(partial_reasons)

    def values(field: str) -> list[float]:
        return [item[field] for item in median_group if isinstance(item.get(field), (int, float))]

    pids = [item.get("pid") for item in attempts]
    measured_pids = [item.get("pid") for item in measured]
    warmup_pid = warmup.get("pid")
    pid_set = [pid for pid in pids if isinstance(pid, int)]
    summary = {
        "protocol": PROTOCOL_ID,
        "when": datetime.now(timezone.utc).isoformat(),
        "partial": partial,
        "partialReasons": partial_reasons,
        "p95": "not-computed; not promised at this sample size",
        "fileCacheDropped": False,
        "scenario": "forced-ordinary-baseline",
        "cacheHit": False,
        "ramHit": False,
        "publish": publish,
        "publishInsideAttemptTimers": False,
        "restore": {
            "performed": False,
            "reason": "control already on disk; restore is outside load timing and was not run",
            "includedInAttemptTimers": False,
        },
        "warmupExcludedFromMedian": True,
        "timeouts": {
            "initializeSeconds": INIT_TIMEOUT_S,
            "loadSeconds": LOAD_TIMEOUT_S,
            "semanticSeconds": SEMANTIC_TIMEOUT_S,
            "note": "timeouts of this runner; archived baseline_bench.py pins were not changed",
        },
        "limits": {
            "h001": "External XML documentation of binary dependencies is excluded. Full XML-doc equivalence is not claimed.",
            "h002": "Strong-name scope is variant B. Exact hidden strong-name state is not claimed.",
            "scenario": "forced ordinary baseline; not a cache hit and not a RAM hit",
            "stages": {name: "not-measured" for name in NOT_MEASURED_STAGES},
            "p95": "not promised; n is below 20",
        },
        "host": host_key,
        "contentIdentityAtStart": baseline_identity,
        "contentIdentityScope": scope,
        "inputFileCount": len(baseline_files),
        "counts": {
            "measuredAttempts": len(measured),
            "successes": counts["successes"],
            "errors": counts["errors"],
            "timeouts": counts["timeouts"],
            "errorBreakdown": counts["errorBreakdown"],
            "medianGroup": len(median_group),
        },
        "load": stat_block(values("loadMs")),
        "semantic": stat_block(values("semanticMs")),
        "useful": stat_block(values("usefulMs")),
        "pids": {
            "all": pids,
            "measured": measured_pids,
            "warmup": warmup_pid,
            "everyPidDistinct": len(pid_set) == len(set(pid_set)) and len(pid_set) == len(attempts),
            "everyMeasuredPidDistinctFromWarmup": all(pid != warmup_pid for pid in measured_pids),
        },
        "compareKey": median_group[0]["compareKey"] if median_group else None,
        "compareKeyId": median_key,
        "stoppedEarly": stopped_early,
        "attemptTable": [
            {
                "index": item["index"],
                "role": item["role"],
                "pid": item.get("pid"),
                "outcome": item["outcome"],
                "loadMs": item.get("loadMs"),
                "semanticMs": item.get("semanticMs"),
                "usefulMs": item.get("usefulMs"),
                "includedInMedian": item["includedInMedian"],
                "file": attempt_filename(item),
            }
            for item in attempts
        ],
        "workspace": str(workspace),
        "query": args.query,
        "expectFullName": args.expect_full_name,
        "expectFile": str(expect_file),
        "server": str(exe),
    }
    (out_dir / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    if summary["compareKey"] is not None:
        (out_dir / "compare-key.json").write_text(
            json.dumps(summary["compareKey"], ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
    log(f"SUMMARY {out_dir / 'summary.json'} partial={partial}", log_path)
    log(
        "COUNTS successes={successes} errors={errors} timeouts={timeouts}".format(**counts),
        log_path,
    )
    return 0


def attempt_filename(attempt: dict) -> str:
    if attempt["role"] == "warmup":
        return "attempt-00-warmup.json"
    return f"attempt-{attempt['index']:02d}.json"


def write_attempt(out_dir: Path, attempt: dict) -> None:
    path = out_dir / attempt_filename(attempt)
    path.write_text(json.dumps(attempt, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
