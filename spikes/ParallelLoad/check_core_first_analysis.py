"""Deterministic analysis regression checks without measured-process dependencies."""
import copy

from summarize_core_first import measurement_issues, summarize_combo


def main():
    baseline = {"Projects": {}, "Diagnostics": [], "BaselineMedianMs": 1000}
    run = {"Status": "ok", "Projects": {}, "Diagnostics": [], "WallMs": 700, "UniqueProjects": 1,
           "Process": {"Errors": [], "MaxSampleGapMs": 1000, "PeakBuildHostWorkingSet": 1024}}
    result = summarize_combo(baseline, [run, run, run], {})
    assert result["completeValidRuns"] and result["wallRatio"] == .7, "A one-second gap must remain admissible."
    delayed = copy.deepcopy(run)
    delayed["Process"]["MaxSampleGapMs"] = 1000.01
    result = summarize_combo(baseline, [run, delayed, run], {})
    assert not result["completeValidRuns"] and result["wallRatio"] is None and result["speedup"] is None, \
        "One delayed sample must invalidate the whole combination's measured speedup."
    assert any("once-per-second" in reason for reason in result["invalidMeasurementReasons"][1]), \
        "The invalid measurement must retain its sampling reason."
    for gap in (float("inf"), float("nan")):
        delayed["Process"]["MaxSampleGapMs"] = gap
        assert measurement_issues(delayed), "Nonfinite sample gaps must not pass measurement acceptance."
    failed = copy.deepcopy(run)
    failed["Status"] = "timeout"
    result = summarize_combo(baseline, [run, failed, run], {})
    assert result["statusCounts"]["timeout"] == 1 and not result["completeValidRuns"], \
        "A timeout must retain a failed row and invalidate measured speedup."
    print("Core-first analysis checks passed: 1000ms boundary, delayed/nonfinite telemetry, timeout, and invalid speedup.")


if __name__ == "__main__":
    main()
