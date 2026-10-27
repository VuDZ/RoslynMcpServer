# Build order probe (BuildHost/task-02, step 0)

- runs: 28, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 16
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | root evaluations ms (summed) | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 32064 | 1.000 | 25717 | 6314 | 250 | 0 % | 1.00 | 0.89 | 0.91 | 31359, 32064, 35463 |
| ReferencesFirst | — | no | 29738 | 0.927 | 22177 | 7526 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 29738, 29464, 31582 |
| ReferencesFirst | — | yes | 27152 | 0.847 | 20575 | 6764 | 74 | 0 % | 1.00 | 0.96 | 0.97 | 27028, 27152, 31171 |
| EvaluateAhead | 2 | no | 29875 | 0.932 | 23549 | 9990 | 74 | 13 % | 1.35 | 1.02 | 1.06 | 29100, 29875, 31073 |
| EvaluateAhead | 4 | no | 28928 | 0.902 | 22451 | 12985 | 74 | 11 % | 1.87 | 0.99 | 1.01 | 28928, 29037, 28566 |
| EvaluateAhead | 15 | no | 29836 | 0.931 | 23262 | 27527 | 74 | 4 % | 3.78 | 1.02 | 1.05 | 29836, 29644, 31009 |
| EvaluateAhead | 15 | yes | 28233 | 0.881 | 22095 | 24992 | 74 | 17 % | 3.69 | 0.99 | 1.08 | 28233, 27290, 29433 |

Builds ms and root evaluations ms add up each build and evaluation; with evaluate-ahead they overlap, and only wall is elapsed time.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
