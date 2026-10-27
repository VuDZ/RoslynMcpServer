# Build order probe (BuildHost/task-02, step 0)

- runs: 28, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 16
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 34553 | 1.000 | 27254 | 7056 | 7022 | 318 | 250 | 0 % | 1.00 | 0.96 | 0.97 | 33776, 34553, 34773 |
| ReferencesFirst | — | no | 30471 | 0.882 | 23121 | 7400 | 7365 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 29590, 31593, 30471 |
| ReferencesFirst | — | yes | 28160 | 0.815 | 21548 | 6612 | 6576 | 318 | 74 | 0 % | 1.00 | 0.96 | 0.97 | 26836, 28160, 28459 |
| EvaluateAhead | 4 | no | 30719 | 0.889 | 23729 | 6990 | 14288 | 0 | 74 | 10 % | 1.82 | 1.09 | 1.13 | 30059, 32048, 30719 |
| EvaluateClosure | 1 | no | 31209 | 0.903 | 25521 | 5687 | 8679 | 209 | 74 | 22 % | 1.15 | 1.03 | 0.99 | 29205, 31651, 31209 |
| EvaluateClosure | 2 | no | 32277 | 0.934 | 26477 | 5800 | 10655 | 202 | 74 | 29 % | 1.45 | 1.05 | 1.20 | 30378, 32687, 32277 |
| EvaluateClosure | 2 | yes | 30508 | 0.883 | 25193 | 5314 | 10065 | 203 | 74 | 29 % | 1.46 | 1.02 | 1.17 | 28760, 30508, 31564 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
