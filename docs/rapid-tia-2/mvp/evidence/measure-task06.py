"""Measure task-06 on a disposable, prebuilt solution copy over MCP stdio."""

import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import threading
import time
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parents[4] / "scripts/workspace-state-cache"))
from ordinary_baseline import (  # noqa: E402
    _read_frames, classify_load, extract_tool, send_message, wait_response,
)


@contextmanager
def server(dll, solution, results):
    log_dir = results / "logs"
    env = dict(os.environ, ROSLYN_MCP_HOME=str(results / "home"),
               ROSLYN_MCP_LOG_DIR=str(log_dir), ROSLYN_MCP_WORKSPACE=str(solution.parent),
               ROSLYN_MCP_TOOL_PROFILE="full")
    with (results / "stderr.log").open("wb") as stderr, subprocess.Popen(
        ["dotnet", str(dll)], cwd=solution.parent, env=env, stdin=subprocess.PIPE,
        stdout=subprocess.PIPE, stderr=stderr,
    ) as proc:
        responses = queue.Queue()
        threading.Thread(target=_read_frames, args=(proc.stdout, responses, [0]), daemon=True).start()
        next_id = 0

        def request(method, params):
            nonlocal next_id
            next_id += 1
            send_message(proc, dict(jsonrpc="2.0", id=next_id, method=method, params=params))
            response = wait_response(responses, next_id, 300)
            if "error" in response:
                raise RuntimeError(response["error"])
            return response

        def call(name, **arguments):
            started = time.perf_counter()
            response = request("tools/call", dict(name=name, arguments=arguments))
            elapsed = round((time.perf_counter() - started) * 1000, 1)
            body, error = extract_tool(response)
            (results / f"{name}.txt").write_text(body, encoding="utf-8", newline="")
            if error or not body:
                raise RuntimeError(f"{name} failed. See {results}")
            return body, elapsed

        try:
            request("initialize", dict(protocolVersion="2025-11-25", capabilities={},
                                       clientInfo=dict(name="task06-measure", version="1")))
            send_message(proc, dict(jsonrpc="2.0", method="notifications/initialized"))
            yield call, log_dir
        finally:
            proc.stdin.close()
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=10)


def wait_for_baseline(log_dir):
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        logs = "".join(path.read_text(encoding="utf-8", errors="replace")
                       for path in log_dir.glob("mcp-*.log"))
        read = re.search(r"assembly_snapshot first_read outcome=success assemblies=(\d+) elapsed_ms=(\d+)", logs)
        if read and re.search(r"baseline_capture generation=\d+ outcome=success", logs):
            return dict(snapshotAssemblies=int(read[1]), snapshotReadMs=int(read[2]))
        time.sleep(.1)
    raise TimeoutError(f"No completed source and assembly baseline in {log_dir}")


def cli(solution, results, name, args):
    started = time.perf_counter()
    with (results / f"{name}.log").open("wb") as log:
        proc = subprocess.run(["dotnet", *args], cwd=solution.parent,
                              stdout=log, stderr=subprocess.STDOUT, timeout=300)
    elapsed = round((time.perf_counter() - started) * 1000, 1)
    if proc.returncode:
        raise RuntimeError(f"{name} exited {proc.returncode}. See {results}")
    return elapsed


def edit_body(original, signature):
    pattern = rb"(?m)^([ ]*)[^\r\n]*" + re.escape(signature.encode()) + rb"[^\r\n]*\r?\n\1\{(\r?\n)"
    matches = list(re.finditer(pattern, original))
    if len(matches) != 1:
        raise ValueError(f"Expected one block body for {signature}, found {len(matches)}")
    match = matches[0]
    insertion = match[1] + b"    System.GC.KeepAlive(null);" + match[2]
    return original[:match.end()] + insertion + original[match.end():]


