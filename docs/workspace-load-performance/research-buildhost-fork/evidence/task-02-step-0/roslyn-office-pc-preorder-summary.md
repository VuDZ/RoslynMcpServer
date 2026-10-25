# Build order probe (BuildHost/task-02, step 0)

- runs: 32, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 81550 | 1.000 | 61852 | 19643 | 19551 | 318 | 250 | 0 % | 1.00 | 0.99 | 0.98 | 80397, 81550, 82327 |
| ReferencesFirst | — | no | 72477 | 0.889 | 52461 | 20016 | 19922 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 73018, 72269, 72477 |
| EvaluateAhead | 4 | no | 69111 | 0.847 | 53145 | 16031 | 23227 | 0 | 74 | 7 % | 1.39 | 1.03 | 1.05 | 68933, 69111, 69841 |
| EvaluateAheadPaused | 4 | no | 68413 | 0.839 | 52491 | 16039 | 23130 | 0 | 74 | 6 % | 1.39 | 1.02 | 1.07 | 68103, 68413, 69006 |
| EvaluateClosure | 2 | no | 68002 | 0.834 | 53152 | 14850 | 22450 | 205 | 74 | 22 % | 1.29 | 1.02 | 1.05 | 68002, 67454, 68384 |
| EvaluateClosure | 4 | no | 67932 | 0.833 | 53124 | 14808 | 24202 | 192 | 74 | 26 % | 1.47 | 1.04 | 1.05 | 67729, 68665, 67932 |
| EvaluateClosurePreorder | 2 | no | 67830 | 0.832 | 53275 | 14539 | 22224 | 206 | 74 | 27 % | 1.27 | 1.02 | 1.05 | 67830, 67466, 68261 |
| EvaluateClosurePreorder | 4 | no | 68323 | 0.838 | 53696 | 14652 | 23703 | 202 | 74 | 27 % | 1.44 | 1.03 | 1.06 | 68323, 68255, 68570 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
