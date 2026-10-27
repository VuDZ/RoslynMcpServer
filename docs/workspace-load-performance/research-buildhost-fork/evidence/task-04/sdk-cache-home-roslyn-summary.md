# SDK resolution cache (BuildHost/task-04)

- MSBuild 18.6.4.27133, 16 logical processors; 318 projects, 129 multi-targeting; measured runs 9, round 0 is warmup

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

Result: **identical**; fingerprints of 1199 steps per run.

Medians of the measured rounds, ms; in parentheses the ratio to isolated.

| mode | wall | root-evaluation | tfm-evaluation | globs | design-time-build | wall per round |
|---|---|---|---|---|---|---|
| Isolated | 35778 (×1.000) | 7376 (×1.000) | 9675 (×1.000) | 7309 (×1.000) | 10908 (×1.000) | 35778, 35510, 38759 |
| Project | 32144 (×0.898) | 8534 (×1.157) | 6276 (×0.649) | 4156 (×0.569) | 11833 (×1.085) | 29579, 32417, 32144 |
| Solution | 27643 (×0.773) | 4494 (×0.609) | 6111 (×0.632) | 4303 (×0.589) | 12047 (×1.104) | 25451, 27643, 28232 |

Steps per run: design-time-build 434, globs 318, root-evaluation 318, tfm-evaluation 374.

## SDK resolution inside each kind of step (attribution runs, slower than timed runs)

| mode | step | step ms | SDK resolution ms | share |
|---|---|---|---|---|
| Isolated | design-time-build (434) | 12078 | 354 | 2.9 % |
| Isolated | globs (318) | 7946 | 3987 | 50.2 % |
| Isolated | root-evaluation (318) | 8003 | 3941 | 49.2 % |
| Isolated | tfm-evaluation (374) | 10481 | 4680 | 44.7 % |
| Project | design-time-build (434) | 12137 | 357 | 2.9 % |
| Project | globs (318) | 4666 | 2 | 0.1 % |
| Project | root-evaluation (318) | 9204 | 4290 | 46.6 % |
| Project | tfm-evaluation (374) | 6643 | 460 | 6.9 % |
| Solution | design-time-build (434) | 13223 | 374 | 2.8 % |
| Solution | globs (318) | 4366 | 3 | 0.1 % |
| Solution | root-evaluation (318) | 4448 | 78 | 1.7 % |
| Solution | tfm-evaluation (374) | 6018 | 4 | 0.1 % |

| mode | resolutions (from the service's cache) | resolver ms |
|---|---|---|
| Isolated | 6172 (2291) | Microsoft.DotNet.MSBuildWorkloadSdkResolver 12521, NuGetSdkResolver 290, DefaultSdkResolver 101 |
| Project | 6172 (4705) | Microsoft.DotNet.MSBuildWorkloadSdkResolver 4908, NuGetSdkResolver 129, DefaultSdkResolver 39 |
| Solution | 6172 (5997) | Microsoft.DotNet.MSBuildWorkloadSdkResolver 392, NuGetSdkResolver 38, DefaultSdkResolver 7 |
