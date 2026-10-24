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
| load ms | 42266 | 42745 | 1.011 |
| first search ms | 21719 | 21959 | 1.011 |
| client CPU ms | 12562 | 12781 | 1.017 |
| client peak working set MB | 808 | 804 | 0.995 |
| BuildHost processes | 1 | 1 | 1.000 |
| BuildHost CPU ms (lower bound) | 54781 | 55172 | 1.007 |
| largest BuildHost peak working set MB | 1545 | 1543 | 0.999 |

Per-round load ms: original 41848, 42879, 42266; fork 42745, 44271, 42388
Per-round client CPU ms: original 12531, 13031, 12562; fork 12531, 13484, 12781
