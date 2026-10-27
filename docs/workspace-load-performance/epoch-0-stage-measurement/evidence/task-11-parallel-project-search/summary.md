# Parallel declaration search matrix (D/E0/task-11)

- solution: Roslyn.slnx
- solutionCommit: 013d3a758df6c137497ff37a93f0d4bed103853a
- spikeCommit: 6174f222cd61
- logicalProcessors: 16
- os: Microsoft Windows NT 10.0.26200.0
- dotnetSdks: 10.0.301
- matchName: CSharpCompilationOptions
- missingName: NoSuchDeclarationName7d3f2a
- degrees: 1, 2, 4, 8
- runs: 5
- cpu: AMD Ryzen 7 9800X3D 8-Core Processor (8 cores)
- ramGb: 93.7

Verdict: **no-go**. Thresholds fixed before measuring: on both queries a degree of 2 or 4 with median first-search wall <= 0.70 x degree 1 and median search peak working set <= 1.25 x degree 1; every answer identical.

- match: identical answers True (1 distinct); degrees within thresholds: 
- missing: identical answers True (1 distinct); degrees within thresholds: 

Medians over measured rounds (warmup excluded). Peak working set is sampled every 25 ms during the search only.

| query | dop | runs | first search ms | ratio | CPU ms | busy cores | peak WS MB | WS ratio | GC pause ms | second search ms | load s | projects | declarations |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| match | 1 | 5 | 20726 | 1 | 59484 | 2.94 | 5443 | 1 | 8323 | 64 | 40.1 | 434 | 4 |
| match | 2 | 5 | 18118 | 0.874 | 72438 | 3.99 | 5431 | 0.998 | 8835 | 39 | 40.7 | 434 | 4 |
| match | 4 | 5 | 16785 | 0.81 | 87203 | 5.1 | 5514 | 1.013 | 9237 | 28 | 40.5 | 434 | 4 |
| match | 8 | 5 | 16625 | 0.802 | 96984 | 5.96 | 5582 | 1.025 | 9631 | 21 | 40.9 | 434 | 4 |
| missing | 1 | 5 | 17752 | 1 | 55547 | 3.14 | 5272 | 1 | 8041 | 33 | 41 | 434 | 0 |
| missing | 2 | 5 | 15734 | 0.886 | 65922 | 4.32 | 5386 | 1.022 | 8364 | 27 | 40.3 | 434 | 0 |
| missing | 4 | 5 | 15088 | 0.85 | 77734 | 5.28 | 5392 | 1.023 | 8872 | 12 | 40.7 | 434 | 0 |
| missing | 8 | 5 | 14790 | 0.833 | 80031 | 5.42 | 5446 | 1.033 | 9177 | 10 | 40.8 | 434 | 0 |
