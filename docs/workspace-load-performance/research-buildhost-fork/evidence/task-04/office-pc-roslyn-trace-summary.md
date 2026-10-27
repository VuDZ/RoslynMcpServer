# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 85259 ms; warmup round 85675 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.015 ms, min tail 0.031 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 23752 | 27.9 % | 23549–24246 | 23543 |
| host: evaluation (TFM switch) | 21881 | 25.7 % | 21532–21977 | 22033 |
| host: ProjectFileInfo extraction | 19025 | 22.3 % | 18912–19573 | 19304 |
| several host spans at once | 10229 | 12.0 % | 10059–10294 | 10289 |
| client: Resolve, no host work | 2886 | 3.4 % | 2761–2939 | 2851 |
| client: between loader operations | 2798 | 3.3 % | 2780–2842 | 2831 |
| client: after the last loader operation | 839 | 1.0 % | 800–861 | 798 |
| host: response send | 783 | 0.9 % | 768–802 | 781 |
| client: Build waiting, no host work | 693 | 0.8 % | 673–716 | 669 |
| host: project file read | 676 | 0.8 % | 15–972 | 769 |
| host: project instance | 590 | 0.7 % | 571–639 | 572 |
| host: response serialization | 263 | 0.3 % | 262–295 | 294 |
| host: RPC method, other | 259 | 0.3 % | 241–261 | 257 |
| client: before the first loader operation | 193 | 0.2 % | 185–193 | 189 |
| host: process startup (estimate) | 112 | 0.1 % | 110–112 | 114 |
| host: RPC dispatch | 83 | 0.1 % | 81–86 | 79 |
| host: RPC queue | 70 | 0.1 % | 66–76 | 67 |
| host: build bookkeeping | 63 | 0.1 % | 48–71 | 51 |
| host: build manager start | 63 | 0.1 % | 62–63 | 64 |
| host: evaluate-ahead-start | 56 | 0.1 % | 55–56 | 58 |
| client: Evaluate waiting, no host work | 52 | 0.1 % | 51–59 | 50 |
| host: waiting for the client to connect | 12 | 0.0 % | 1–24 | 1 |
| host: upstream-log | 5 | 0.0 % | 5–6 | 5 |
| host: prepared-build | 4 | 0.0 % | 4–4 | 4 |
| host: build-ahead | 3 | 0.0 % | 3–3 | 3 |
| host: evaluation (root) | 0 | 0.0 % | 0–0 | 0 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
