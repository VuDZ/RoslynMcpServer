# BuildHost fork baseline (BuildHost/task-00)

- original BuildHost: 5.9.0-1.26357.3+35d9211b841e7613c1d2f8f5af6d628ace696c4c; fork: 5.9.0-fork+35d9211b841e7613c1d2f8f5af6d628ace696c4c
- runs: original 4, fork 4; round 0 is warmup: only its search answer and BuildHost processes are checked, equivalence and timings use rounds 1+
- reference: original round 1, 434 projects
- natural variance (original round 1 against round 2): 0 difference(s)
- original rounds 3+ against the reference: natural 0, unexplained 0
- fork rounds 1+ against the reference: **identical**; natural 0, unexplained 0
- distinct search answers in all runs: original 1, fork 1
- runs finished, BuildHost processes recorded, from own folder, none left 10 s after the first search: ok
- result: **equivalent**
- only the captured fields are compared; see the limits of SolutionObservation in the task-00 report

| metric (median of rounds 1+) | original | fork | fork / original |
|---|---|---|---|
| load ms | 107838 | 100420 | 0.931 |
| first search ms | 50122 | 50244 | 1.002 |
| client CPU ms | 16734 | 16953 | 1.013 |
| client peak working set MB | 739 | 721 | 0.976 |
| BuildHost processes | 1 | 1 | 1.000 |
| BuildHost CPU ms (lower bound) | 122203 | 112828 | 0.923 |
| largest BuildHost peak working set MB | 1511 | 1546 | 1.023 |

Per-round load ms: original 108709, 107560, 107838; fork 100774, 100420, 100032
Per-round client CPU ms: original 17094, 16734, 16000; fork 16953, 16953, 16500
