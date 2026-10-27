# Task-04: server GC + ConserveMemory=9 against A+B

Here `original` means A+B without a GC override; `fork` means A+B plus the experimental host GC profile. A+B is explicitly enabled on both sides in every round.

- original BuildHost: 5.9.0-fork+35d9211b841e7613c1d2f8f5af6d628ace696c4c; fork: 5.9.0-fork+35d9211b841e7613c1d2f8f5af6d628ace696c4c
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
| load ms | 39012 | 36669 | 0.940 |
| first search ms | 17425 | 17418 | 1.000 |
| client CPU ms | 6594 | 6781 | 1.028 |
| client peak working set MB | 755 | 732 | 0.968 |
| BuildHost processes | 1 | 1 | 1.000 |
| BuildHost CPU ms (lower bound) | 61156 | 68828 | 1.125 |
| largest BuildHost peak working set MB | 1518 | 1725 | 1.136 |

Per-round load ms: original 38869, 39210, 39012; fork 37107, 36669, 36070
Per-round client CPU ms: original 6594, 6688, 6406; fork 6781, 6969, 6547
