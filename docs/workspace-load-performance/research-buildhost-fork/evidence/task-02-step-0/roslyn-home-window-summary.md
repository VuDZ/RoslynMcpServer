# Build order probe (BuildHost/task-02, step 0)

- runs: 24, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 16
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | window | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | GC pause ms | GCs gen 0/1/2 | peak working set MB | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | — | no | 29707 | 1.000 | 24034 | 5721 | 5708 | 318 | 250 | 0 % | 1.00 | 0.86 | 0.86 | 2779 | 214/183/7 | 1500 | 29707, 30048, 29672 |
| ReferencesFirst | — | — | no | 27288 | 0.919 | 20469 | 6819 | 6805 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 2584 | 198/168/7 | 1421 | 29776, 27031, 27288 |
| EvaluateClosurePreorder | 4 | — | no | 27099 | 0.912 | 22444 | 4792 | 7540 | 217 | 74 | 26 % | 1.41 | 1.10 | 1.03 | 2680 | 198/177/7 | 1424 | 28045, 27099, 26460 |
| EvaluateSolutionPreorder | 4 | 4 | no | 24331 | 0.819 | 23812 | 535 | 9872 | 10 | 74 | 81 % | 2.09 | 1.25 | 1.20 | 2782 | 199/185/7 | 1423 | 35977, 23632, 24331 |
| EvaluateSolutionPreorder | 4 | 16 | no | 24495 | 0.825 | 24227 | 269 | 10166 | 1 | 74 | 87 % | 2.26 | 1.32 | 1.23 | 2788 | 200/184/7 | 1441 | 25794, 24495, 23870 |
| EvaluateSolutionPreorder | 4 | all | no | 24577 | 0.827 | 24308 | 268 | 11259 | 1 | 74 | 89 % | 4.00 | 1.75 | 1.25 | 2816 | 200/184/7 | 1444 | 27412, 24577, 24109 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
Window is how many solution entries ahead of the build thread are queued; all means every entry at the start. GC pause and collections cover the wall time; the peak working set is the whole process's; runs of older probe versions show 0 or n/a.
