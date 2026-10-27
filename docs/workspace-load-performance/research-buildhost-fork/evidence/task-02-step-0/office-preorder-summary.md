# Build order probe (BuildHost/task-02, step 0)

- runs: 32, round 0 is warmup; 142 design-time builds of 142 projects, 0 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 142 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 26237 | 1.000 | 15269 | 10968 | 10855 | 142 | 101 | 0 % | 1.00 | 0.96 | n/a | 26596, 26237, 25531 |
| ReferencesFirst | — | no | 21313 | 0.812 | 9885 | 11404 | 11291 | 142 | 0 | 0 % | 1.00 | 1.00 | n/a | 20978, 21429, 21313 |
| EvaluateAhead | 4 | no | 17710 | 0.675 | 10416 | 7294 | 14527 | 0 | 0 | 16 % | 1.74 | 1.27 | n/a | 17710, 18193, 17416 |
| EvaluateAheadPaused | 4 | no | 17594 | 0.671 | 10407 | 7175 | 14438 | 0 | 0 | 17 % | 1.75 | 1.28 | n/a | 17706, 17594, 17181 |
| EvaluateClosure | 2 | no | 17838 | 0.680 | 10608 | 7230 | 13753 | 57 | 0 | 22 % | 1.56 | 1.23 | n/a | 17909, 17477, 17838 |
| EvaluateClosure | 4 | no | 17340 | 0.661 | 10651 | 6800 | 15263 | 49 | 0 | 20 % | 1.94 | 1.32 | n/a | 17491, 17340, 17123 |
| EvaluateClosurePreorder | 2 | no | 17762 | 0.677 | 10763 | 6946 | 13272 | 44 | 0 | 33 % | 1.44 | 1.21 | n/a | 17762, 17797, 17676 |
| EvaluateClosurePreorder | 4 | no | 16943 | 0.646 | 10251 | 6717 | 15032 | 44 | 0 | 28 % | 1.84 | 1.33 | n/a | 17628, 16943, 16897 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