def compare_test_runs(solution, results, project, response):
    match = re.search(r"(?im)^Run: `" + re.escape(str(project)) + r"` with filter `([^`]+)`\r?\n(?:\r?\n)*"
                      r"Stages \(classes\):", response)
    if not match:
        return dict(pairedRun=False)
    test = ["test", str(project), "-c", "Release", "--no-build", "--no-restore"]
    timings = dict(pairedRun=True)
    for mode, extra in [("filtered", ["--filter", match[1]]), ("whole", [])]:
        trx = f"test-{mode}.trx"
        timings[mode + "Ms"] = cli(solution, results, f"test-{mode}", [*test,
            "--logger", f"trx;LogFileName={trx}", "--results-directory", str(results), *extra])
        counters = ET.parse(results / trx).find(".//{*}Counters")
        executed = int(counters.attrib["executed"])
        if not executed or int(counters.attrib["failed"]):
            raise RuntimeError(f"Invalid {mode} test run. See {results / trx}")
        timings[mode + "Tests"] = executed
    return timings


def solution_file(solution, relative):
    path = (solution.parent / relative).resolve(strict=True)
    if not path.is_relative_to(solution.parent) or not path.is_file():
        raise ValueError(f"Expected a file inside the solution copy: {relative}")
    return path


def measure(dll, solution, results, case, build, version, external_save):
    source = solution_file(solution, case["file"])
    original = source.read_bytes()
    changed = edit_body(original, case["signature"])
    with server(dll, solution, results) as (call, log_dir):
        if version:
            call("get_mcp_server_info")
        loaded, load_ms = call("load_workspace", workspacePath=str(solution),
                               configuration="Release", briefOutput=True)
        if classify_load(loaded, False) != "success":
            raise RuntimeError(f"Workspace did not load cleanly. See {results}")
        row = dict(loadMs=load_ms, **wait_for_baseline(log_dir))
        depth = case.get("depth", "all")
        try:
            if external_save:
                source.write_bytes(changed)
            else:
                written, _ = call("update_file_content", filePath=str(source), content=changed.decode("utf-8-sig"))
                if "Successfully wrote" not in written or source.read_bytes() != changed:
                    raise RuntimeError(f"MCP edit did not save exactly the requested bytes. See {results}")
            row["sourceMtimeNs"] = source.stat().st_mtime_ns
            row["buildMs"] = cli(solution, results, "build-edit", build)
            response, row["answerMs"] = call("estimate_affected_tests", depth=depth, run=False)
            row["sourceMtimeAfterEstimateNs"] = source.stat().st_mtime_ns
            header = re.search(r"Changed since load: \d+ members in (\d+) files\. (\d+) changed type", response)
            if not header or header[1] != "1":
                raise RuntimeError(f"The tool did not report exactly one changed file. See {results}")
            row["changedTypes"] = int(header[2])
            stale = re.search(r"(?m)^Reasons: ([^\r\n]+?\.dll) is not newer than the last edit", response)
            if stale and Path(stale[1]).is_file():
                row["reportedStaleDllMtimeNs"] = Path(stale[1]).stat().st_mtime_ns
            if "compare" in case:
                project = solution_file(solution, case["compare"])
                row.update(compare_test_runs(solution, results, project, response))
            return row
        finally:
            source.write_bytes(original)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("server", type=Path)
    parser.add_argument("solution", type=Path)
    parser.add_argument("cases", type=Path)
    parser.add_argument("results", type=Path)
    parser.add_argument("--external-save", action="store_true", help="Write the edit without MCP.")
    args = parser.parse_args()
    dll, solution, results = (path.resolve() for path in (args.server, args.solution, args.results))
    cases = json.loads(args.cases.read_text(encoding="utf-8-sig"))
    build = ["build", str(solution), "-c", "Release", "--no-restore", "-m:1"]
    for index, case in enumerate(cases, start=1):
        case_results = results / f"{index:02d}"
        case_results.mkdir(parents=True, exist_ok=False)
        cli(solution, case_results, "build-baseline", build)
        row = measure(dll, solution, case_results, case, build, index == 1, args.external_save)
        print(json.dumps(dict(case=index, **row)), flush=True)
    cli(solution, results, "build-restored", build)


if __name__ == "__main__":
    main()
