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
| load ms | 40878 | 38048 | 0.931 |
| first search ms | 21489 | 21381 | 0.995 |
| client CPU ms | 13172 | 13156 | 0.999 |
| client peak working set MB | 805 | 807 | 1.003 |
| BuildHost processes | 1 | 1 | 1.000 |
| BuildHost CPU ms (lower bound) | 53578 | 48797 | 0.911 |
| largest BuildHost peak working set MB | 1543 | 1524 | 0.988 |

Per-round load ms: original 40760, 41272, 40878; fork 38595, 38048, 37869
Per-round client CPU ms: original 13141, 13172, 13469; fork 13156, 13281, 12797
