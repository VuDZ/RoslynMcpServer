# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 43379 ms; warmup round 43261 ms
- clock check over all rounds: 1096 matched, 40 unmatched, 0 violations; min lead 0.019 ms, min tail -0.204 ms
- 142 projects, 18877 documents, 1674 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 13740 | 31.7 % | 13640–14835 | 13736 |
| host: evaluation (root) | 10381 | 23.9 % | 10267–11237 | 10259 |
| host: ProjectFileInfo extraction | 10080 | 23.2 % | 10046–10756 | 9989 |
| client: Build waiting, no host work | 3690 | 8.5 % | 3667–3721 | 3706 |
| client: Evaluate waiting, no host work | 1552 | 3.6 % | 1551–1562 | 1549 |
| client: between loader operations | 1143 | 2.6 % | 1135–1233 | 1151 |
| client: Resolve, no host work | 970 | 2.2 % | 939–1000 | 951 |
| client: after the last loader operation | 551 | 1.3 % | 550–557 | 542 |
| host: response send | 311 | 0.7 % | 302–312 | 296 |
| host: project instance | 239 | 0.6 % | 225–243 | 243 |
| client: before the first loader operation | 195 | 0.4 % | 188–200 | 194 |
| host: response serialization | 171 | 0.4 % | 165–172 | 175 |
| host: process startup (estimate) | 116 | 0.3 % | 112–119 | 116 |
| host: RPC method, other | 92 | 0.2 % | 91–95 | 91 |
| host: project file read | 86 | 0.2 % | 86–100 | 84 |
| host: build manager start | 55 | 0.1 % | 55–55 | 55 |
| host: RPC queue | 53 | 0.1 % | 52–55 | 52 |
| host: RPC dispatch | 43 | 0.1 % | 42–46 | 42 |
| host: build bookkeeping | 19 | 0.0 % | 15–24 | 20 |
| host: waiting for the client to connect | 8 | 0.0 % | 8–21 | 8 |
| several host spans at once | 0 | 0.0 % | 0–0 | 0 |

Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
