# BuildHost fork baseline (BuildHost/task-00)

- original BuildHost: 5.9.0-1.26357.3+35d9211b841e7613c1d2f8f5af6d628ace696c4c; fork: 5.9.0-fork+35d9211b841e7613c1d2f8f5af6d628ace696c4c
- runs: original 4, fork 4; round 0 is warmup: only its search answer and BuildHost processes are checked, equivalence and timings use rounds 1+
- reference: original round 1, 142 projects
- natural variance (original round 1 against round 2): 0 difference(s)
- original rounds 3+ against the reference: natural 0, unexplained 0
- fork rounds 1+ against the reference: **different**; natural 0, unexplained 15
- distinct search answers in all runs: original 1, fork 1
- runs finished, BuildHost processes recorded, from own folder, none left 10 s after the first search: ok
- result: **not shown equivalent: the fork differs from the original**
- only the captured fields are compared; see the limits of SolutionObservation in the task-00 report

| metric (median of rounds 1+) | original | fork | fork / original |
|---|---|---|---|
| load ms | 44058 | 39897 | 0.906 |
| first search ms | 31113 | 29014 | 0.933 |
| client CPU ms | 8031 | 8109 | 1.010 |
| client peak working set MB | 267 | 271 | 1.012 |
| BuildHost processes | 2 | 2 | 1.000 |
| BuildHost CPU ms (lower bound) | 59469 | 53516 | 0.900 |
| largest BuildHost peak working set MB | 537 | 501 | 0.933 |

Per-round load ms: original 44058, 43766, 44107; fork 40031, 39897, 39583
Per-round client CPU ms: original 8328, 8031, 8031; fork 7922, 8531, 8109

Unexplained differences of the fork: 15, all of them evaluation warnings "... cannot be imported again" (5 distinct messages in each of rounds 1-3, reference present, this run absent); other: 0. Paths and project names of the private_project are removed from this copy.
