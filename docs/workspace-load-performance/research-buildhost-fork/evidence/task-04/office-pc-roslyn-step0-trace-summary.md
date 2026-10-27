# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 84408 ms; warmup round 83585 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.016 ms, min tail 0.031 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 23343 | 27.7 % | 22957–23645 | 23165 |
| host: evaluation (TFM switch) | 21602 | 25.6 % | 21597–21928 | 20905 |
| host: extraction / GetAllGlobs | 18601 | 22.0 % | 18452–18782 | 18405 |
| several host spans at once | 10151 | 12.0 % | 10098–10173 | 10084 |
| client: Resolve, no host work | 2833 | 3.4 % | 2821–2978 | 2868 |
| client: between loader operations | 2788 | 3.3 % | 2772–2820 | 2760 |
| host: response send | 791 | 0.9 % | 758–796 | 770 |
| client: after the last loader operation | 767 | 0.9 % | 757–773 | 773 |
| host: project file read | 665 | 0.8 % | 608–700 | 1188 |
| client: Build waiting, no host work | 651 | 0.8 % | 636–661 | 661 |
| host: project instance | 599 | 0.7 % | 598–606 | 551 |
| host: response serialization | 269 | 0.3 % | 257–272 | 261 |
| host: RPC method, other | 233 | 0.3 % | 223–233 | 238 |
| host: extraction / documents and metadata | 182 | 0.2 % | 154–185 | 152 |
| client: before the first loader operation | 177 | 0.2 % | 176–186 | 188 |
| host: process startup (estimate) | 111 | 0.1 % | 108–115 | 108 |
| host: RPC dispatch | 85 | 0.1 % | 84–97 | 89 |
| host: RPC queue | 74 | 0.1 % | 70–76 | 67 |
| host: build manager start | 62 | 0.1 % | 62–63 | 61 |
| host: ProjectFileInfo extraction | 57 | 0.1 % | 50–81 | 46 |
| host: evaluate-ahead-start | 56 | 0.1 % | 55–56 | 55 |
| client: Evaluate waiting, no host work | 52 | 0.1 % | 51–57 | 52 |
| host: build bookkeeping | 51 | 0.1 % | 49–52 | 49 |
| host: extraction / other items and properties | 23 | 0.0 % | 19–37 | 19 |
| host: extraction / additional and editorconfig documents | 18 | 0.0 % | 18–27 | 25 |
| host: waiting for the client to connect | 10 | 0.0 % | 5–12 | 12 |
| host: extraction / compiler arguments | 10 | 0.0 % | 9–11 | 9 |
| host: prepared-build | 6 | 0.0 % | 4–7 | 4 |
| host: extraction / properties | 6 | 0.0 % | 5–6 | 6 |
| host: upstream-log | 5 | 0.0 % | 5–5 | 5 |
| host: extraction / result and project references | 5 | 0.0 % | 5–7 | 5 |
| host: build-ahead | 5 | 0.0 % | 3–5 | 3 |
| host: extraction / fallback arguments | 3 | 0.0 % | 3–3 | 3 |
| host: evaluation (root) | 0 | 0.0 % | 0–0 | 0 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
