# BuildHost fork baseline (BuildHost/task-00)

- original BuildHost: 5.9.0-1.26357.3+35d9211b841e7613c1d2f8f5af6d628ace696c4c; fork: 5.9.0-fork+35d9211b841e7613c1d2f8f5af6d628ace696c4c
- runs: original 4, fork 4; round 0 is warmup: only its search answer and BuildHost processes are checked, equivalence and timings use rounds 1+
- reference: original round 1, 142 projects
- natural variance (original round 1 against round 2): 0 difference(s)
- original rounds 3+ against the reference: natural 0, unexplained 0
- fork rounds 1+ against the reference: **identical**; natural 0, unexplained 0
- distinct search answers in all runs: original 1, fork 1
- runs finished, BuildHost processes recorded, from own folder, none left 10 s after the first search: ok
- result: **equivalent**
- only the captured fields are compared; see the limits of SolutionObservation in the task-00 report

| metric (median of rounds 1+) | original | fork | fork / original |
|---|---|---|---|
| load ms | 44236 | 44000 | 0.995 |
| first search ms | 29794 | 31682 | 1.063 |
| client CPU ms | 6500 | 7906 | 1.216 |
| client peak working set MB | 268 | 272 | 1.018 |
| BuildHost processes | 2 | 2 | 1.000 |
| BuildHost CPU ms (lower bound) | 59625 | 59359 | 0.996 |
| largest BuildHost peak working set MB | 536 | 536 | 1.000 |

Per-round load ms: original 43889, 44236, 45458; fork 44000, 43986, 46885
Per-round client CPU ms: original 6188, 6500, 7531; fork 7922, 5984, 7906
