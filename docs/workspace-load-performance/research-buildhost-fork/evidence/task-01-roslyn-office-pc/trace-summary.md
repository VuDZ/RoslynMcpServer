# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 107737 ms; warmup round 108557 ms
- clock check over all rounds: 2544 matched, 0 unmatched, 0 violations; min lead 0.016 ms, min tail -0.077 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 35493 | 32.9 % | 34746–35550 | 35925 |
| host: evaluation (TFM switch) | 24593 | 22.8 % | 24497–24638 | 24731 |
| host: evaluation (root) | 19343 | 18.0 % | 19279–19351 | 19308 |
| host: ProjectFileInfo extraction | 19172 | 17.8 % | 18994–19385 | 19077 |
| client: between loader operations | 2935 | 2.7 % | 2931–2963 | 2993 |
| client: Resolve, no host work | 2604 | 2.4 % | 2586–2625 | 2648 |
| client: after the last loader operation | 782 | 0.7 % | 773–784 | 820 |
| host: response send | 741 | 0.7 % | 719–757 | 733 |
| host: project instance | 607 | 0.6 % | 605–607 | 628 |
| client: Build waiting, no host work | 524 | 0.5 % | 510–529 | 544 |
| host: response serialization | 289 | 0.3 % | 289–300 | 294 |
| client: before the first loader operation | 181 | 0.2 % | 179–182 | 192 |
| host: RPC method, other | 131 | 0.1 % | 125–135 | 135 |
| host: process startup (estimate) | 114 | 0.1 % | 113–121 | 115 |
| host: project file read | 93 | 0.1 % | 93–96 | 92 |
| host: RPC dispatch | 83 | 0.1 % | 81–88 | 83 |
| host: RPC queue | 68 | 0.1 % | 67–68 | 67 |
| host: build bookkeeping | 64 | 0.1 % | 58–67 | 57 |
| host: build manager start | 60 | 0.1 % | 60–60 | 60 |
| client: Evaluate waiting, no host work | 50 | 0.0 % | 49–51 | 51 |
| host: waiting for the client to connect | 10 | 0.0 % | 9–14 | 1 |
| several host spans at once | 0 | 0.0 % | 0–6 | 4 |

Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
