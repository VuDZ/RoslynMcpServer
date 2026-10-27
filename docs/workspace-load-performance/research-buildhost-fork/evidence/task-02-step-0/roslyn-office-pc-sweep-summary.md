# Build order probe (BuildHost/task-02, step 0)

- runs: 28, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | root evaluations ms (summed) | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 87209 | 1.000 | 65066 | 22016 | 250 | 0 % | 1.00 | 0.99 | 0.98 | 88292, 86414, 87209 |
| ReferencesFirst | — | no | 79070 | 0.907 | 56049 | 22703 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 79232, 78675, 79070 |
| ReferencesFirst | — | yes | 74457 | 0.854 | 52861 | 21754 | 74 | 0 % | 1.00 | 0.95 | 0.95 | 75011, 74007, 74457 |
| EvaluateAhead | 2 | no | 79676 | 0.914 | 58825 | 31631 | 74 | 13 % | 1.35 | 1.02 | 1.03 | 79676, 80192, 79650 |
| EvaluateAhead | 4 | no | 79496 | 0.912 | 58384 | 43380 | 74 | 11 % | 1.85 | 1.01 | 1.03 | 79496, 79263, 79849 |
| EvaluateAhead | 5 | no | 79409 | 0.911 | 58441 | 47975 | 74 | 11 % | 2.05 | 1.01 | 1.05 | 79409, 79229, 79703 |
| EvaluateAhead | 5 | yes | 75009 | 0.860 | 54576 | 46421 | 74 | 13 % | 2.06 | 0.95 | 0.98 | 75421, 74746, 75009 |

Builds ms and root evaluations ms add up each build and evaluation; with evaluate-ahead they overlap, and only wall is elapsed time.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
