# BuildHost fork matrix (BuildHost/task-02, stage 3)

- sides: original, fork-ab, fork-ab-trace; each round runs every side once, in an order rotated by one side per round; round 0 is warmup
- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md
- result: **all variants equivalent**
- no `fork` side, so there are no ratios to the fork without optimizations

| side | answer | load ms | / original | / fork | BuildHost CPU / fork | BuildHost peak / fork | client CPU / fork | client peak / fork |
|---|---|---|---|---|---|---|---|---|
| original | reference | 43702 | 1.000 | n/a | n/a | n/a | n/a | n/a |
| fork-ab | identical | 32273 | 0.738 | n/a | n/a | n/a | n/a | n/a |
| fork-ab-trace | identical | 32433 | 0.742 | n/a | n/a | n/a | n/a | n/a |

Per-round load ms (rounds 1+):

- original: 42895, 43702, 43967
- fork-ab: 31870, 32273, 33303
- fork-ab-trace: 32173, 32433, 33179
