# Build order probe (BuildHost/task-02, step 0)

- runs: 12, round 0 is warmup; 434 design-time builds per run; logical processors: 16
- results: **identical** — every run built the same 434 project and target framework results as the first client run

| order | workers | wall ms (median) | against client | builds ms | root evaluations ms (summed) | evaluations during builds | wall per round |
|---|---|---|---|---|---|---|---|
| Client | 0 | 34348 | 1.000 | 27515 | 6798 | 250 | 34348, 33247, 34685 |
| ReferencesFirst | 0 | 31067 | 0.904 | 23597 | 7409 | 74 | 29665, 31067, 31494 |
| EvaluateAhead | 15 | 31372 | 0.913 | 24170 | 27299 | 74 | 31733, 31372, 31286 |

Builds ms and root evaluations ms add up each build and evaluation; with evaluate-ahead they overlap, and only wall is elapsed time.
