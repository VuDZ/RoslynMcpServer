# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 27012 ms; warmup round 27888 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.009 ms, min tail 0.019 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 9751 | 36.1 % | 9647–10733 | 10300 |
| host: evaluation (TFM switch) | 4363 | 16.2 % | 4358–4450 | 4531 |
| several host spans at once | 3859 | 14.3 % | 3799–3887 | 3852 |
| host: extraction / GetAllGlobs evaluation, shared SDK cache | 3580 | 13.3 % | 3395–4113 | 3921 |
| host: project file read | 1242 | 4.6 % | 1048–1485 | 1169 |
| client: Resolve, no host work | 826 | 3.1 % | 741–837 | 760 |
| client: between loader operations | 630 | 2.3 % | 628–661 | 652 |
| host: response send | 486 | 1.8 % | 485–521 | 493 |
| client: after the last loader operation | 424 | 1.6 % | 400–430 | 423 |
| host: project instance | 404 | 1.5 % | 377–433 | 433 |
| client: Build waiting, no host work | 317 | 1.2 % | 314–381 | 338 |
| host: response serialization | 237 | 0.9 % | 224–249 | 214 |
| host: extraction / GetAllGlobs | 148 | 0.5 % | 124–199 | 160 |
| host: extraction / documents and metadata | 136 | 0.5 % | 109–167 | 94 |
| host: RPC method, other | 134 | 0.5 % | 121–143 | 138 |
| client: before the first loader operation | 82 | 0.3 % | 81–91 | 82 |
| host: process startup (estimate) | 49 | 0.2 % | 49–50 | 48 |
| host: RPC queue | 41 | 0.2 % | 41–41 | 44 |
| host: build manager start | 37 | 0.1 % | 36–38 | 36 |
| host: RPC dispatch | 36 | 0.1 % | 35–37 | 38 |
| host: ProjectFileInfo extraction | 32 | 0.1 % | 17–44 | 30 |
| host: build bookkeeping | 24 | 0.1 % | 24–26 | 40 |
| host: evaluate-ahead-start | 22 | 0.1 % | 22–24 | 23 |
| client: Evaluate waiting, no host work | 20 | 0.1 % | 20–21 | 20 |
| host: extraction / additional and editorconfig documents | 11 | 0.0 % | 11–13 | 12 |
| host: extraction / other items and properties | 11 | 0.0 % | 11–12 | 11 |
| host: waiting for the client to connect | 10 | 0.0 % | 5–13 | 6 |
| host: extraction / compiler arguments | 6 | 0.0 % | 5–6 | 6 |
| host: upstream-log | 4 | 0.0 % | 4–4 | 4 |
| host: extraction / properties | 3 | 0.0 % | 3–3 | 3 |
| host: extraction / result and project references | 2 | 0.0 % | 2–3 | 3 |
| host: prepared-build | 2 | 0.0 % | 2–2 | 2 |
| host: build-ahead | 2 | 0.0 % | 2–2 | 2 |
| host: extraction / fallback arguments | 2 | 0.0 % | 2–2 | 2 |
| host: evaluation (root) | 0 | 0.0 % | 0–0 | 0 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
