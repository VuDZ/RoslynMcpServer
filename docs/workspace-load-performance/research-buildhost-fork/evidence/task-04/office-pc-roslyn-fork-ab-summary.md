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
| load ms | 108120 | 84537 | 0.782 |
| first search ms | 43315 | 43891 | 1.013 |
| client CPU ms | 15375 | 14719 | 0.957 |
| client peak working set MB | 742 | 730 | 0.984 |
| BuildHost processes | 1 | 1 | 1.000 |
| BuildHost CPU ms (lower bound) | 122875 | 126203 | 1.027 |
| largest BuildHost peak working set MB | 1487 | 1671 | 1.124 |

Per-round load ms: original 108120, 110229, 106539; fork 85095, 84537, 84428
Per-round client CPU ms: original 14891, 15375, 15375; fork 14703, 15188, 14719
