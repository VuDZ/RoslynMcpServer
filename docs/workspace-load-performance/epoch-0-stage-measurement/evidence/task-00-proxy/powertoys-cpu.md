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
- runs: 2
- note: all CPUs, warm, CPU sampling
- startedBy: scripts/Measure-WorkspaceLoad.ps1

Stage columns are estimates from log timestamps. open = capture line - tools/call - ReplayMs
(lock wait, BuildHost start, evaluation, design-time build, IPC, snapshot). post-open = first
watcher line - capture line (execution gate, publication, diagnostics, input map).
watchers = Loaded line - first watcher line. response = handler completed - Loaded line.

| run | kind | call | state | client ms | handler ms | open | replay | post-open | watchers | response | projects | diag lines | MSBuild failures | WS MB |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | warmup | load | ok | 42187 | 42180 | 39469 | 936 | 1655 | 63 | 57 | 248 | 0 | 2279 | 483.9 |
| 1 | warmup | find | ok | 10394 | 10393 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 2 | measured | load | ok | 42194 | 42191 | 39445 | 936 | 1680 | 76 | 55 | 248 | 0 | 2279 | 482.4 |
| 2 | measured | find | ok | 10263 | 10262 | n/a | n/a | n/a | n/a | n/a |  |  | 0 |  |
| 2 | measured | ramHit | ok | 21 | 20 | n/a | n/a | n/a | n/a | n/a | 248 | 0 | 0 |  |

Median over measured fresh-process runs (n=1, warmup excluded):

- load clientMs : 42194
- load handlerMs : 42191
- load openMs : 39445
- load replayMs : 936
- load postOpenMs : 1680
- load watchersMs : 76
- load responseMs : 55
- first find_symbol_definition clientMs : 10263
- server startup to initialize response ms : 195

CPU time during the call (Win32_Process kernel+user; child = BuildHost and other descendants;
busy cores = total CPU / wall time):

| run | call | server CPU ms | child CPU ms | busy cores | children |
|---|---|---|---|---|---|
| 1 | load | 9203 | 48734 | 1.37 | cmd.exe, conhost.exe, dotnet.exe, Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.exe, XamlCompiler.exe |
| 1 | find | 17078 | 0 | 1.64 | conhost.exe |
| 2 | load | 8609 | 49203 | 1.37 | cmd.exe, conhost.exe, dotnet.exe, Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.exe, XamlCompiler.exe |
| 2 | find | 16766 | 0 | 1.63 | conhost.exe |
- failed runs: 0; leftover child processes killed: 0
