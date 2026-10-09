"""Recheck query admission, fresh child receipts, TRX failures, and log retention."""

import copy
import hashlib
import json
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[5]
HERE = Path(__file__).resolve().parent
RAW = HERE.parent / "task-07"
sys.path.insert(0, str(REPO / "scripts/workspace-state-cache"))
import roundtrip_benchmark as runner

checks = {}
command = [
    sys.executable, str(REPO / "scripts/workspace-state-cache/roundtrip_benchmark.py"),
    "--workspace", str(REPO / "samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj"),
    "--query", "IRoslynMcpPlugin",
    "--server", str(REPO / "bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe"),
    "--expect-full-name", "RoslynMcpServer.Plugins.IRoslynMcpPlugin",
    "--expect-file", str(REPO / "Plugins/IRoslynMcpPlugin.cs"),
    "--repo", str(REPO),
    "--test-assembly", str(REPO / "RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.dll"),
    "--out", str(HERE / "wrong-query-output"),
]
completed = subprocess.run(command, cwd=REPO, capture_output=True, text=True, timeout=30)
(HERE / "wrong-query-stdout.txt").write_text(completed.stdout, encoding="utf-8")
(HERE / "wrong-query-stderr.txt").write_text(completed.stderr, encoding="utf-8")
assert completed.returncode == 1
assert not (HERE / "wrong-query-output").exists()
checks["wrongQueryCli"] = {"exitCode": completed.returncode, "noOutputDirectory": True}

directory = HERE / "zero-test"
directory.mkdir(exist_ok=True)
old = RAW / "restore/attempt-01/capture-stages.json"
(directory / "capture-stages.json").write_bytes(old.read_bytes())
(directory / "capture.pid").write_text("65072", encoding="utf-8")
(directory / "capture.trx").write_bytes((RAW / "restore/attempt-01/capture.trx").read_bytes())
runner.FILTER = "FullyQualifiedName=No.Such.Task07.Test"
child = runner.run_child(
    test_dll=REPO / "RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.dll",
    repo=REPO, directory=directory, role="capture",
    workspace=REPO / "samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj",
    timeout_s=60, log_path=HERE / "recheck-run.txt",
)
assert child["exitCode"] == 0 and child["trx"]["total"] == "0"
assert child["stages"] is None and child["receiptPid"] is None
assert runner.child_outcome(child) == "error"
checks["staleReceiptZeroTest"] = {"child": child, "reportedOutcome": runner.child_outcome(child)}

# Mutations of one valid receipt represent the failure cases that used to be
# hidden by stages.outcome. Each must remain an error or timeout.
valid = {
    "exitCode": 0, "timedOut": False, "runId": "current-launch", "expectedRole": "capture", "receiptPid": 12345,
    "trx": {"total": "1", "passed": "1", "failed": "0", "notExecuted": "0"},
    "stages": {"role": "capture", "pid": 12345, "runId": "current-launch", "outcome": "success"},
}
assert runner.child_outcome(valid) == "success"
negative = {}
for name in ("nonzeroExit", "timeout", "missingTrx", "failedTest", "skippedTest", "staleRunId", "wrongRole", "wrongPid"):
    case = copy.deepcopy(valid)
    if name == "nonzeroExit":
        case["exitCode"] = 1
    elif name == "timeout":
        case["timedOut"] = True
    elif name == "missingTrx":
        case["trx"] = None
    elif name == "failedTest":
        case["trx"].update(passed="0", failed="1")
    elif name == "skippedTest":
        case["trx"].update(passed="0", notExecuted="1")
    elif name == "staleRunId":
        case["stages"]["runId"] = "previous-launch"
    elif name == "wrongRole":
        case["stages"]["role"] = "restore"
    else:
        case["receiptPid"] = 54321
    outcome = runner.child_outcome(case)
    assert outcome == ("timeout" if name == "timeout" else "error")
    negative[name] = outcome
checks["childFailureAdmission"] = negative

expected_file = REPO / "samples/RoslynMcpPlugin/SamplePlugin.cs"
assert runner.declaration_matches({"fullName": runner.ACCEPTED_FULL_NAME, "file": str(expected_file)}, runner.ACCEPTED_FULL_NAME, expected_file, REPO)
assert not runner.declaration_matches({"fullName": "RoslynMcpServer.Plugins.IRoslynMcpPlugin", "file": str(expected_file)}, runner.ACCEPTED_FULL_NAME, expected_file, REPO)
assert not runner.declaration_matches({"fullName": runner.ACCEPTED_FULL_NAME, "file": str(REPO / "Plugins/IRoslynMcpPlugin.cs")}, runner.ACCEPTED_FULL_NAME, expected_file, REPO)
checks["structuredDeclarationAdmission"] = True

path = RAW / "run.txt"
ignored = subprocess.run(["git", "check-ignore", str(path)], cwd=REPO, capture_output=True, text=True)
assert path.is_file() and ignored.returncode == 1
checks["retainedOriginalLog"] = {"file": str(path), "ignored": False, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
if (RAW / "run.log").is_file():
    assert path.read_bytes() == (RAW / "run.log").read_bytes()
    checks["retainedOriginalLog"]["sameBytesAsOriginalLog"] = True

(HERE / "checks.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
print(json.dumps(checks, indent=2))
