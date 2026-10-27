# BuildHost fork matrix (BuildHost/task-02, stage 3)

- sides: original, fork, fork-a, fork-b, fork-ab, fork-ab-trace; each round runs every side once, in an order rotated by one side per round; round 0 is warmup
- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md
- result: **all variants equivalent**
- `/ fork`: ratio to the fork without optimizations, the reference of the task-02 thresholds

| side | answer | load ms | / original | / fork | BuildHost CPU / fork | BuildHost peak / fork | client CPU / fork | client peak / fork |
|---|---|---|---|---|---|---|---|---|
| original | reference | 106405 | 1.000 | 0.996 | 0.998 | 1.065 | 1.013 | 0.984 |
| fork | identical | 106840 | 1.004 | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 |
| fork-a | identical | 99002 | 0.930 | 0.927 | 0.919 | 1.055 | 1.020 | 0.976 |
| fork-b | identical | 91333 | 0.858 | 0.855 | 1.099 | 1.054 | 0.984 | 1.007 |
| fork-ab | identical | 89249 | 0.839 | 0.835 | 1.054 | 1.091 | 1.022 | 0.951 |
| fork-ab-trace | identical | 84669 | 0.796 | 0.792 | 1.042 | 1.088 | 0.989 | 0.972 |

Per-round load ms (rounds 1+):

- original: 105872, 106405, 107979
- fork: 106840, 105714, 109085
- fork-a: 98432, 99002, 99916
- fork-b: 93572, 90708, 91333
- fork-ab: 89249, 84541, 95937
- fork-ab-trace: 84669, 84489, 88150
