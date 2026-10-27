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
- diagnostics in report order (not part of the result), same as the reference: original rounds 2+ 2 of 2, fork rounds 1+ 3 of 3

| metric (median of rounds 1+) | original | fork | fork / original |
|---|---|---|---|
| load ms | 110261 | 86312 | 0.783 |
| first search ms | 45575 | 43976 | 0.965 |
| client CPU ms | 16047 | 16203 | 1.010 |
| client peak working set MB | 763 | 744 | 0.974 |
| BuildHost processes | 1 | 1 | 1.000 |
| BuildHost CPU ms (lower bound) | 124156 | 128000 | 1.031 |
| largest BuildHost peak working set MB | 1508 | 1640 | 1.088 |

Per-round load ms: original 110261, 113218, 108926; fork 85814, 87733, 86312
Per-round client CPU ms: original 16047, 14969, 17188; fork 16359, 15656, 16203
