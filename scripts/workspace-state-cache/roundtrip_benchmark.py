#!/usr/bin/env python3
"""Paired ordinary baseline and isolated cross-process restore.

Ordinary attempts reuse ordinary_baseline. Restore attempts start a new
testhost per capture and per read/decode/hydrate/query. Publish, NuGet
restore, and the OS file cache stay outside the stage timers. The two
scenarios share one compare key. This file does not enable a production cache.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

import ordinary_baseline as baseline

PROTOCOL_ID = "e0-paired-ordinary-restore-1"
FILTER = (
    "FullyQualifiedName=RoslynMcpServer.Tests.WorkspaceStateCache."
    "IsolatedRestoreBenchmarkTests.Attempt_requires_a_runner_directory"
)
CHILD_TIMEOUT_S = 720
LARGE_TIMEOUT_S = 900
DEFAULT_LARGE = Path(r"E:\Devel\roslyn-mcp-bench\clones\btcpay-web\BTCPayServer\BTCPayServer.csproj")
ACCEPTED_QUERY = "SamplePlugin"
ACCEPTED_FULL_NAME = "RoslynMcpPlugin.SamplePlugin"
ACCEPTED_FILE = Path("samples") / "RoslynMcpPlugin" / "SamplePlugin.cs"
ADMITTED_OUTCOMES = {"success", "miss", "unsupported", "unexpected-success", "error"}


def log(message: str, log_path: Path) -> None:
    baseline.log(message, log_path)


def clone(value: object) -> object:
    return json.loads(json.dumps(value))


def paired_material(host_key: dict, record: dict, health: dict, facts: dict, evaluated: dict) -> dict:
    material = baseline.compare_material(host_key, record, health, facts, evaluated)
    material["semanticMode"] = "paired-forced-ordinary-and-isolated-restore"
    material["scenario"] = "e0-paired-ordinary-and-isolated-restore"
    material["overlayMode"] = "off"
    material["cacheHit"] = False
    material["productionReady"] = False
    material["epochAccepted"] = False
    return material


def stamp(record: dict, material: dict | None, identity: str) -> None:
    record["protocol"] = PROTOCOL_ID
    shares = material is not None and record.get("contentIdentity") == identity and not record.get("healthDiverged")
    if not shares:
        record["compareKey"] = None
        record["compareKeyId"] = None
        record["sharesCompareKey"] = False
        return
    stamped = clone(material)
    record["compareKey"] = stamped
    record["compareKeyId"] = baseline.sha256_text(baseline.canonical_json(stamped))
    record["sharesCompareKey"] = True


def health_matches(health: dict, material: dict) -> bool:
    observed = material["serverHealthObservation"]

    def value(name: str) -> str:
        raw = health.get(name)
        return "not-observed" if raw is None else raw

    return (
        value("effectiveConfiguration") == observed["msbuildConfiguration"]
        and value("effectivePlatform") == observed["msbuildPlatform"]
        and value("effectiveTargetFramework") == observed["msbuildTargetFramework"]
    )


def accepted_control(query: str, full_name: str, expect_file: Path, repo: Path) -> bool:
    if query != ACCEPTED_QUERY or full_name != ACCEPTED_FULL_NAME:
        return False
    return baseline.path_matches(str(expect_file), ACCEPTED_FILE, repo)


def declaration_matches(declaration: object, full_name: str, expect_file: Path, repo: Path) -> bool:
    if not isinstance(declaration, dict):
        return False
    actual_name = declaration.get("fullName")
    actual_file = declaration.get("file")
    if actual_name != full_name or not isinstance(actual_file, str):
        return False
    return baseline.path_matches(actual_file, expect_file, repo)


def clear_role_receipts(directory: Path, role: str) -> None:
    # A reused --out keeps the previous stages file. A zero-test child would not replace it,
    # so the next launch must not be able to read that receipt.
    for suffix in ("-stages.json", ".pid", ".trx"):
        path = directory / f"{role}{suffix}"
        if path.is_file():
            path.unlink()


def read_trx(path: Path) -> dict | None:
    if not path.is_file():
        return None
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return None
    counters = next((element for element in root.iter() if element.tag.endswith("Counters")), None)
    if counters is None:
        return None
    return {key: counters.attrib.get(key) for key in ("total", "passed", "failed", "notExecuted")}


def trx_accepted(trx: dict | None) -> bool:
    return bool(
        trx
        and trx.get("total") == "1"
        and trx.get("passed") == "1"
        and trx.get("failed") == "0"
        and trx.get("notExecuted") == "0"
    )


def read_receipt_pid(path: Path) -> int | None:
    if not path.is_file():
        return None
    text = path.read_text(encoding="utf-8").strip()
    if not text.isdigit():
        return None
    return int(text)


def run_child(
    *,
    test_dll: Path,
    repo: Path,
    directory: Path,
    role: str,
    workspace: Path | None,
    timeout_s: float,
    log_path: Path,
    expect_full_name: str | None = None,
    expect_file: Path | None = None,
) -> dict:
    directory.mkdir(parents=True, exist_ok=True)
    clear_role_receipts(directory, role)
    run_id = uuid.uuid4().hex
    env = os.environ.copy()
    env["ROSLYN_TASK07_DIRECTORY"] = str(directory)
    env["ROSLYN_TASK07_ROLE"] = role
    env["ROSLYN_TASK07_RUN_ID"] = run_id
    env.pop("ROSLYN_TASK07_WORKSPACE", None)
    env.pop("ROSLYN_TASK07_EXPECT_FULL_NAME", None)
    env.pop("ROSLYN_TASK07_EXPECT_FILE", None)
    if workspace is not None:
        env["ROSLYN_TASK07_WORKSPACE"] = str(workspace)
    if expect_full_name:
        env["ROSLYN_TASK07_EXPECT_FULL_NAME"] = expect_full_name
    if expect_file is not None:
        env["ROSLYN_TASK07_EXPECT_FILE"] = str(expect_file)
    env["DOTNET_CLI_UI_LANGUAGE"] = "en"
    env["DOTNET_NOLOGO"] = "1"
    command = [
        "dotnet",
        "vstest",
        str(test_dll),
        "--TestCaseFilter:" + FILTER,
        "--logger:trx;LogFileName=" + role + ".trx",
        "--ResultsDirectory:" + str(directory),
    ]
    log(f"child role={role} runId={run_id} dir={directory.name}", log_path)
    proc = subprocess.Popen(
        command,
        cwd=str(repo),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    timed_out = False
    try:
        stdout, stderr = proc.communicate(timeout=timeout_s)
    except subprocess.TimeoutExpired:
        timed_out = True
        baseline.kill_tree(proc.pid)
        try:
            stdout, stderr = proc.communicate(timeout=15)
        except subprocess.TimeoutExpired:
            proc.kill()
            stdout, stderr = proc.communicate(timeout=15)
    (directory / f"{role}-stdout.txt").write_text(stdout or "", encoding="utf-8")
    (directory / f"{role}-stderr.txt").write_text(stderr or "", encoding="utf-8")
    stages_path = directory / f"{role}-stages.json"
    stages = None
    if stages_path.is_file():
        loaded = json.loads(stages_path.read_text(encoding="utf-8"))
        if isinstance(loaded, dict) and loaded.get("runId") == run_id and loaded.get("role") == role:
            stages = loaded
    receipt_pid = read_receipt_pid(directory / f"{role}.pid")
    if stages is not None and stages.get("pid") != receipt_pid:
        stages = None
    trx = read_trx(directory / f"{role}.trx")
    return {
        "dotnetPid": proc.pid,
        "exitCode": proc.returncode,
        "timedOut": timed_out,
        "runId": run_id,
        "expectedRole": role,
        "receiptPid": receipt_pid,
        "trx": trx,
        "stages": stages,
        "stagesFile": stages_path.name if stages is not None else None,
    }


def child_outcome(child: dict) -> str:
    # stages.outcome alone is not this launch. A non-zero exit, a timeout, or a trx that
    # did not execute this one test must not inherit a leftover or mismatched receipt.
    if child.get("timedOut"):
        return "timeout"
    if child.get("exitCode") != 0:
        return "error"
    if not trx_accepted(child.get("trx")):
        return "error"
    stages = child.get("stages") or {}
    if stages.get("runId") != child.get("runId"):
        return "error"
    if stages.get("role") != child.get("expectedRole"):
        return "error"
    pid = stages.get("pid")
    if not isinstance(pid, int) or pid != child.get("receiptPid"):
        return "error"
    outcome = stages.get("outcome")
    if outcome not in ADMITTED_OUTCOMES:
        return "error"
    return outcome


def number(stages: dict | None, name: str) -> float | None:
    if not stages:
        return None
    value = stages.get(name)
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return float(value)
    return None


def byte_block(values: list[float]) -> dict:
    block = baseline.stat_block(values)
    if block["median"] is None:
        return block
    block["medianText"] = f"{int(block['median'])} bytes"
    block["minText"] = f"{int(block['min'])} bytes"
    block["maxText"] = f"{int(block['max'])} bytes"
    return block


def stage_map(measured: dict[str, str]) -> dict[str, str]:
    stages = {name: "not-measured" for name in baseline.NOT_MEASURED_STAGES}
    stages.update(measured)
    return stages


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Time ordinary load and isolated cross-process restore on one compare key."
    )
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--query", required=True)
    parser.add_argument("--server", type=Path, required=True)
    parser.add_argument("--expect-full-name", required=True)
    parser.add_argument("--expect-file", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--test-assembly", type=Path, required=True)
    parser.add_argument("--large-workspace", type=Path, default=DEFAULT_LARGE)
    parser.add_argument("--measured", type=int, default=baseline.DEFAULT_MEASURED)
    parser.add_argument("--max-measured", type=int, default=baseline.DEFAULT_MAX_MEASURED)
    parser.add_argument("--publish-record", type=Path, default=None)
    return parser.parse_args()


def main() -> int:
    try:
        sys.stdout.reconfigure(line_buffering=True)
    except (AttributeError, OSError):
        pass
    if sys.version_info < (3, 11) or sys.platform != "win32":
        print("This runner needs Python 3.11+ on Windows.", file=sys.stderr)
        return 1
    args = parse_args()
    if args.measured < baseline.MIN_MEASURED or args.max_measured < args.measured:
        print("--measured must be >= 5 and --max-measured must be >= --measured.", file=sys.stderr)
        return 1
    workspace = args.workspace.resolve()
    exe = args.server.resolve()
    test_dll = args.test_assembly.resolve()
    repo = args.repo.resolve()
    if not workspace.is_file() or not exe.is_file() or not test_dll.is_file():
        print("Workspace, server exe, or test assembly was not found.", file=sys.stderr)
        return 1
    if not accepted_control(args.query, args.expect_full_name, args.expect_file, repo):
        print(
            "Refusing a query outside the accepted SamplePlugin control before any measurement. "
            "Expected query SamplePlugin, full name RoslynMcpPlugin.SamplePlugin, "
            "file samples/RoslynMcpPlugin/SamplePlugin.cs.",
            file=sys.stderr,
        )
        return 1
    out_dir = args.out.resolve()
    ordinary_dir = out_dir / "ordinary"
    restore_dir = out_dir / "restore"
    ordinary_dir.mkdir(parents=True, exist_ok=True)
    restore_dir.mkdir(parents=True, exist_ok=True)
    log_path = out_dir / "run.txt"
    log(f"{PROTOCOL_ID} workspace={workspace}", log_path)
    log("file cache is not dropped; this is a new-process/warm-files run", log_path)
    versions = baseline.read_csproj_versions(repo)
    host_key = baseline.build_host_key(repo, exe, versions)
    publish = baseline.load_publish_record(args.publish_record.resolve() if args.publish_record else None)
    baseline_files, scope = baseline.content_manifest(workspace, repo, out_dir)
    identity = baseline.manifest_identity(baseline_files)
    (out_dir / "content-identity.json").write_text(
        json.dumps({"scope": scope, "identity": identity, "fileCount": len(baseline_files), "files": baseline_files}, indent=2),
        encoding="utf-8",
    )
    omitted = {
        "configuration": baseline.ABSENT,
        "platform": baseline.ABSENT,
        "targetFramework": baseline.ABSENT,
        "buildArgs": baseline.ABSENT,
        "shadowCopyInSolutionAnalyzers": baseline.ABSENT,
    }
    facts = baseline.loader_facts(repo, exe)
    evaluated = baseline.evaluated_instance(workspace, repo, omitted)
    (out_dir / "evaluated-instance.json").write_text(json.dumps(evaluated, indent=2), encoding="utf-8")
    env = baseline.child_env(repo)
    key_record = {
        "query": args.query,
        "expectFullName": args.expect_full_name,
        "expectFile": str(args.expect_file),
        "contentIdentity": identity,
        "contentIdentityScope": scope,
        "loadArgumentsOmitted": list(omitted),
        "loadArgumentsSent": {"workspacePath": str(workspace), "briefOutput": True},
    }

    ordinary_attempts: list[dict] = []
    warmup = baseline.run_attempt(
        exe=exe, repo=repo, workspace=workspace, query=args.query,
        expect_full_name=args.expect_full_name, expect_file=args.expect_file,
        env=env, role="warmup", index=0, response_dir=ordinary_dir, log_path=log_path,
        baseline_files=baseline_files, host_key=host_key, loader_facts_value=facts, evaluated=evaluated,
    )
    ordinary_attempts.append(warmup)
    counts = {"successes": 0, "errors": 0, "timeouts": 0, "errorBreakdown": {}}
    consecutive = 0
    stopped = None
    measured_index = 0
    success_keys: dict[str, int] = {}
    while measured_index < args.max_measured:
        best = max(success_keys.values()) if success_keys else 0
        clean = counts["errors"] == 0 and counts["timeouts"] == 0
        if best >= args.measured and (clean or measured_index >= args.measured):
            break
        measured_index += 1
        attempt = baseline.run_attempt(
            exe=exe, repo=repo, workspace=workspace, query=args.query,
            expect_full_name=args.expect_full_name, expect_file=args.expect_file,
            env=env, role="measured", index=measured_index, response_dir=ordinary_dir, log_path=log_path,
            baseline_files=baseline_files, host_key=host_key, loader_facts_value=facts, evaluated=evaluated,
        )
        ordinary_attempts.append(attempt)
        baseline.tally_outcome(attempt["outcome"], counts)
        if attempt["outcome"] == "success":
            success_keys[attempt.get("contentIdentity") or ""] = success_keys.get(attempt.get("contentIdentity") or "", 0) + 1
            consecutive = 0
        else:
            consecutive += 1
            if consecutive >= baseline.CONSECUTIVE_FAILURES:
                stopped = f"ordinary stopped after {baseline.CONSECUTIVE_FAILURES} consecutive measured failures"
                log(stopped, log_path)
                break

    health_source = next((item.get("health") or {} for item in ordinary_attempts if item["outcome"] == "success"), {})
    material = paired_material(host_key, key_record, health_source, facts, evaluated)
    for item in ordinary_attempts:
        if item["outcome"] == "success" and not health_matches(item.get("health") or {}, material):
            item["healthDiverged"] = True
        stamp(item, material, identity)
        item["includedInMedian"] = False
        baseline.write_attempt(ordinary_dir, item)

    restore_attempts: list[dict] = []
    restore_counts = {"successes": 0, "errors": 0, "timeouts": 0, "errorBreakdown": {}}
    consecutive = 0
    restore_stopped = None
    restore_index = 0
    roles = ["warmup", *["measured"] * args.max_measured]
    for role in roles:
        if role == "measured":
            best = sum(1 for item in restore_attempts if item["role"] == "measured" and item["outcome"] == "success" and item.get("sharesCompareKey"))
            clean = restore_counts["errors"] == 0 and restore_counts["timeouts"] == 0
            if best >= args.measured and (clean or restore_index >= args.measured):
                break
            if restore_stopped:
                break
            restore_index += 1
            index = restore_index
        else:
            index = 0
        handoff = restore_dir / (f"attempt-{index:02d}-warmup" if role == "warmup" else f"attempt-{index:02d}")
        files, _ = baseline.content_manifest(workspace, repo, out_dir)
        diff = baseline.diff_manifests(baseline_files, files)
        attempt_identity = baseline.manifest_identity(files)
        capture = run_child(
            test_dll=test_dll, repo=repo, directory=handoff, role="capture",
            workspace=workspace, timeout_s=CHILD_TIMEOUT_S, log_path=log_path,
        )
        capture_outcome = child_outcome(capture)
        restore = None
        if capture_outcome == "success":
            restore = run_child(
                test_dll=test_dll, repo=repo, directory=handoff, role="restore",
                workspace=None, timeout_s=CHILD_TIMEOUT_S, log_path=log_path,
                expect_full_name=args.expect_full_name, expect_file=args.expect_file,
            )
        capture_stages = capture.get("stages") or {}
        restore_stages = (restore or {}).get("stages") or {}
        restore_outcome = child_outcome(restore) if restore else "skipped"
        declaration = restore_stages.get("declaration")
        if restore_outcome == "success" and not declaration_matches(
            declaration, args.expect_full_name, args.expect_file, repo
        ):
            restore_outcome = "miss"
        paired_outcome = restore_outcome if capture_outcome == "success" else capture_outcome
        error = (restore_stages or capture_stages).get("error")
        if restore_outcome == "miss" and not error:
            error = "restore declaration does not match the expected query"
        record = {
            "protocol": PROTOCOL_ID,
            "role": role,
            "index": index,
            "includedInMedian": False,
            "scenario": "isolated-cross-process-restore",
            "cacheHit": False,
            "ramHit": False,
            "fileCacheDropped": False,
            "newProcess": True,
            "outcome": paired_outcome,
            "captureOutcome": capture_outcome,
            "restoreOutcome": restore_outcome,
            "capturePid": capture_stages.get("pid"),
            "restorePid": restore_stages.get("pid"),
            "captureDotnetPid": capture["dotnetPid"],
            "restoreDotnetPid": None if restore is None else restore["dotnetPid"],
            "captureExitCode": capture["exitCode"],
            "captureTimedOut": capture["timedOut"],
            "captureTrx": capture.get("trx"),
            "captureRunId": capture.get("runId"),
            "restoreExitCode": None if restore is None else restore["exitCode"],
            "restoreTimedOut": None if restore is None else restore["timedOut"],
            "restoreTrx": None if restore is None else restore.get("trx"),
            "restoreRunId": None if restore is None else restore.get("runId"),
            "runnerPid": os.getpid(),
            "contentIdentity": attempt_identity,
            "inputDifferences": diff,
            "ordinaryLoadMs": number(capture_stages, "ordinaryLoadMs"),
            "captureMs": number(capture_stages, "captureMs"),
            "writeMs": number(capture_stages, "writeMs"),
            "captureOverheadMs": number(capture_stages, "captureOverheadMs"),
            "readMs": number(restore_stages, "readMs"),
            "decodeMs": number(restore_stages, "decodeMs"),
            "hydrateMs": number(restore_stages, "hydrateMs"),
            "queryMs": number(restore_stages, "queryMs"),
            "usefulMs": number(restore_stages, "usefulMs"),
            "diskEnvelopeBytes": number(restore_stages, "diskEnvelopeBytes") or number(capture_stages, "diskEnvelopeBytes"),
            "capturePeakWorkingSetBytes": number(capture_stages, "peakWorkingSetBytes"),
            "restorePeakWorkingSetBytes": number(restore_stages, "peakWorkingSetBytes"),
            "declaration": declaration,
            "query": args.query,
            "expectFullName": args.expect_full_name,
            "expectFile": str(args.expect_file),
            "hydrateIncludesInternalCodecRoundTrip": restore_stages.get("hydrateIncludesInternalCodecRoundTrip"),
            "error": error,
            "handoff": handoff.name,
            "stages": stage_map({
                "capture": "measured" if number(capture_stages, "captureMs") is not None else "not-measured",
                "read": "measured" if number(restore_stages, "readMs") is not None else "not-measured",
                "decode": "measured" if number(restore_stages, "decodeMs") is not None else "not-measured",
                "hydrate": "measured" if number(restore_stages, "hydrateMs") is not None else "not-measured",
                "write": "measured" if number(capture_stages, "writeMs") is not None else "not-measured",
                "diskEnvelope": "measured" if number(capture_stages, "diskEnvelopeBytes") is not None else "not-measured",
                "peakWorkingSet": "measured",
            }),
            "timing": {
                "capture": "MSBuild load is ordinaryLoadMs; CaptureAsync is captureMs; encode+write is writeMs",
                "restore": "new pid: read, decode, hydrate, query; useful is their sum and does not include capture",
                "hydrate": "includes the hydrator's own encode/decode round-trip",
                "query": "accepted SamplePlugin control only; declaration is compared before a success is admitted",
                "publishIncluded": False,
                "restoreOfCorpusIncluded": False,
                "fileCacheDropped": False,
            },
        }
        stamp(record, material, identity)
        restore_attempts.append(record)
        name = "attempt-00-warmup.json" if role == "warmup" else f"attempt-{index:02d}.json"
        (restore_dir / name).write_text(json.dumps(record, indent=2), encoding="utf-8")
        if role == "measured":
            baseline.tally_outcome(record["outcome"], restore_counts)
            if record["outcome"] == "success":
                consecutive = 0
            else:
                consecutive += 1
                if consecutive >= baseline.CONSECUTIVE_FAILURES:
                    restore_stopped = f"restore stopped after {baseline.CONSECUTIVE_FAILURES} consecutive measured failures"
                    log(restore_stopped, log_path)

    large = probe_large(args.large_workspace, test_dll, repo, out_dir / "large", log_path)
    (out_dir / "large-target.json").write_text(json.dumps(large, indent=2), encoding="utf-8")
    summary = build_summary(
        ordinary_attempts, restore_attempts, counts, restore_counts, material, identity, scope,
        baseline_files, host_key, publish, workspace, exe, test_dll, args, stopped, restore_stopped, large,
    )
    (out_dir / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    if summary["compareKey"] is not None:
        (out_dir / "compare-key.json").write_text(json.dumps(summary["compareKey"], indent=2), encoding="utf-8")
    log(f"SUMMARY partial={summary['partial']}", log_path)
    return 0


def probe_large(path: Path, test_dll: Path, repo: Path, directory: Path, log_path: Path) -> dict:
    if not path.is_file():
        result = {
            "workspace": str(path),
            "outcome": "not-present",
            "includedInMedian": False,
            "note": "The large candidate is not on this machine. It is not treated as supported and not folded into the control median.",
        }
        log("large target not present", log_path)
        return result
    child = run_child(
        test_dll=test_dll, repo=repo, directory=directory, role="large",
        workspace=path, timeout_s=LARGE_TIMEOUT_S, log_path=log_path,
    )
    stages = child.get("stages") or {}
    return {
        "workspace": str(path),
        "outcome": child_outcome(child),
        "includedInMedian": False,
        "pid": stages.get("pid"),
        "dotnetPid": child["dotnetPid"],
        "runId": child.get("runId"),
        "timedOut": child["timedOut"],
        "exitCode": child["exitCode"],
        "trx": child.get("trx"),
        "unsupportedReason": stages.get("unsupportedReason"),
        "captureMs": number(stages, "captureMs"),
        "ordinaryLoadMs": number(stages, "ordinaryLoadMs"),
        "error": stages.get("error"),
        "note": "Separate from the RoslynMcpPlugin median. An unexpected success is not a supported control and is not a cache hit.",
    }


def select_median(attempts: list[dict], predicate) -> list[dict]:
    return [item for item in attempts if item["role"] == "measured" and predicate(item)]


def build_summary(
    ordinary, restore, ordinary_counts, restore_counts, material, identity, scope,
    files, host_key, publish, workspace, exe, test_dll, args, stopped, restore_stopped, large,
) -> dict:
    key_id = baseline.sha256_text(baseline.canonical_json(material))
    ordinary_group = select_median(
        ordinary,
        lambda item: item["outcome"] == "success" and item.get("compareKeyId") == key_id,
    )
    capture_group = select_median(
        restore,
        lambda item: item["captureOutcome"] == "success" and item.get("compareKeyId") == key_id,
    )
    restore_group = select_median(
        restore,
        lambda item: item["outcome"] == "success" and item.get("compareKeyId") == key_id,
    )
    for item in ordinary:
        item["includedInMedian"] = item in ordinary_group
    for item in restore:
        item["includedInCaptureMedian"] = item in capture_group
        item["includedInRestoreMedian"] = item in restore_group
        item["includedInMedian"] = item in restore_group
    for item in ordinary:
        if item in ordinary_group:
            baseline.write_attempt(args.out.resolve() / "ordinary", item)
    for item in restore:
        name = "attempt-00-warmup.json" if item["role"] == "warmup" else f"attempt-{item['index']:02d}.json"
        (args.out.resolve() / "restore" / name).write_text(json.dumps(item, indent=2), encoding="utf-8")

    reasons: list[str] = []
    if ordinary_counts["errors"] or ordinary_counts["timeouts"]:
        reasons.append("an ordinary measured attempt failed, timed out, or missed the expected declaration")
    if restore_counts["errors"] or restore_counts["timeouts"]:
        reasons.append("an isolated-restore measured attempt failed, timed out, or missed the expected declaration")
    if len(ordinary_group) < baseline.MIN_MEASURED or len(restore_group) < baseline.MIN_MEASURED:
        reasons.append(
            f"fewer than {baseline.MIN_MEASURED} successes share the compare key "
            f"(ordinary {len(ordinary_group)}, restore {len(restore_group)})"
        )
    if len(capture_group) < baseline.MIN_MEASURED:
        reasons.append(f"fewer than {baseline.MIN_MEASURED} capture successes share the compare key")
    if stopped:
        reasons.append(stopped)
    if restore_stopped:
        reasons.append(restore_stopped)
    pids = [item.get("pid") for item in ordinary if isinstance(item.get("pid"), int)]
    for item in restore:
        for name in ("capturePid", "restorePid"):
            if isinstance(item.get(name), int):
                pids.append(item[name])
    if len(pids) != len(set(pids)):
        reasons.append("measured process ids are not all distinct")
    if any(item.get("capturePid") == item.get("restorePid") for item in restore if item.get("restorePid")):
        reasons.append("a restore pid matched its capture pid")

    def values(group: list[dict], field: str) -> list[float]:
        return [item[field] for item in group if isinstance(item.get(field), (int, float))]

    return {
        "protocol": PROTOCOL_ID,
        "when": datetime.now(timezone.utc).isoformat(),
        "partial": bool(reasons),
        "partialReasons": reasons,
        "productionReady": False,
        "epochAccepted": False,
        "p95": "not-computed; not promised at this sample size",
        "fileCacheDropped": False,
        "cacheHit": False,
        "ramHit": False,
        "compareKey": material,
        "compareKeyId": key_id,
        "publish": publish,
        "publishInsideAttemptTimers": False,
        "corpusRestore": {"performed": False, "includedInAttemptTimers": False},
        "warmupExcludedFromMedian": True,
        "host": host_key,
        "contentIdentityAtStart": identity,
        "contentIdentityScope": scope,
        "inputFileCount": len(files),
        "workspace": str(workspace),
        "server": str(exe),
        "testAssembly": str(test_dll),
        "query": args.query,
        "expectFullName": args.expect_full_name,
        "expectFile": str(args.expect_file),
        "ordinary": {
            "scenario": "forced-ordinary-baseline",
            "counts": {
                "measuredAttempts": sum(1 for item in ordinary if item["role"] == "measured"),
                "successes": ordinary_counts["successes"],
                "errors": ordinary_counts["errors"],
                "timeouts": ordinary_counts["timeouts"],
                "errorBreakdown": ordinary_counts["errorBreakdown"],
                "medianGroup": len(ordinary_group),
            },
            "load": baseline.stat_block(values(ordinary_group, "loadMs")),
            "semantic": baseline.stat_block(values(ordinary_group, "semanticMs")),
            "useful": baseline.stat_block(values(ordinary_group, "usefulMs")),
            "peakWorkingSet": byte_block(values(ordinary_group, "peakWorkingSetBytes")),
        },
        "capture": {
            "scenario": "capture-overhead",
            "medianGroup": len(capture_group),
            "ordinaryLoad": baseline.stat_block(values(capture_group, "ordinaryLoadMs")),
            "capture": baseline.stat_block(values(capture_group, "captureMs")),
            "write": baseline.stat_block(values(capture_group, "writeMs")),
            "overhead": baseline.stat_block(values(capture_group, "captureOverheadMs")),
            "peakWorkingSet": byte_block(values(capture_group, "capturePeakWorkingSetBytes")),
            "diskEnvelope": byte_block(values(capture_group, "diskEnvelopeBytes")),
        },
        "restore": {
            "scenario": "isolated-cross-process-restore",
            "counts": {
                "measuredAttempts": sum(1 for item in restore if item["role"] == "measured"),
                "successes": restore_counts["successes"],
                "errors": restore_counts["errors"],
                "timeouts": restore_counts["timeouts"],
                "errorBreakdown": restore_counts["errorBreakdown"],
                "medianGroup": len(restore_group),
            },
            "read": baseline.stat_block(values(restore_group, "readMs")),
            "decode": baseline.stat_block(values(restore_group, "decodeMs")),
            "hydrate": baseline.stat_block(values(restore_group, "hydrateMs")),
            "query": baseline.stat_block(values(restore_group, "queryMs")),
            "useful": baseline.stat_block(values(restore_group, "usefulMs")),
            "peakWorkingSet": byte_block(values(restore_group, "restorePeakWorkingSetBytes")),
            "usefulExcludesCapture": True,
        },
        "largeTarget": large,
        "limits": {
            "productionReady": False,
            "epochAccepted": False,
            "h001": "External XML documentation is excluded. Full XML-doc equivalence is not claimed.",
            "h002": "Strong-name scope is variant B. Exact hidden strong-name state is not claimed.",
            "cache": "Isolated restore is not a production cache hit and not a RAM hit.",
            "p95": "not promised; n is below 20",
            "notMeasured": [
                "inputEnumeration", "stat", "textHashing", "binarySample", "binaryRead",
                "prepare", "publication", "visitedEntries", "bytesHashed", "uniqueBinaries",
                "dirtyOrReloadedProjects", "openOrDtbObservations",
            ],
        },
        "attemptTables": {
            "ordinary": [
                {
                    "index": item["index"], "role": item["role"], "pid": item.get("pid"),
                    "outcome": item["outcome"], "loadMs": item.get("loadMs"),
                    "semanticMs": item.get("semanticMs"), "usefulMs": item.get("usefulMs"),
                    "includedInMedian": item["includedInMedian"], "file": baseline.attempt_filename(item),
                }
                for item in ordinary
            ],
            "restore": [
                {
                    "index": item["index"], "role": item["role"],
                    "capturePid": item.get("capturePid"), "restorePid": item.get("restorePid"),
                    "outcome": item["outcome"], "captureMs": item.get("captureMs"),
                    "readMs": item.get("readMs"), "decodeMs": item.get("decodeMs"),
                    "hydrateMs": item.get("hydrateMs"), "queryMs": item.get("queryMs"),
                    "usefulMs": item.get("usefulMs"), "includedInMedian": item["includedInMedian"],
                    "file": "attempt-00-warmup.json" if item["role"] == "warmup" else f"attempt-{item['index']:02d}.json",
                }
                for item in restore
            ],
        },
    }


if __name__ == "__main__":
    raise SystemExit(main())
