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
- runs: 2
- note: all CPUs, warm, CPU sampling
- startedBy: scripts/Measure-WorkspaceLoad.ps1

Stage columns are estimates from log timestamps. open = capture line - tools/call - ReplayMs
(lock wait, BuildHost start, evaluation, design-time build, IPC, snapshot). post-open = first
watcher line - capture line (execution gate, publication, diagnostics, input map).
watchers = Loaded line - first watcher line. response = handler completed - Loaded line.

| run | kind | call | state | client ms | handler ms | open | replay | post-open | watchers | response | projects | diag lines | MSBuild failures | WS MB |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | warmup | load | ok | 11338 | 11336 | 11183 | 40 | 62 | 8 | 43 | 11 | 0 | 9 | 246.6 |
| 1 | warmup | find | ok | 9936 | 9935 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 2 | measured | load | ok | 11288 | 11286 | 11132 | 40 | 61 | 9 | 44 | 11 | 0 | 9 | 246.9 |
| 2 | measured | find | ok | 10890 | 10889 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 2 | measured | ramHit | ok | 13 | 12 | n/a | n/a | n/a | n/a | n/a | 11 | 0 | 0 |  |

Median over measured fresh-process runs (n=1, warmup excluded):

- load clientMs : 11288
- load handlerMs : 11286
- load openMs : 11132
- load replayMs : 40
- load postOpenMs : 61
- load watchersMs : 9
- load responseMs : 44
- first find_symbol_definition clientMs : 10890
- server startup to initialize response ms : 201

CPU time during the call (Win32_Process kernel+user; child = BuildHost and other descendants;
busy cores = total CPU / wall time):

| run | call | server CPU ms | child CPU ms | busy cores | children |
|---|---|---|---|---|---|
| 1 | load | 1891 | 10266 | 1.07 | cmd.exe, conhost.exe, dotnet.exe, XamlCompiler.exe |
| 1 | find | 13375 | 0 | 1.35 | conhost.exe, cswinrt.exe |
| 2 | load | 1828 | 10250 | 1.07 | cmd.exe, conhost.exe, dotnet.exe, XamlCompiler.exe |
| 2 | find | 14531 | 0 | 1.33 | conhost.exe |
- failed runs: 0; leftover child processes killed: 0
