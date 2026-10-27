# BuildHost fork matrix (BuildHost/task-02, stage 3)

- sides: original, fork, fork-a, fork-b, fork-ab, fork-ab-trace; each round runs every side once, in an order rotated by one side per round; round 0 is warmup
- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md
- result: **all variants equivalent**
- `/ fork`: ratio to the fork without optimizations, the reference of the task-02 thresholds

| side | answer | load ms | / original | / fork | BuildHost CPU / fork | BuildHost peak / fork | client CPU / fork | client peak / fork |
|---|---|---|---|---|---|---|---|---|
| original | reference | 43566 | 1.000 | 1.000 | 1.003 | 0.991 | 1.030 | 1.001 |
| fork | identical | 43554 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 |
| fork-a | identical | 39123 | 0.898 | 0.898 | 0.895 | 1.002 | 1.028 | 1.009 |
| fork-b | identical | 36514 | 0.838 | 0.838 | 1.074 | 1.110 | 1.009 | 1.012 |
| fork-ab | identical | 31892 | 0.732 | 0.732 | 0.948 | 1.042 | 1.030 | 1.006 |
| fork-ab-trace | identical | 32232 | 0.740 | 0.740 | 0.973 | 1.050 | 1.004 | 0.993 |

Per-round load ms (rounds 1+):

- original: 43566, 43759, 43515
- fork: 43554, 45754, 43398
- fork-a: 39085, 39123, 39466
- fork-b: 36116, 37062, 36514
- fork-ab: 31868, 31892, 32079
- fork-ab-trace: 32223, 32232, 32627
