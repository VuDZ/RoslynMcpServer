# Build order probe (BuildHost/task-02, step 0)

- runs: 12, round 0 is warmup; 142 design-time builds per run; logical processors: 6
- results: **identical** — every run built the same 142 project and target framework results as the first client run

| order | workers | wall ms (median) | against client | builds ms | root evaluations ms (summed) | evaluations during builds | wall per round |
|---|---|---|---|---|---|---|---|
| Client | 0 | 27719 | 1.000 | 14647 | 12951 | 101 | 27724, 27719, 27682 |
| ReferencesFirst | 0 | 23321 | 0.841 | 9651 | 13508 | 0 | 23031, 23321, 23476 |
| EvaluateAhead | 5 | 19588 | 0.707 | 10586 | 18751 | 0 | 20284, 19475, 19588 |

Builds ms and root evaluations ms add up each build and evaluation; with evaluate-ahead they overlap, and only wall is elapsed time.
