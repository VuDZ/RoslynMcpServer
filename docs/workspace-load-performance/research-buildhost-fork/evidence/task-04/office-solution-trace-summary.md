# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 31807 ms; warmup round 31161 ms
- clock check over all rounds: 1096 matched, 0 outside their operation, 40 operations served by no traced host; min lead 0.021 ms, min tail 0.046 ms
- 142 projects, 18877 documents, 1674 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: ProjectFileInfo extraction | 10235 | 32.2 % | 10183–10349 | 10093 |
| host: design-time build | 6865 | 21.6 % | 6845–6868 | 6704 |
| several host spans at once | 5024 | 15.8 % | 4880–5405 | 4840 |
| client: Build waiting, no host work | 3723 | 11.7 % | 3647–3826 | 3701 |
| client: Evaluate waiting, no host work | 1547 | 4.9 % | 1531–1599 | 1552 |
| client: between loader operations | 967 | 3.0 % | 966–979 | 994 |
| client: Resolve, no host work | 928 | 2.9 % | 905–932 | 919 |
| host: project file read | 601 | 1.9 % | 450–708 | 462 |
| client: after the last loader operation | 547 | 1.7 % | 545–570 | 548 |
| host: response send | 308 | 1.0 % | 303–325 | 312 |
| host: RPC method, other | 214 | 0.7 % | 211–225 | 210 |
| client: before the first loader operation | 200 | 0.6 % | 193–203 | 203 |
| host: project instance | 196 | 0.6 % | 174–215 | 134 |
| host: response serialization | 144 | 0.5 % | 141–145 | 139 |
| host: process startup (estimate) | 110 | 0.3 % | 109–121 | 112 |
| host: build manager start | 58 | 0.2 % | 58–62 | 61 |
| host: RPC queue | 52 | 0.2 % | 52–53 | 52 |
| host: RPC dispatch | 45 | 0.1 % | 44–49 | 42 |
| host: evaluate-ahead-start | 28 | 0.1 % | 28–29 | 30 |
| host: evaluation (root) | 25 | 0.1 % | 24–25 | 28 |
| host: build bookkeeping | 10 | 0.0 % | 9–11 | 10 |
| host: waiting for the client to connect | 10 | 0.0 % | 7–14 | 11 |
| host: prepared-build | 3 | 0.0 % | 3–3 | 3 |
| host: upstream-log | 2 | 0.0 % | 2–5 | 2 |
| host: build-ahead | 2 | 0.0 % | 2–2 | 2 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
