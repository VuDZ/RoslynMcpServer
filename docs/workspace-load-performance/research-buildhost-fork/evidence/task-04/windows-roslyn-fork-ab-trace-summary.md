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
| load ms | 47966 | 39086 | 0.815 |
| first search ms | 17369 | 17560 | 1.011 |
| client CPU ms | 9062 | 9000 | 0.993 |
| client peak working set MB | 757 | 762 | 1.006 |
| BuildHost processes | 1 | 1 | 1.000 |
| BuildHost CPU ms (lower bound) | 59750 | 63422 | 1.061 |
| largest BuildHost peak working set MB | 1448 | 1521 | 1.051 |

Per-round load ms: original 49015, 47966, 47459; fork 39463, 39024, 39086
Per-round client CPU ms: original 9484, 9000, 9062; fork 9172, 9000, 8891
