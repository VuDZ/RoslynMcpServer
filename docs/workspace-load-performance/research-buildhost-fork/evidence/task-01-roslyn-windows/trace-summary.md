# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 42394 ms; warmup round 41320 ms
- clock check over all rounds: 2544 matched, 0 unmatched, 0 violations; min lead 0.013 ms, min tail -0.002 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 18374 | 43.3 % | 18226–18544 | 18018 |
| host: evaluation (TFM switch) | 8302 | 19.6 % | 8106–8512 | 7918 |
| host: evaluation (root) | 6037 | 14.2 % | 5879–6283 | 5788 |
| host: ProjectFileInfo extraction | 5777 | 13.6 % | 5615–6446 | 5633 |
| client: Resolve, no host work | 849 | 2.0 % | 794–853 | 800 |
| client: between loader operations | 775 | 1.8 % | 771–787 | 783 |
| client: after the last loader operation | 539 | 1.3 % | 475–546 | 550 |
| host: response send | 529 | 1.2 % | 518–559 | 516 |
| host: project instance | 413 | 1.0 % | 407–466 | 345 |
| client: Build waiting, no host work | 252 | 0.6 % | 233–351 | 267 |
| host: response serialization | 227 | 0.5 % | 208–238 | 218 |
| client: before the first loader operation | 91 | 0.2 % | 89–97 | 90 |
| host: RPC method, other | 88 | 0.2 % | 87–100 | 87 |
| host: process startup (estimate) | 51 | 0.1 % | 51–52 | 51 |
| host: project file read | 49 | 0.1 % | 49–49 | 49 |
| host: RPC queue | 44 | 0.1 % | 42–45 | 46 |
| host: RPC dispatch | 42 | 0.1 % | 41–43 | 42 |
| client: Evaluate waiting, no host work | 38 | 0.1 % | 26–49 | 37 |
| host: build manager start | 35 | 0.1 % | 35–35 | 34 |
| host: build bookkeeping | 33 | 0.1 % | 33–34 | 33 |
| host: waiting for the client to connect | 4 | 0.0 % | 0–11 | 15 |
| several host spans at once | 0 | 0.0 % | 0–1 | 0 |

Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
