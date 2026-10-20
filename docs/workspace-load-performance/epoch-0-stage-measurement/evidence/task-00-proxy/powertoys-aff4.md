# Workspace load measurement

- serverVersion: 1.5.6.0
- os: Microsoft Windows 11 Pro 10.0.26200
- cpu: AMD Ryzen 7 9800X3D 8-Core Processor (8 cores, 16 logical)
- ramGb: 93.7
- dotnetSdks: 9.0.314, 10.0.201, 10.0.300, 10.0.401
- antivirus: Kaspersky, Kaspersky, Windows Defender
- affinityCount: 4
- workspace: <repo>\PowerToys.slnx
- configuration: Debug
- platform: x64
- targetFramework: 
- symbolName: GeneralViewModel
- runs: 4
- note: first 4 logical CPUs, warm, Kaspersky on
- startedBy: scripts/Measure-WorkspaceLoad.ps1

Stage columns are estimates from log timestamps. open = capture line - tools/call - ReplayMs
(lock wait, BuildHost start, evaluation, design-time build, IPC, snapshot). post-open = first
watcher line - capture line (execution gate, publication, diagnostics, input map).
watchers = Loaded line - first watcher line. response = handler completed - Loaded line.

| run | kind | call | state | client ms | handler ms | open | replay | post-open | watchers | response | projects | diag lines | MSBuild failures | WS MB |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | warmup | load | ok | 42843 | 42841 | 40099 | 938 | 1670 | 78 | 56 | 248 | 0 | 2279 | 486.1 |
| 1 | warmup | find | ok | 11238 | 11236 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 2 | measured | load | ok | 41819 | 41816 | 39687 | 673 | 1356 | 54 | 47 | 248 | 0 | 2279 | 479.1 |
| 2 | measured | find | ok | 10375 | 10374 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 3 | measured | load | ok | 44077 | 44074 | 41388 | 851 | 1707 | 73 | 55 | 248 | 0 | 2279 | 489.3 |
| 3 | measured | find | ok | 10741 | 10740 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 4 | measured | load | ok | 44065 | 44063 | 41450 | 822 | 1664 | 70 | 57 | 248 | 0 | 2279 | 487.2 |
| 4 | measured | find | ok | 10727 | 10726 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 4 | measured | ramHit | ok | 26 | 25 | n/a | n/a | n/a | n/a | n/a | 248 | 0 | 0 |  |

Median over measured fresh-process runs (n=3, warmup excluded):

- load clientMs : 44065
- load handlerMs : 44063
- load openMs : 41388
- load replayMs : 822
- load postOpenMs : 1664
- load watchersMs : 70
- load responseMs : 55
- first find_symbol_definition clientMs : 10727
- server startup to initialize response ms : 198

CPU time during the call (Win32_Process kernel+user; child = BuildHost and other descendants;
busy cores = total CPU / wall time):

| run | call | server CPU ms | child CPU ms | busy cores | children |
|---|---|---|---|---|---|
| 1 | load | 8641 | 0 | 0.2 | conhost.exe |
| 1 | find | 15594 | 0 | 1.39 | conhost.exe |
| 2 | load | 7781 | 0 | 0.19 | conhost.exe |
| 2 | find | 15391 | 0 | 1.48 | conhost.exe |
| 3 | load | 8734 | 0 | 0.2 | conhost.exe |
| 3 | find | 15625 | 0 | 1.45 | conhost.exe |
| 4 | load | 8828 | 0 | 0.2 | conhost.exe |
| 4 | find | 15344 | 0 | 1.43 | conhost.exe |
- failed runs: 0; leftover child processes killed: 0
