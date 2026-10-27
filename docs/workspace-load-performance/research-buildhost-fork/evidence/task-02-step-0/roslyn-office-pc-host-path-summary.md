# Build order probe (BuildHost/task-02, step 0)

- runs: 24, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 80956 | 1.000 | 61328 | 19526 | 19402 | 318 | 250 | 0 % | 1.00 | 0.99 | 0.98 | 80956, 80832, 81101 |
| ReferencesFirst | — | no | 72270 | 0.893 | 52321 | 19861 | 19736 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 72315, 72270, 72171 |
| EvaluateAhead | 4 | no | 69149 | 0.854 | 52984 | 16165 | 23448 | 0 | 74 | 7 % | 1.38 | 1.04 | 1.05 | 68974, 69526, 69149 |
| EvaluateAheadPaused | 4 | no | 69279 | 0.856 | 52889 | 16128 | 23357 | 0 | 74 | 7 % | 1.39 | 1.04 | 1.07 | 68832, 69666, 69279 |
| EvaluateClosure | 2 | no | 68401 | 0.845 | 53270 | 15040 | 22701 | 204 | 74 | 23 % | 1.29 | 1.04 | 1.05 | 68668, 68401, 68305 |
| EvaluateClosure | 4 | no | 68855 | 0.851 | 53968 | 15031 | 24533 | 192 | 74 | 26 % | 1.47 | 1.04 | 1.10 | 69503, 68458, 68855 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
