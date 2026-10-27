# BuildHost fork matrix (BuildHost/task-02, stage 3)

- sides: original, fork, fork-a, fork-b, fork-ab, fork-ab-trace; each round runs every side once, in an order rotated by one side per round; round 0 is warmup
- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md
- result: **all variants equivalent**
- `/ fork`: ratio to the fork without optimizations, the reference of the task-02 thresholds

| side | answer | load ms | / original | / fork | BuildHost CPU / fork | BuildHost peak / fork | client CPU / fork | client peak / fork |
|---|---|---|---|---|---|---|---|---|
| original | reference | 43967 | 1.000 | 0.984 | 1.009 | 0.999 | 1.028 | 0.998 |
| fork | identical | 44660 | 1.016 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 |
| fork-a | identical | 41606 | 0.946 | 0.932 | 0.948 | 0.990 | 1.075 | 0.993 |
| fork-b | identical | 38257 | 0.870 | 0.857 | 1.092 | 1.053 | 0.911 | 0.998 |
| fork-ab | identical | 36424 | 0.828 | 0.816 | 1.044 | 1.051 | 1.034 | 1.003 |
| fork-ab-trace | identical | 36429 | 0.829 | 0.816 | 1.056 | 1.053 | 1.067 | 1.012 |

Per-round load ms (rounds 1+):

- original: 44582, 43952, 43967
- fork: 44660, 44938, 43070
- fork-a: 42299, 41606, 41107
- fork-b: 39486, 37970, 38257
- fork-ab: 36677, 36349, 36424
- fork-ab-trace: 36429, 36803, 36284
