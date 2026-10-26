# BuildHost fork matrix (BuildHost/task-02, stage 3)

- sides: original, fork-ab, fork-abc, fork-abc-trace; each round runs every side once, in an order rotated by one side per round; round 0 is warmup
- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md
- result: **all variants equivalent**
- `/ fork-ab`: ratio to A + B, the reference of the task-04 thresholds

| side | answer | load ms | / original | / fork-ab | BuildHost CPU / fork-ab | BuildHost peak / fork-ab | client CPU / fork-ab | client peak / fork-ab |
|---|---|---|---|---|---|---|---|---|
| original | reference | 41831 | 1.000 | 1.240 | 0.966 | 0.945 | 0.943 | 0.962 |
| fork-ab | identical | 33745 | 0.807 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 |
| fork-abc | identical | 27625 | 0.660 | 0.819 | 0.834 | 1.001 | 0.966 | 0.998 |
| fork-abc-trace | identical | 27335 | 0.653 | 0.810 | 0.855 | 1.007 | 1.008 | 1.002 |

Per-round load ms (rounds 1+):

- original: 42209, 41831, 41307
- fork-ab: 33745, 33773, 33281
- fork-abc: 27625, 27836, 27073
- fork-abc-trace: 27162, 28684, 27335
