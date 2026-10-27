# Build order probe (BuildHost/task-02, step 0)

- runs: 32, round 0 is warmup; 142 design-time builds of 142 projects, 0 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 142 project and target framework results as the first client run

| order | workers | window | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | GC pause ms | GCs gen 0/1/2 | peak working set MB | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | — | no | 26010 | 1.000 | 15089 | 10921 | 10806 | 142 | 101 | 0 % | 1.00 | 0.94 | n/a | 1490 | 382/98/9 | 459 | 26158, 26010, 25356 |
| ReferencesFirst | — | — | no | 21356 | 0.821 | 9868 | 11488 | 11374 | 142 | 0 | 0 % | 1.00 | 1.00 | n/a | 1211 | 320/84/8 | 442 | 21356, 21486, 21229 |
| EvaluateClosurePreorder | 2 | — | no | 17540 | 0.674 | 10517 | 7178 | 13436 | 45 | 0 | 31 % | 1.43 | 1.20 | n/a | 1351 | 324/88/8 | 429 | 17540, 21033, 17536 |
| EvaluateClosurePreorder | 4 | — | no | 17124 | 0.658 | 10409 | 6721 | 14960 | 44 | 0 | 28 % | 1.83 | 1.32 | n/a | 1382 | 323/92/8 | 433 | 17124, 17057, 17140 |
| EvaluateSolutionPreorder | 2 | 4 | no | 14926 | 0.574 | 11173 | 3723 | 15076 | 10 | 0 | 49 % | 1.92 | 1.29 | n/a | 1437 | 323/92/7 | 437 | 14960, 14633, 14926 |
| EvaluateSolutionPreorder | 2 | all | no | 12688 | 0.488 | 11832 | 885 | 15881 | 1 | 0 | 87 % | 2.04 | 1.38 | n/a | 1520 | 324/90/8 | 457 | 12428, 13001, 12688 |
| EvaluateSolutionPreorder | 4 | 4 | no | 14122 | 0.543 | 10660 | 3433 | 17878 | 10 | 0 | 39 % | 2.90 | 1.46 | n/a | 1491 | 323/92/7 | 460 | 14124, 14122, 13978 |
| EvaluateSolutionPreorder | 4 | all | no | 12693 | 0.488 | 11963 | 709 | 20022 | 0 | 0 | 87 % | 3.94 | 1.67 | n/a | 1667 | 327/93/7 | 445 | 12693, 12951, 12568 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
Window is how many solution entries ahead of the build thread are queued; all means every entry at the start. GC pause and collections cover the wall time; the peak working set is the whole process's; runs of older probe versions show 0 or n/a.
