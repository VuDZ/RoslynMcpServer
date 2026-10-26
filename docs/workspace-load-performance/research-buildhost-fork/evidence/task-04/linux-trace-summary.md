# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 7630 ms; warmup round 7629 ms
- clock check over all rounds: 40 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.134 ms, min tail 0.177 ms
- 5 projects, 487 documents, 0 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: extraction / GetAllGlobs | 3230 | 42.3 % | 3156–3268 | 3259 |
| host: evaluation (root) | 1976 | 25.9 % | 1932–2081 | 1934 |
| host: design-time build | 1227 | 16.1 % | 1214–1273 | 1268 |
| several host spans at once | 495 | 6.5 % | 492–519 | 523 |
| client: between loader operations | 135 | 1.8 % | 134–146 | 139 |
| client: before the first loader operation | 97 | 1.3 % | 90–103 | 88 |
| host: process startup (estimate) | 68 | 0.9 % | 63–68 | 70 |
| host: RPC method, other | 52 | 0.7 % | 50–52 | 51 |
| client: after the last loader operation | 51 | 0.7 % | 50–54 | 61 |
| client: Resolve, no host work | 40 | 0.5 % | 36–40 | 51 |
| host: build manager start | 35 | 0.5 % | 33–37 | 33 |
| host: response serialization | 26 | 0.3 % | 25–26 | 26 |
| host: RPC queue | 25 | 0.3 % | 24–26 | 24 |
| client: Build waiting, no host work | 20 | 0.3 % | 19–22 | 21 |
| host: evaluate-ahead-start | 19 | 0.3 % | 18–20 | 18 |
| host: RPC dispatch | 17 | 0.2 % | 15–17 | 16 |
| host: project instance | 10 | 0.1 % | 10–10 | 10 |
| host: response send | 10 | 0.1 % | 9–10 | 9 |
| host: ProjectFileInfo extraction | 7 | 0.1 % | 7–7 | 7 |
| host: project file read | 4 | 0.1 % | 4–4 | 4 |
| host: build bookkeeping | 4 | 0.0 % | 4–4 | 4 |
| client: Evaluate waiting, no host work | 3 | 0.0 % | 3–3 | 3 |
| host: extraction / documents and metadata | 2 | 0.0 % | 2–2 | 2 |
| host: waiting for the client to connect | 1 | 0.0 % | 1–1 | 1 |
| host: upstream-log | 1 | 0.0 % | 1–1 | 1 |
| host: extraction / result and project references | 1 | 0.0 % | 1–1 | 1 |
| host: extraction / other items and properties | 1 | 0.0 % | 1–1 | 1 |
| host: extraction / properties | 1 | 0.0 % | 1–1 | 1 |
| host: build-ahead | 1 | 0.0 % | 1–1 | 1 |
| host: extraction / additional and editorconfig documents | 1 | 0.0 % | 1–1 | 1 |
| host: prepared-build | 0 | 0.0 % | 0–0 | 0 |
| host: extraction / compiler arguments | 0 | 0.0 % | 0–0 | 0 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
