# BuildHost fork failure scenarios (BuildHost/task-00)

- fork: 5.9.0-fork+35d9211b841e7613c1d2f8f5af6d628ace696c4c; references first (BuildHost/task-02 A): True; evaluate ahead (B): 4 workers, solution entries: all; solution for load, cancel and kill: RoslynMcpServer.sln
- delay after the first BuildHost: 1107 ms (half of the undisturbed original load after its BuildHost appeared); early cancel: 0 ms
- fixture observe, warmup + 3 rounds: fork rounds 1+ against the reference: **identical**; natural 0, unexplained 0; result: **equivalent**; diagnostics in report order (not part of the result), same as the reference: original rounds 2+ 2 of 2, fork rounds 1+ 3 of 3
- result: **all checks passed**

| scenario | outcome | host seen at ms | disturbed at ms | load ms | projects | failures / warnings | BuildHosts | left after load | check |
|---|---|---|---|---|---|---|---|---|---|
| fixture load, original | completed | 417 | n/a | 1603 | 9 | 12 / 0 | 1: netcore: <spike>\bin\Release\net10.0\BuildHost-netcore | 0 | ok |
| fixture load, fork | completed | 414 | n/a | 1433 | 9 | 12 / 0 | 1: netcore: <spike>\bin-fork\BuildHost-netcore | 0 | ok |
| fixture load, fork without its BuildHost dll | failed: System.Exception: The build host could not be found at '<spike>\bin-fork-broken\BuildHost-netcore\Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll' | n/a | n/a | 445 |  | 1 / 0 | 0:  | 0 | ok |
| load, original | completed | 419 | n/a | 2633 | 5 | 0 / 0 | 1: netcore: <spike>\bin\Release\net10.0\BuildHost-netcore | 0 | ok |
| load, fork | completed | 412 | n/a | 2462 | 5 | 0 / 0 | 1: netcore: <spike>\bin-fork\BuildHost-netcore | 0 | ok |
| cancel, original | canceled | 422 | 1543 | 2276 |  | 0 / 0 | 1: netcore: <spike>\bin\Release\net10.0\BuildHost-netcore | 0 | ok |
| cancel, fork | canceled | 427 | 1547 | 2112 |  | 0 / 0 | 1: netcore: <spike>\bin-fork\BuildHost-netcore | 0 | ok |
| kill BuildHost, original | failed: System.Exception: The server disconnected unexpectedly. | 411 | 1537 | 1538 |  | 1 / 0 | 1: netcore: <spike>\bin\Release\net10.0\BuildHost-netcore | 0 | ok |
| kill BuildHost, fork | failed: System.Exception: The server disconnected unexpectedly. | 418 | 1558 | 1558 |  | 1 / 0 | 1: netcore: <spike>\bin-fork\BuildHost-netcore | 0 | ok |
| cancel at BuildHost start, original | canceled | 426 | 426 | 465 |  | 0 / 0 | 1: netcore: <spike>\bin\Release\net10.0\BuildHost-netcore | 1 | 1 BuildHost left on both sides (upstream) |
| cancel at BuildHost start, fork | canceled | 410 | 410 | 413 |  | 0 / 0 | 1: netcore: <spike>\bin-fork\BuildHost-netcore | 1 | 1 BuildHost left on both sides (upstream) |

Fixture diagnostics (fork load; the check above compares them with the original load line by line):

- Failure: Cannot open project '<fixture>\Unsupported\Unsupported.vcxproj' because the file extension '.vcxproj' is not associated with a language.
- Failure: Msbuild failed when processing the file '' with message: "<fixture>\MultiTfm\MultiTfm.props" cannot be imported again. It was already imported at "<fixture>\MultiTfm\MultiTfm.csproj (1,185)". This is most likely a build authoring error. This subsequent import will be ignored.
- Failure: Msbuild failed when processing the file '' with message: "<fixture>\MultiTfm\MultiTfm.props" cannot be imported again. It was already imported at "<fixture>\MultiTfm\MultiTfm.csproj (1,185)". This is most likely a build authoring error. This subsequent import will be ignored.
- Failure: Msbuild failed when processing the file '' with message: "<fixture>\MultiTfm\MultiTfm.props" cannot be imported again. It was already imported at "<fixture>\MultiTfm\MultiTfm.csproj (1,185)". This is most likely a build authoring error. This subsequent import will be ignored.
- Failure: Msbuild failed when processing the file '' with message: "<fixture>\Noisy\Noisy.props" cannot be imported again. It was already imported at "<fixture>\Noisy\Noisy.csproj (1,106)". This is most likely a build authoring error. This subsequent import will be ignored.
- Failure: Msbuild failed when processing the file '<fixture>\Broken\Broken.csproj' with message: The project file could not be loaded. Unexpected end of file has occurred. The following elements are not closed: PropertyGroup, Project. Line 1, position 49.
- Failure: Msbuild failed when processing the file '<fixture>\DanglingReference\DanglingReference.csproj' with message: The referenced project ../Absent/Absent.csproj does not exist.
- Failure: Msbuild failed when processing the file '<fixture>\Noisy\Noisy.csproj' with message: Noisy build warning
- Failure: Msbuild failed when processing the file '<fixture>\Noisy\Noisy.csproj' with message: Noisy target path warning
- Failure: Msbuild failed when processing the file '<fixture>\Noisy\Noisy.csproj' with message: Noisy target path warning
- Failure: Project file not found: '<fixture>\Absent\Absent.csproj'
- Failure: Project file not found: '<fixture>\Missing\Missing.csproj'
