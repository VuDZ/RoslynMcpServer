# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 36098 ms; warmup round 36453 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.008 ms, min tail 0.019 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 10903 | 30.2 % | 10796–11236 | 10678 |
| host: evaluation (TFM switch) | 9029 | 25.0 % | 8734–9218 | 9177 |
| host: ProjectFileInfo extraction | 7845 | 21.7 % | 7840–8215 | 8138 |
| several host spans at once | 3673 | 10.2 % | 3665–3903 | 3691 |
| client: Resolve, no host work | 795 | 2.2 % | 794–795 | 838 |
| client: between loader operations | 663 | 1.8 % | 656–676 | 675 |
| host: project file read | 559 | 1.5 % | 417–728 | 738 |
| host: response send | 520 | 1.4 % | 500–524 | 507 |
| client: after the last loader operation | 435 | 1.2 % | 434–469 | 438 |
| host: project instance | 426 | 1.2 % | 420–435 | 461 |
| client: Build waiting, no host work | 377 | 1.0 % | 369–378 | 386 |
| host: response serialization | 238 | 0.7 % | 231–239 | 238 |
| host: RPC method, other | 123 | 0.3 % | 114–132 | 124 |
| client: before the first loader operation | 85 | 0.2 % | 84–86 | 82 |
| host: process startup (estimate) | 47 | 0.1 % | 47–57 | 48 |
| host: RPC queue | 41 | 0.1 % | 40–42 | 40 |
| host: RPC dispatch | 39 | 0.1 % | 38–40 | 49 |
| host: build manager start | 36 | 0.1 % | 36–36 | 36 |
| host: build bookkeeping | 27 | 0.1 % | 26–34 | 48 |
| host: evaluate-ahead-start | 22 | 0.1 % | 22–23 | 22 |
| client: Evaluate waiting, no host work | 22 | 0.1 % | 22–22 | 22 |
| host: waiting for the client to connect | 15 | 0.0 % | 0–15 | 9 |
| host: upstream-log | 4 | 0.0 % | 4–5 | 4 |
| host: build-ahead | 2 | 0.0 % | 2–2 | 2 |
| host: prepared-build | 2 | 0.0 % | 2–11 | 2 |
| host: evaluation (root) | 0 | 0.0 % | 0–0 | 0 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
