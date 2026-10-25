# Build order probe (BuildHost/task-02, step 0)

- runs: 32, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | window | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | GC pause ms | GCs gen 0/1/2 | peak working set MB | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | — | no | 79919 | 1.000 | 60730 | 19300 | 19210 | 318 | 250 | 0 % | 1.00 | 0.98 | 0.97 | 5960 | 1687/365/10 | 1479 | 80320, 79867, 79919 |
| ReferencesFirst | — | — | no | 71815 | 0.899 | 52115 | 19700 | 19607 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 5429 | 1557/339/11 | 1402 | 76729, 71815, 71516 |
| EvaluateClosurePreorder | 2 | — | no | 67976 | 0.851 | 53160 | 14816 | 22752 | 204 | 74 | 27 % | 1.27 | 1.02 | 1.06 | 5745 | 1560/344/10 | 1386 | 69400, 67743, 67976 |
| EvaluateClosurePreorder | 4 | — | no | 67459 | 0.844 | 52837 | 14622 | 23691 | 205 | 74 | 28 % | 1.44 | 1.02 | 1.07 | 5811 | 1562/344/10 | 1392 | 67418, 67459, 67848 |
| EvaluateSolutionPreorder | 2 | 4 | no | 57562 | 0.720 | 55436 | 2126 | 27383 | 14 | 74 | 82 % | 1.70 | 1.36 | 1.31 | 6287 | 1566/363/10 | 1434 | 57396, 57562, 57635 |
| EvaluateSolutionPreorder | 2 | all | no | 58016 | 0.726 | 57251 | 765 | 32545 | 4 | 74 | 94 % | 2.02 | 1.66 | 1.56 | 6271 | 1571/365/10 | 1506 | 57963, 58016, 58138 |
| EvaluateSolutionPreorder | 4 | 4 | no | 57222 | 0.716 | 55121 | 2098 | 31268 | 10 | 74 | 77 % | 2.28 | 1.48 | 1.46 | 6388 | 1572/372/10 | 1412 | 57104, 57222, 57788 |
| EvaluateSolutionPreorder | 4 | all | no | 57754 | 0.723 | 56903 | 814 | 39255 | 0 | 74 | 93 % | 3.95 | 1.97 | 1.93 | 6570 | 1576/362/10 | 1493 | 57464, 57879, 57754 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
Window is how many solution entries ahead of the build thread are queued; all means every entry at the start. GC pause and collections cover the wall time; the peak working set is the whole process's; runs of older probe versions show 0 or n/a.
