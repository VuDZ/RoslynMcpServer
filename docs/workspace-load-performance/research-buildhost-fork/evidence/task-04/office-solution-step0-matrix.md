# BuildHost fork matrix (BuildHost/task-02, stage 3)

- sides: original, fork-ab, fork-ab-trace; each round runs every side once, in an order rotated by one side per round; round 0 is warmup
- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md
- result: **all variants equivalent**
- no `fork` side, so there are no ratios to the fork without optimizations

| side | answer | load ms | / original | / fork | BuildHost CPU / fork | BuildHost peak / fork | client CPU / fork | client peak / fork |
|---|---|---|---|---|---|---|---|---|
| original | reference | 43341 | 1.000 | n/a | n/a | n/a | n/a | n/a |
| fork-ab | identical | 31966 | 0.738 | n/a | n/a | n/a | n/a | n/a |
| fork-ab-trace | identical | 32069 | 0.740 | n/a | n/a | n/a | n/a | n/a |

Per-round load ms (rounds 1+):

- original: 43338, 43601, 43341
- fork-ab: 31763, 32507, 31966
- fork-ab-trace: 32176, 32013, 32069
