# Build order probe (BuildHost/task-02, step 0)

- runs: 20, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 16
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 29619 | 1.000 | 23904 | 5710 | 5677 | 318 | 250 | 0 % | 1.00 | 0.84 | 0.87 | 29058, 29657, 29619 |
| ReferencesFirst | — | no | 28046 | 0.947 | 20966 | 6756 | 6722 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 27095, 28046, 29684 |
| EvaluateAhead | 4 | no | 27906 | 0.942 | 22276 | 5630 | 7754 | 0 | 74 | 7 % | 1.32 | 1.07 | 1.12 | 27906, 26400, 29159 |
| EvaluateAheadPaused | 4 | no | 26612 | 0.898 | 21211 | 5401 | 7353 | 0 | 74 | 6 % | 1.31 | 1.03 | 1.05 | 26151, 26612, 26706 |
| EvaluateClosure | 12 | no | 26158 | 0.883 | 21328 | 4867 | 9636 | 193 | 74 | 28 % | 1.90 | 1.00 | 0.99 | 26387, 26137, 26158 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
