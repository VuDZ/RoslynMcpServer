# Traced loads, fork side (BuildHost/task-01)

- measured rounds: 3; wall median 84016 ms; warmup round 84793 ms
- clock check over all rounds: 2544 matched, 0 outside their operation, 0 operations served by no traced host; min lead 0.016 ms, min tail 0.03 ms
- 434 projects, 50201 documents, 44 workspace failures, 1 traced host(s) per load

| category | median ms | share of median wall | min–max ms | warmup ms |
|---|---|---|---|---|
| host: design-time build | 23174 | 27.6 % | 22947–24573 | 23594 |
| host: evaluation (TFM switch) | 21376 | 25.4 % | 21201–21954 | 20795 |
| host: ProjectFileInfo extraction | 18856 | 22.4 % | 18766–19573 | 19046 |
| several host spans at once | 10205 | 12.1 % | 10151–10236 | 10277 |
| client: between loader operations | 2779 | 3.3 % | 2779–2930 | 2830 |
| client: Resolve, no host work | 2769 | 3.3 % | 2760–2999 | 2903 |
| host: project file read | 987 | 1.2 % | 864–1119 | 1401 |
| client: after the last loader operation | 845 | 1.0 % | 760–865 | 761 |
| host: response send | 783 | 0.9 % | 782–791 | 755 |
| client: Build waiting, no host work | 668 | 0.8 % | 659–744 | 663 |
| host: project instance | 584 | 0.7 % | 565–609 | 534 |
| host: response serialization | 277 | 0.3 % | 276–283 | 270 |
| host: RPC method, other | 249 | 0.3 % | 245–251 | 261 |
| client: before the first loader operation | 184 | 0.2 % | 177–184 | 178 |
| host: process startup (estimate) | 111 | 0.1 % | 110–115 | 115 |
| host: RPC dispatch | 80 | 0.1 % | 79–87 | 88 |
| host: RPC queue | 66 | 0.1 % | 66–67 | 65 |
| host: build manager start | 62 | 0.1 % | 61–62 | 62 |
| host: evaluate-ahead-start | 55 | 0.1 % | 54–56 | 55 |
| client: Evaluate waiting, no host work | 53 | 0.1 % | 50–59 | 51 |
| host: build bookkeeping | 52 | 0.1 % | 47–68 | 63 |
| host: waiting for the client to connect | 12 | 0.0 % | 1–15 | 14 |
| host: upstream-log | 5 | 0.0 % | 5–5 | 5 |
| host: prepared-build | 4 | 0.0 % | 4–6 | 4 |
| host: build-ahead | 3 | 0.0 % | 3–5 | 3 |
| host: evaluate-ahead | 0 | 0.0 % | 0–0 | 0 |
| host: evaluation (root) | 0 | 0.0 % | 0–0 | 0 |

Each category's median is taken on its own, so the medians need not add up to the wall median.
Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.
