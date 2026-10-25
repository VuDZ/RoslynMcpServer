# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 39677 ms; warmup round 39671 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.007 ms, min tail 0.027 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 11032 | 27.8 % | 10880–11156 | 10998 |
| host: evaluation (TFM switch) | 10092 | 25.4 % | 10040–10143 | 10119 |
| host: ProjectFileInfo extraction | 8103 | 20.4 % | 7672–8244 | 8097 |
| host: evaluation (root) | 4501 | 11.3 % | 4484–4613 | 4533 |
| several host spans at once | 2067 | 5.2 % | 1936–2070 | 1975 |
| client: Resolve, no host work | 797 | 2.0 % | 773–815 | 799 |
| client: between loader operations | 668 | 1.7 % | 652–673 | 679 |
| host: response send | 490 | 1.2 % | 485–518 | 488 |
| client: after the last loader operation | 419 | 1.1 % | 409–442 | 430 |
| host: project instance | 419 | 1.1 % | 366–446 | 473 |
| client: Build waiting, no host work | 337 | 0.9 % | 328–359 | 351 |
| host: response serialization | 223 | 0.6 % | 221–235 | 230 |
| host: RPC method, other | 108 | 0.3 % | 103–152 | 110 |
| client: before the first loader operation | 83 | 0.2 % | 81–86 | 83 |
| host: project file read | 73 | 0.2 % | 71–86 | 69 |
| host: process startup (estimate) | 47 | 0.1 % | 46–47 | 48 |
| host: RPC queue | 43 | 0.1 % | 41–43 | 41 |
| host: RPC dispatch | 38 | 0.1 % | 37–53 | 38 |
| host: build manager start | 36 | 0.1 % | 34–37 | 35 |
| host: build bookkeeping | 28 | 0.1 % | 28–42 | 27 |
| client: Evaluate waiting, no host work | 23 | 0.1 % | 23–24 | 23 |
| host: wait-evaluation-ahead | 5 | 0.0 % | 1–5 | 4 |
| host: upstream-log | 4 | 0.0 % | 4–4 | 4 |
| host: waiting for the client to connect | 2 | 0.0 % | 0–3 | 13 |
| host: build-ahead | 2 | 0.0 % | 2–2 | 2 |
| host: prepared-build | 2 | 0.0 % | 2–2 | 2 |
| host: evaluate-ahead-start | 1 | 0.0 % | 1–1 | 1 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
