# BuildHost fork matrix (BuildHost/task-02, stage 3)

- sides: original, fork-ab, fork-ab-trace; each round runs every side once, in an order rotated by one side per round; round 0 is warmup
- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md
- result: **all variants equivalent**
- no `fork` side, so there are no ratios to the fork without optimizations

| side | answer | load ms | / original | / fork | BuildHost CPU / fork | BuildHost peak / fork | client CPU / fork | client peak / fork |
|---|---|---|---|---|---|---|---|---|
| original | reference | 108120 | 1.000 | n/a | n/a | n/a | n/a | n/a |
| fork-ab | identical | 84537 | 0.782 | n/a | n/a | n/a | n/a | n/a |
| fork-ab-trace | identical | 85932 | 0.795 | n/a | n/a | n/a | n/a | n/a |

Per-round load ms (rounds 1+):

- original: 108120, 110229, 106539
- fork-ab: 85095, 84537, 84428
- fork-ab-trace: 85932, 87279, 84949
