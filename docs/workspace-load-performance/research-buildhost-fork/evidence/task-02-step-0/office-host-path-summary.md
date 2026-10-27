# Build order probe (BuildHost/task-02, step 0)

- runs: 24, round 0 is warmup; 142 design-time builds of 142 projects, 0 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 142 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 26045 | 1.000 | 15038 | 10965 | 10835 | 142 | 101 | 0 % | 1.00 | 0.95 | n/a | 26385, 26045, 25786 |
| ReferencesFirst | — | no | 21433 | 0.823 | 9809 | 11516 | 11391 | 142 | 0 | 0 % | 1.00 | 1.00 | n/a | 21433, 21501, 21048 |
| EvaluateAhead | 4 | no | 17706 | 0.680 | 10560 | 7275 | 14407 | 0 | 0 | 15 % | 1.74 | 1.26 | n/a | 17689, 17706, 17876 |
| EvaluateAheadPaused | 4 | no | 17797 | 0.683 | 10568 | 7228 | 14464 | 0 | 0 | 17 % | 1.74 | 1.26 | n/a | 17797, 17209, 17885 |
| EvaluateClosure | 2 | no | 17838 | 0.685 | 10540 | 7298 | 13825 | 57 | 0 | 22 % | 1.56 | 1.24 | n/a | 17938, 17303, 17838 |
| EvaluateClosure | 4 | no | 17343 | 0.666 | 10396 | 6926 | 15365 | 49 | 0 | 20 % | 1.91 | 1.32 | n/a | 17348, 17343, 17295 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
