# Build order probe (BuildHost/task-02, step 0)

- runs: 16, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 16
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 34791 | 1.000 | 27755 | 6972 | 6937 | 318 | 250 | 0 % | 1.00 | 0.94 | 0.97 | 34727, 35083, 34791 |
| ReferencesFirst | — | no | 31056 | 0.893 | 23586 | 7470 | 7435 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 31575, 31056, 30159 |
| EvaluateAhead | 4 | no | 30936 | 0.889 | 24226 | 6710 | 13646 | 0 | 74 | 13 % | 1.86 | 1.12 | 1.23 | 30936, 32164, 30439 |
| EvaluateAheadPaused | 4 | no | 32115 | 0.923 | 25081 | 7002 | 13992 | 0 | 74 | 9 % | 1.79 | 1.19 | 1.16 | 32115, 30943, 32204 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
