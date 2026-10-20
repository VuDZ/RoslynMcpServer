# Workspace load measurement

- serverVersion: 1.5.6.0
- os: Microsoft Windows 11 Pro 10.0.26200
- cpu: AMD Ryzen 7 9800X3D 8-Core Processor (8 cores, 16 logical)
- ramGb: 93.7
- dotnetSdks: 9.0.314, 10.0.201, 10.0.300, 10.0.401
- antivirus: Kaspersky, Kaspersky, Windows Defender
- affinityCount: 0
- workspace: <repo>\PowerToys.slnx
- configuration: Debug
- platform: x64
- targetFramework: 
- symbolName: GeneralViewModel
- runs: 4
- note: all 16 logical CPUs, warm disk cache after restore, Kaspersky on
- startedBy: scripts/Measure-WorkspaceLoad.ps1

Stage columns are estimates from log timestamps. open = capture line - tools/call - ReplayMs
(lock wait, BuildHost start, evaluation, design-time build, IPC, snapshot). post-open = first
watcher line - capture line (execution gate, publication, diagnostics, input map).
watchers = Loaded line - first watcher line. response = handler completed - Loaded line.

| run | kind | call | state | client ms | handler ms | open | replay | post-open | watchers | response | projects | diag lines | MSBuild failures | WS MB |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | warmup | load | ok | 141047 | 141041 | 138257 | 928 | 1733 | 73 | 50 | 248 | 0 | 0 | 481 |
| 1 | warmup | find | ok | 10697 | 10696 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 2 | measured | load | ok | 42195 | 42192 | 39514 | 824 | 1727 | 75 | 53 | 248 | 0 | 0 | 484.4 |
| 2 | measured | find | ok | 9951 | 9950 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 3 | measured | load | ok | 42194 | 42191 | 39473 | 842 | 1751 | 75 | 50 | 248 | 0 | 0 | 483 |
| 3 | measured | find | ok | 10374 | 10373 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 4 | measured | load | ok | 42275 | 42273 | 39607 | 842 | 1696 | 74 | 54 | 248 | 0 | 0 | 484.8 |
| 4 | measured | find | ok | 9852 | 9851 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 4 | measured | ramHit | ok | 21 | 21 | n/a | n/a | n/a | n/a | n/a | 248 | 0 | 0 |  |

Median over measured fresh-process runs (n=3, warmup excluded):

- load clientMs : 42195
- load handlerMs : 42192
- load openMs : 39514
- load replayMs : 842
- load postOpenMs : 1727
- load watchersMs : 75
- load responseMs : 53
- first find_symbol_definition clientMs : 9951
- server startup to initialize response ms : 195
- failed runs: 0; leftover child processes killed: 0
