"""Reproduce task-07 receipt admission defects without changing the runner."""

import copy
import json
import statistics
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parents[5]
sys.path.insert(0, str(REPO / "scripts/workspace-state-cache"))
import roundtrip_benchmark as runner

HERE = Path(__file__).resolve().parent
RAW = HERE.parent / "task-07"
summary = json.loads((RAW / "summary.json").read_text(encoding="utf-8"))
checks = {}
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
receipts = []
for leg in ("ordinary", "restore"):
    attempts = [json.loads(p.read_text(encoding="utf-8")) for p in sorted((RAW / leg).glob("attempt-*.json"))]
    measured = [a for a in attempts if a["includedInMedian"]]
    assert len(measured) == 5
    assert all(a["compareKeyId"] == summary["compareKeyId"] for a in measured)
    assert all(runner.baseline.sha256_text(runner.baseline.canonical_json(a["compareKey"])) == a["compareKeyId"] for a in attempts)
    fields = {"loadMs": "load", "semanticMs": "semantic", "usefulMs": "useful"} if leg == "ordinary" else {
        "readMs": "read", "decodeMs": "decode", "hydrateMs": "hydrate", "queryMs": "query", "usefulMs": "useful",
    }
    for field, metric in fields.items():
        values = [a[field] for a in measured]
        block = summary[leg][metric]
        assert (statistics.median(values), min(values), max(values)) == (block["median"], block["min"], block["max"])
    if leg == "ordinary":
        receipts.extend(a["pid"] for a in attempts)
    else:
        for a in attempts:
            receipts.extend([a["capturePid"], a["restorePid"]])
            for role in ("capture", "restore"):
                handoff = RAW / leg / a["handoff"]
                counters = ET.parse(handoff / (role + ".trx")).find("t:ResultSummary/t:Counters", ns).attrib
                assert all(counters[k] == v for k, v in {"total": "1", "passed": "1", "failed": "0", "notExecuted": "0"}.items())
                stages = json.loads((handoff / (role + "-stages.json")).read_text(encoding="utf-8"))
                assert stages["pid"] == a[role + "Pid"]
assert len(receipts) == len(set(receipts)) == 18
checks["originalEvidence"] = {"mediansRecomputed": True, "distinctPids": 18, "childTrxAllOnePassed": True}

# The public --out directory may be reused. Seed a receipt from the earlier run,
# then launch a real VSTest process whose filter matches zero tests.
directory = HERE / "zero-test"
directory.mkdir(exist_ok=True)
old = RAW / "restore/attempt-01/capture-stages.json"
(directory / "capture-stages.json").write_bytes(old.read_bytes())
runner.FILTER = "FullyQualifiedName=No.Such.Task07.Test"
child = runner.run_child(
    test_dll=REPO / "RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.dll",
    repo=REPO, directory=directory, role="capture",
    workspace=REPO / "samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj",
    timeout_s=60, log_path=HERE / "reproduction-run.txt",
)
counters = ET.parse(directory / "capture.trx").find("t:ResultSummary/t:Counters", ns).attrib
checks["staleReceiptZeroTest"] = {
    "exitCode": child["exitCode"], "trxCounters": counters,
    "reportedOutcome": runner.child_outcome(child),
    "stalePid": child["stages"]["pid"],
    "stagesUnchanged": (directory / "capture-stages.json").read_bytes() == old.read_bytes(),
}
checks["nonzeroExitWithSuccessReceipt"] = runner.child_outcome({
    "timedOut": False, "exitCode": 1, "stages": {"outcome": "success"},
})

# No effective loader context from capture is consulted by stamp.
material = copy.deepcopy(summary["compareKey"])
material["loaderGlobalsBeforeOpen"]["merged"]["configuration"] = "Release"
record = {"contentIdentity": summary["contentIdentityAtStart"], "outcome": "success"}
runner.stamp(record, material, summary["contentIdentityAtStart"])
checks["unobservedLoaderContext"] = {"sharesCompareKey": record["sharesCompareKey"], "claimedConfiguration": record["compareKey"]["loaderGlobalsBeforeOpen"]["merged"]["configuration"]}

(HERE / "checks.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
print(json.dumps(checks, indent=2))
