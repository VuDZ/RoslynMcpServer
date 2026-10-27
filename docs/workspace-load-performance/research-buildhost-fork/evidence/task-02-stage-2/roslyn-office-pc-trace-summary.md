# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 85642 ms; warmup round 85893 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.016 ms, min tail 0.033 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 23598 | 27.6 % | 23295–24125 | 23639 |
| host: evaluation (TFM switch) | 21757 | 25.4 % | 21306–22481 | 21933 |
| host: ProjectFileInfo extraction | 19278 | 22.5 % | 18941–19392 | 19259 |
| several host spans at once | 10291 | 12.0 % | 10233–10483 | 10296 |
| client: between loader operations | 2968 | 3.5 % | 2944–3059 | 2963 |
| client: Resolve, no host work | 2916 | 3.4 % | 2897–2956 | 2918 |
| host: project file read | 939 | 1.1 % | 633–1055 | 762 |
| client: after the last loader operation | 832 | 1.0 % | 772–857 | 869 |
| host: response send | 806 | 0.9 % | 777–810 | 807 |
| client: Build waiting, no host work | 662 | 0.8 % | 641–670 | 661 |
| host: project instance | 580 | 0.7 % | 564–594 | 558 |
| host: response serialization | 278 | 0.3 % | 268–281 | 271 |
| host: RPC method, other | 251 | 0.3 % | 245–258 | 247 |
| client: before the first loader operation | 188 | 0.2 % | 181–189 | 185 |
| host: process startup (estimate) | 117 | 0.1 % | 115–118 | 126 |
| host: RPC dispatch | 85 | 0.1 % | 84–86 | 83 |
| host: build bookkeeping | 70 | 0.1 % | 49–70 | 54 |
| host: RPC queue | 69 | 0.1 % | 64–70 | 69 |
| host: build manager start | 64 | 0.1 % | 64–65 | 63 |
| host: evaluate-ahead-start | 56 | 0.1 % | 56–58 | 57 |
| client: Evaluate waiting, no host work | 54 | 0.1 % | 51–56 | 53 |
| host: waiting for the client to connect | 7 | 0.0 % | 6–11 | 8 |
| host: upstream-log | 5 | 0.0 % | 5–5 | 5 |
| host: prepared-build | 4 | 0.0 % | 4–4 | 4 |
| host: build-ahead | 3 | 0.0 % | 3–5 | 3 |
| host: evaluation (root) | 0 | 0.0 % | 0–0 | 0 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
