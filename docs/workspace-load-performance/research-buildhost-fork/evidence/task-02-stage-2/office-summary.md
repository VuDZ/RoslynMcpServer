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
- diagnostics in report order (not part of the result), same as the reference: original rounds 2+ 2 of 2, fork rounds 1+ 3 of 3

| metric (median of rounds 1+) | original | fork | fork / original |
|---|---|---|---|
| load ms | 44120 | 32720 | 0.742 |
| first search ms | 33167 | 31495 | 0.950 |
| client CPU ms | 8281 | 7672 | 0.926 |
| client peak working set MB | 273 | 272 | 0.997 |
| BuildHost processes | 2 | 2 | 1.000 |
| BuildHost CPU ms (lower bound) | 58797 | 57750 | 0.982 |
| largest BuildHost peak working set MB | 540 | 552 | 1.022 |

Per-round load ms: original 44372, 43930, 44120; fork 32720, 32601, 32748
Per-round client CPU ms: original 8203, 8281, 8375; fork 7609, 7672, 7672
