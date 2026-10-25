# Build order probe (BuildHost/task-02, step 0)

- runs: 16, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 87045 | 1.000 | 64927 | 22138 | 22010 | 318 | 250 | 0 % | 1.00 | 0.98 | 0.98 | 86872, 87045, 88474 |
| ReferencesFirst | — | no | 79121 | 0.909 | 56405 | 22717 | 22586 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 83162, 78783, 79121 |
| EvaluateAhead | 4 | no | 79829 | 0.917 | 57670 | 22235 | 43915 | 0 | 74 | 5 % | 1.87 | 1.01 | 1.04 | 79905, 79829, 79455 |
| EvaluateAheadPaused | 4 | no | 79330 | 0.911 | 57925 | 21406 | 42161 | 0 | 74 | 7 % | 1.82 | 1.01 | 1.04 | 79185, 79341, 79330 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
