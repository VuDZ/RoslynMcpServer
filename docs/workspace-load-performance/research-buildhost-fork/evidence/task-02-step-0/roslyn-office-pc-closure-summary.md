# Build order probe (BuildHost/task-02, step 0)

- runs: 28, round 0 is warmup; 434 design-time builds of 318 projects, 129 of them multi-targeting; logical processors: 6
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | evaluation slowdown, multi-TFM | wall per round |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Client | — | no | 86661 | 1.000 | 64681 | 21980 | 21852 | 318 | 250 | 0 % | 1.00 | 0.98 | 0.98 | 86586, 86661, 87134 |
| ReferencesFirst | — | no | 78342 | 0.904 | 55776 | 22548 | 22422 | 318 | 74 | 0 % | 1.00 | 1.00 | 1.00 | 77871, 78553, 78342 |
| ReferencesFirst | — | yes | 73919 | 0.853 | 52476 | 21695 | 21568 | 318 | 74 | 0 % | 1.00 | 0.94 | 0.94 | 73902, 73919, 75735 |
| EvaluateAhead | 4 | no | 78727 | 0.908 | 57696 | 21048 | 43460 | 0 | 74 | 12 % | 1.87 | 1.02 | 1.03 | 78727, 79367, 78639 |
| EvaluateClosure | 1 | no | 76861 | 0.887 | 59622 | 17495 | 27303 | 207 | 74 | 25 % | 1.13 | 1.02 | 1.06 | 76357, 76861, 78406 |
| EvaluateClosure | 2 | no | 78834 | 0.910 | 61192 | 17733 | 34324 | 201 | 74 | 33 % | 1.46 | 1.02 | 1.07 | 78810, 78834, 79409 |
| EvaluateClosure | 2 | yes | 74922 | 0.865 | 58054 | 16784 | 32970 | 201 | 74 | 34 % | 1.46 | 0.96 | 0.99 | 74506, 74922, 74994 |

Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.
Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.
Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.
