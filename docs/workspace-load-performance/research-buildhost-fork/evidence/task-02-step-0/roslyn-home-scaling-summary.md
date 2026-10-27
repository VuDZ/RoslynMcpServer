# Evaluation scaling (BuildHost/task-02)

- measured runs: 21, round 0 is warmup; 318 projects evaluated, 129 of them multi-targeting, 0 not evaluated; logical processors: 16

| evaluation | workers | wall ms (median) | speedup against 1 worker | evaluations at once (mean) | time-weighted slowdown, one TFM | time-weighted slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|
| host, shared collection | 1 | 7734 | 1.00 | 1.00 | 1.00 | 1.00 | 7986, 7734, 7710 |
| host, shared collection | 2 | 4321 | 1.79 | 2.00 | 1.14 | 1.06 | 4270, 4321, 4350 |
| host, collection per worker | 2 | 4415 | 1.75 | 2.00 | 1.13 | 1.15 | 4436, 4415, 4390 |
| LoadProject, shared collection | 2 | 9156 | 0.84 | 2.00 | 2.23 | 2.98 | 9135, 9156, 9485 |
| host, shared collection | 4 | 2660 | 2.91 | 3.98 | 1.33 | 1.44 | 2660, 2665, 2616 |
| host, collection per worker | 4 | 2793 | 2.77 | 3.98 | 1.36 | 1.62 | 2793, 2892, 2748 |
| LoadProject, shared collection | 4 | 9279 | 0.83 | 3.99 | 4.19 | 6.40 | 9279, 9078, 9366 |

Evaluations at once is the summed duration of a run's evaluations over its wall time.
