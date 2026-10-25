# Build order probe (BuildHost/task-02, step 0)

- runs: 16, round 0 is warmup; 142 design-time builds of 142 projects, 0 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 142 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 27782 | 1.000 | 14786 | 13003 | 12880 | 142 | 101 | 0 % | 1.00 | 0.94 | n/a | 27597, 27903, 27782 |
| ReferencesFirst | — | no | 23406 | 0.842 | 9629 | 13814 | 13690 | 142 | 0 | 0 % | 1.00 | 1.00 | n/a | 23726, 23367, 23406 |
| EvaluateAhead | 4 | no | 19509 | 0.702 | 10624 | 8885 | 17550 | 0 | 0 | 15 % | 1.72 | 1.28 | n/a | 19818, 19509, 19346 |
| EvaluateAheadPaused | 4 | no | 19508 | 0.702 | 10505 | 9024 | 17678 | 0 | 0 | 14 % | 1.71 | 1.30 | n/a | 19773, 19499, 19508 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
