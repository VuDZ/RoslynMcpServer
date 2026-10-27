# Build order probe (BuildHost/task-02, step 0)

- runs: 28, round 0 is warmup; 142 design-time builds of 142 projects, 0 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 142 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 27722 | 1.000 | 14635 | 13063 | 12939 | 142 | 101 | 0 % | 1.00 | 0.95 | n/a | 27729, 27722, 27526 |
| ReferencesFirst | — | no | 23221 | 0.838 | 9575 | 13695 | 13571 | 142 | 0 | 0 % | 1.00 | 1.00 | n/a | 23221, 23532, 22980 |
| ReferencesFirst | — | yes | 22858 | 0.825 | 9449 | 13400 | 13276 | 142 | 0 | 0 % | 1.00 | 0.96 | n/a | 22455, 22897, 22858 |
| EvaluateAhead | 4 | no | 19108 | 0.689 | 10312 | 8878 | 17446 | 0 | 0 | 15 % | 1.72 | 1.30 | n/a | 19091, 19108, 19496 |
| EvaluateClosure | 1 | no | 20248 | 0.730 | 10342 | 9906 | 15294 | 74 | 0 | 12 % | 1.31 | 1.14 | n/a | 20248, 20453, 20032 |
| EvaluateClosure | 2 | no | 19355 | 0.698 | 10389 | 8966 | 16409 | 57 | 0 | 20 % | 1.54 | 1.22 | n/a | 19600, 19355, 19011 |
| EvaluateClosure | 2 | yes | 18185 | 0.656 | 9634 | 8551 | 15176 | 57 | 0 | 19 % | 1.52 | 1.10 | n/a | 18569, 18185, 18034 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
