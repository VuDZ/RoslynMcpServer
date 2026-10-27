# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 35951 ms; warmup round 35837 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.008 ms, min tail 0.023 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 10670 | 29.7 % | 10612–10850 | 10592 |
| host: evaluation (TFM switch) | 8873 | 24.7 % | 8857–9055 | 8999 |
| host: ProjectFileInfo extraction | 7782 | 21.6 % | 7705–7817 | 8026 |
| several host spans at once | 3735 | 10.4 % | 3710–3740 | 3684 |
| host: project file read | 799 | 2.2 % | 770–852 | 575 |
| client: Resolve, no host work | 798 | 2.2 % | 787–823 | 833 |
| client: between loader operations | 655 | 1.8 % | 634–672 | 663 |
| host: response send | 501 | 1.4 % | 494–512 | 522 |
| client: after the last loader operation | 461 | 1.3 % | 435–465 | 430 |
| host: project instance | 417 | 1.2 % | 411–437 | 419 |
| client: Build waiting, no host work | 387 | 1.1 % | 381–387 | 379 |
| host: response serialization | 225 | 0.6 % | 216–261 | 238 |
| host: RPC method, other | 117 | 0.3 % | 109–139 | 126 |
| client: before the first loader operation | 81 | 0.2 % | 80–82 | 82 |
| host: process startup (estimate) | 48 | 0.1 % | 47–48 | 55 |
| host: RPC queue | 41 | 0.1 % | 40–42 | 41 |
| host: RPC dispatch | 38 | 0.1 % | 38–38 | 39 |
| host: build manager start | 35 | 0.1 % | 34–35 | 36 |
| host: build bookkeeping | 34 | 0.1 % | 26–38 | 26 |
| host: evaluate-ahead-start | 22 | 0.1 % | 22–22 | 27 |
| client: Evaluate waiting, no host work | 22 | 0.1 % | 22–22 | 22 |
| host: waiting for the client to connect | 8 | 0.0 % | 3–13 | 15 |
| host: upstream-log | 4 | 0.0 % | 4–4 | 4 |
| host: build-ahead | 2 | 0.0 % | 2–4 | 2 |
| host: prepared-build | 2 | 0.0 % | 2–2 | 2 |
| host: evaluation (root) | 0 | 0.0 % | 0–0 | 0 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
