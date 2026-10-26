# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 31430 ms; warmup round 31584 ms
- clock check over all rounds: 1096 matched, 0 outside their operation, 40 operations served by no traced host; min lead 0.018 ms, min tail 0.039 ms
- 142 projects, 18877 documents, 1674 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: extraction / GetAllGlobs | 9997 | 31.8 % | 9927–10118 | 9962 |
| host: design-time build | 6721 | 21.4 % | 6650–6769 | 6737 |
| several host spans at once | 4958 | 15.8 % | 4908–4984 | 4901 |
| client: Build waiting, no host work | 3713 | 11.8 % | 3650–3719 | 3678 |
| client: Evaluate waiting, no host work | 1545 | 4.9 % | 1527–1556 | 1550 |
| client: between loader operations | 970 | 3.1 % | 945–998 | 936 |
| client: Resolve, no host work | 885 | 2.8 % | 885–894 | 904 |
| host: project file read | 578 | 1.8 % | 525–722 | 777 |
| client: after the last loader operation | 548 | 1.7 % | 547–582 | 557 |
| host: response send | 318 | 1.0 % | 310–325 | 317 |
| host: RPC method, other | 213 | 0.7 % | 212–214 | 219 |
| host: project instance | 198 | 0.6 % | 181–205 | 235 |
| client: before the first loader operation | 195 | 0.6 % | 192–202 | 208 |
| host: response serialization | 144 | 0.5 % | 142–152 | 147 |
| host: process startup (estimate) | 110 | 0.3 % | 108–119 | 110 |
| host: extraction / documents and metadata | 61 | 0.2 % | 56–77 | 64 |
| host: build manager start | 59 | 0.2 % | 58–63 | 60 |
| host: RPC queue | 52 | 0.2 % | 50–52 | 50 |
| host: RPC dispatch | 41 | 0.1 % | 40–48 | 41 |
| host: evaluate-ahead-start | 28 | 0.1 % | 28–29 | 28 |
| host: ProjectFileInfo extraction | 22 | 0.1 % | 18–26 | 29 |
| host: evaluation (root) | 14 | 0.0 % | 14–14 | 22 |
| host: build bookkeeping | 10 | 0.0 % | 9–10 | 13 |
| host: waiting for the client to connect | 8 | 0.0 % | 1–14 | 17 |
| host: extraction / additional and editorconfig documents | 4 | 0.0 % | 3–6 | 3 |
| host: extraction / compiler arguments | 3 | 0.0 % | 3–3 | 4 |
| host: prepared-build | 3 | 0.0 % | 3–3 | 3 |
| host: upstream-log | 2 | 0.0 % | 2–2 | 2 |
| host: extraction / other items and properties | 2 | 0.0 % | 2–2 | 2 |
| host: extraction / result and project references | 2 | 0.0 % | 2–4 | 2 |
| host: build-ahead | 2 | 0.0 % | 2–2 | 2 |
| host: extraction / properties | 2 | 0.0 % | 2–2 | 2 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
