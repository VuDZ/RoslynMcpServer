# GC mode matrix for load and first declaration search (D/E0/task-12)

- solution: Roslyn.slnx
- solutionCommit: 013d3a758df6c137497ff37a93f0d4bed103853a
- spikeCommit: 3c4dc0c3c1b8
- logicalProcessors: 16
- os: Microsoft Windows NT 10.0.26200.0
- dotnetSdks: 10.0.301
- matchName: CSharpCompilationOptions
- missingName: NoSuchDeclarationName7d3f2a
- gcModes: workstation, server
- degrees: 1, 4
- runs: 5
- cpu: AMD Ryzen 7 9800X3D 8-Core Processor (8 cores)
- ramGb: 93.7

Verdict: **go (server)**. Thresholds fixed before measuring, degree 1, both queries, against workstation GC: first-search wall <= 0.80, load <= 1.05, process peak working set <= 1.25, final working set <= 1.25; every answer identical. Degree 4 is informational.

- match: identical answers True (1 distinct); modes within thresholds: server
- missing: identical answers True (1 distinct); modes within thresholds: server

Medians over measured rounds (warmup excluded). Ratios compare with workstation GC at the same degree. Search peak WS is sampled every 25 ms during the first search; process peak and final WS are read after the second search, without a forced collection.

| query | gc | dop | runs | first search ms | ratio | GC pause ms | GCs | busy cores | search peak WS MB | process peak WS MB | ratio | final WS MB | ratio | final GC heap MB | second search ms | load s | ratio | load GC pause ms | projects | declarations |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| match | workstation | 1 | 5 | 21458 | 1 | 8400 | 203 | 2.97 | 5439 | 5442 | 1 | 5442 | 1 | 4743 | 64 | 40.9 | 1 | 167 | 434 | 4 |
| match | workstation | 4 | 5 | 16659 | 1 | 9192 | 204 | 5.38 | 5472 | 5474 | 1 | 5474 | 1 | 4822 | 27 | 40.6 | 1 | 158 | 434 | 4 |
| match | server | 1 | 5 | 12864 | 0.6 | 1891 | 30 | 5.1 | 6193 | 6194 | 1.138 | 6194 | 1.138 | 4975 | 61 | 37.8 | 0.925 | 115 | 434 | 4 |
| match | server | 4 | 5 | 9706 | 0.583 | 1824 | 32 | 11.86 | 6314 | 6316 | 1.154 | 6316 | 1.154 | 4785 | 33 | 36.9 | 0.907 | 122 | 434 | 4 |
| missing | workstation | 1 | 5 | 17692 | 1 | 8000 | 200 | 3.12 | 5317 | 5320 | 1 | 5320 | 1 | 4672 | 33 | 40.7 | 1 | 161 | 434 | 0 |
| missing | workstation | 4 | 5 | 14922 | 1 | 8826 | 200 | 5 | 5344 | 5345 | 1 | 5345 | 1 | 4726 | 13 | 40.8 | 1 | 159 | 434 | 0 |
| missing | server | 1 | 5 | 10018 | 0.566 | 1795 | 30 | 6.24 | 6315 | 6318 | 1.188 | 6318 | 1.188 | 5117 | 33 | 37.9 | 0.932 | 114 | 434 | 0 |
| missing | server | 4 | 5 | 7429 | 0.498 | 1869 | 30 | 11.54 | 6443 | 6447 | 1.206 | 6447 | 1.206 | 5140 | 23 | 37.8 | 0.927 | 114 | 434 | 0 |
