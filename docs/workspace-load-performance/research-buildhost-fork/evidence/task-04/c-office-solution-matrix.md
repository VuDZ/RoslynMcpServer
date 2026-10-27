# BuildHost fork matrix (BuildHost/task-02, stage 3)

- sides: original, fork-ab, fork-abc; each round runs every side once, in an order rotated by one side per round; round 0 is warmup
- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md
- result: **all variants equivalent**
- `/ fork-ab`: ratio to A + B, the reference of the task-04 thresholds

| side | answer | load ms | / original | / fork-ab | BuildHost CPU / fork-ab | BuildHost peak / fork-ab | client CPU / fork-ab | client peak / fork-ab |
|---|---|---|---|---|---|---|---|---|
| original | reference | 43828 | 1.000 | 1.361 | 1.041 | 0.918 | 0.992 | 1.019 |
| fork-ab | identical | 32193 | 0.735 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 |
| fork-abc | identical | 28992 | 0.661 | 0.901 | 0.918 | 0.996 | 0.950 | 1.027 |

Per-round load ms (rounds 1+):

- original: 44168, 43607, 43828
- fork-ab: 32350, 32038, 32193
- fork-abc: 29283, 28764, 28992
