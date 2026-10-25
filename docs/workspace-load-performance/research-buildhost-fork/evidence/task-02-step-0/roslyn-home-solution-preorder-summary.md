# Build order probe (BuildHost/task-02, step 0)

- runs: 24, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 16
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 32315 | 1.000 | 26130 | 6042 | 6027 | 318 | 250 | 0 % | 1.00 | 0.85 | 0.87 | 32458, 32315, 32038 |
| ReferencesFirst | — | no | 29285 | 0.906 | 22490 | 7050 | 7035 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 29285, 29219, 30417 |
| EvaluateClosurePreorder | 2 | no | 27842 | 0.862 | 22716 | 5147 | 7328 | 219 | 74 | 22 % | 1.24 | 1.01 | 0.94 | 29062, 27842, 27500 |
| EvaluateClosurePreorder | 4 | no | 28447 | 0.880 | 23404 | 5043 | 7863 | 217 | 74 | 25 % | 1.38 | 1.07 | 0.97 | 29133, 28262, 28447 |
| EvaluateSolutionPreorder | 2 | no | 27648 | 0.856 | 27369 | 277 | 10203 | 1 | 74 | 93 % | 2.01 | 1.57 | 1.23 | 27144, 27648, 28770 |
| EvaluateSolutionPreorder | 4 | no | 26357 | 0.816 | 26083 | 269 | 11256 | 1 | 74 | 89 % | 4.00 | 1.62 | 1.25 | 26357, 26997, 26268 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
