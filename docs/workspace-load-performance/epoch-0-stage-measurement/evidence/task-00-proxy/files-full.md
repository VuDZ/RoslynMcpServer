# Workspace load measurement

- serverVersion: 1.5.6.0
- os: Microsoft Windows 11 Pro 10.0.26200
- cpu: AMD Ryzen 7 9800X3D 8-Core Processor (8 cores, 16 logical)
- ramGb: 93.7
- dotnetSdks: 9.0.314, 10.0.201, 10.0.300, 10.0.401
- antivirus: Kaspersky, Kaspersky, Windows Defender
- affinityCount: 0
- workspace: <repo>\Files.slnx
- configuration: Debug
- platform: x64
- targetFramework: 
- symbolName: MainPageViewModel
- runs: 4
- note: all 16 logical CPUs, first load after restore, Kaspersky on, no prior build
- startedBy: scripts/Measure-WorkspaceLoad.ps1

Stage columns are estimates from log timestamps. open = capture line - tools/call - ReplayMs
(lock wait, BuildHost start, evaluation, design-time build, IPC, snapshot). post-open = first
watcher line - capture line (execution gate, publication, diagnostics, input map).
watchers = Loaded line - first watcher line. response = handler completed - Loaded line.

| run | kind | call | state | client ms | handler ms | open | replay | post-open | watchers | response | projects | diag lines | MSBuild failures | WS MB |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | warmup | load | ok | 21078 | 21075 | 20921 | 43 | 66 | 7 | 39 | 11 | 0 | 9 | 243.7 |
| 1 | warmup | find | ok | 10291 | 10290 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 2 | measured | load | ok | 11375 | 11373 | 11221 | 43 | 61 | 7 | 41 | 11 | 0 | 9 | 243.9 |
| 2 | measured | find | ok | 10877 | 10876 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 3 | measured | load | ok | 11323 | 11321 | 11160 | 44 | 67 | 9 | 41 | 11 | 0 | 9 | 244.4 |
| 3 | measured | find | ok | 10975 | 10974 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 4 | measured | load | ok | 11403 | 11401 | 11243 | 44 | 67 | 8 | 39 | 11 | 0 | 9 | 243.9 |
| 4 | measured | find | ok | 10947 | 10946 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 4 | measured | ramHit | ok | 14 | 13 | n/a | n/a | n/a | n/a | n/a | 11 | 0 | 0 |  |

Median over measured fresh-process runs (n=3, warmup excluded):

- load clientMs : 11375
- load handlerMs : 11373
- load openMs : 11221
- load replayMs : 44
- load postOpenMs : 67
- load watchersMs : 8
- load responseMs : 41
- first find_symbol_definition clientMs : 10947
- server startup to initialize response ms : 198
- failed runs: 0; leftover child processes killed: 0
