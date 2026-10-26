# SDK resolution cache (BuildHost/task-04)

- MSBuild 18.9.15.50611, 6 logical processors; 142 projects, 0 multi-targeting; measured runs 9, round 0 is warmup

| run | differing fingerprints against isolated round 1, by kind |
|---|---|
| sdk-cache-isolated-round2 | **identical** |
| sdk-cache-isolated-round3 | **identical** |
| sdk-cache-project-round1 | **identical** |
| sdk-cache-project-round2 | **identical** |
| sdk-cache-project-round3 | **identical** |
| sdk-cache-solution-round1 | **identical** |
| sdk-cache-solution-round2 | **identical** |
| sdk-cache-solution-round3 | **identical** |

Result: **identical**; fingerprints of 426 steps per run.

Medians of the measured rounds, ms; in parentheses the ratio to isolated.

| mode | wall | root-evaluation | tfm-evaluation | globs | design-time-build | wall per round |
|---|---|---|---|---|---|---|
| Isolated | 30457 (×1.000) | 10577 (×1.000) | n/a | 9988 (×1.000) | 9427 (×1.000) | 30457, 30660, 30456 |
| Project | 27440 (×0.901) | 10723 (×1.014) | n/a | 6438 (×0.645) | 9783 (×1.038) | 27313, 27440, 28353 |
| Solution | 24868 (×0.817) | 7303 (×0.690) | n/a | 6853 (×0.686) | 10165 (×1.078) | 25064, 24868, 24795 |

Steps per run: design-time-build 142, globs 142, root-evaluation 142.

## SDK resolution inside each kind of step (attribution runs, slower than timed runs)

| mode | step | step ms | SDK resolution ms | share |
|---|---|---|---|---|
| Isolated | design-time-build (142) | 9846 | 0 | 0.0 % |
| Isolated | globs (142) | 10458 | 3765 | 36.0 % |
| Isolated | root-evaluation (142) | 10955 | 3868 | 35.3 % |
| Project | design-time-build (142) | 9811 | 0 | 0.0 % |
| Project | globs (142) | 6475 | 2 | 0.0 % |
| Project | root-evaluation (142) | 11004 | 3891 | 35.4 % |
| Solution | design-time-build (142) | 10091 | 0 | 0.0 % |
| Solution | globs (142) | 6787 | 2 | 0.0 % |
| Solution | root-evaluation (142) | 7342 | 90 | 1.2 % |

| mode | resolutions (from the service's cache) | resolver ms |
|---|---|---|
| Isolated | 1104 (276) | Microsoft.DotNet.MSBuildWorkloadSdkResolver 7546, DefaultSdkResolver 54 |
| Project | 1104 (690) | Microsoft.DotNet.MSBuildWorkloadSdkResolver 3840, DefaultSdkResolver 27 |
| Solution | 1104 (1101) | Microsoft.DotNet.MSBuildWorkloadSdkResolver 75, DefaultSdkResolver 1 |
