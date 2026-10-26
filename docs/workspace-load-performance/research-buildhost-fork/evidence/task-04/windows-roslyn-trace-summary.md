# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 38756 ms; warmup round 38835 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.007 ms, min tail 0.019 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 10846 | 28.0 % | 10783–10987 | 10899 |
| host: evaluation (TFM switch) | 9872 | 25.5 % | 9755–9979 | 9933 |
| host: extraction / GetAllGlobs | 8389 | 21.6 % | 8247–8572 | 8519 |
| several host spans at once | 4235 | 10.9 % | 4230–4323 | 4069 |
| host: project file read | 1115 | 2.9 % | 1036–1148 | 1044 |
| client: Resolve, no host work | 938 | 2.4 % | 926–941 | 931 |
| client: between loader operations | 795 | 2.1 % | 771–804 | 814 |
| host: response send | 496 | 1.3 % | 494–525 | 489 |
| client: after the last loader operation | 435 | 1.1 % | 434–438 | 462 |
| host: project instance | 408 | 1.1 % | 397–439 | 438 |
| client: Build waiting, no host work | 377 | 1.0 % | 362–379 | 384 |
| host: response serialization | 225 | 0.6 % | 219–240 | 214 |
| host: extraction / documents and metadata | 151 | 0.4 % | 111–160 | 133 |
| host: RPC method, other | 118 | 0.3 % | 105–133 | 108 |
| client: before the first loader operation | 84 | 0.2 % | 82–90 | 84 |
| host: process startup (estimate) | 51 | 0.1 % | 50–56 | 48 |
| host: RPC queue | 40 | 0.1 % | 39–40 | 39 |
| host: RPC dispatch | 36 | 0.1 % | 36–37 | 37 |
| host: build manager start | 36 | 0.1 % | 36–36 | 44 |
| host: ProjectFileInfo extraction | 30 | 0.1 % | 23–35 | 22 |
| host: build bookkeeping | 25 | 0.1 % | 24–39 | 24 |
| host: evaluate-ahead-start | 22 | 0.1 % | 22–23 | 22 |
| client: Evaluate waiting, no host work | 22 | 0.1 % | 22–32 | 23 |
| host: extraction / additional and editorconfig documents | 12 | 0.0 % | 12–36 | 12 |
| host: extraction / other items and properties | 12 | 0.0 % | 12–20 | 12 |
| host: extraction / compiler arguments | 6 | 0.0 % | 6–6 | 6 |
| host: waiting for the client to connect | 4 | 0.0 % | 0–13 | 7 |
| host: upstream-log | 4 | 0.0 % | 4–4 | 4 |
| host: extraction / properties | 4 | 0.0 % | 4–4 | 4 |
| host: extraction / result and project references | 3 | 0.0 % | 3–3 | 3 |
| host: prepared-build | 2 | 0.0 % | 2–2 | 2 |
| host: build-ahead | 2 | 0.0 % | 2–2 | 2 |
| host: extraction / fallback arguments | 2 | 0.0 % | 2–2 | 2 |
| host: evaluation (root) | 0 | 0.0 % | 0–0 | 0 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
