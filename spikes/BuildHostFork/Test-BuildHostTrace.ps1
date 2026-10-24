<#
.SYNOPSIS
    BuildHost/task-01: checks the load trace of the fork on a generated solution with the cases the trace must
    show: several target frameworks, a conditional import, ReferenceOutputAssembly=false, a project found only
    through a reference, repeated output path queries for a project the workspace cannot load, and a solution
    entry the workspace does not load. Also checks that the trace changes nothing in the load, including when its
    directory cannot be created.

.DESCRIPTION
    Builds the fork and the spike (bin-fork), generates the fixture in results\trace-check-<id> and loads it three
    times: with the trace on, with the trace off, and with the trace variable naming an existing file. Checks the
    trace, report.json and graph.json of the first load and that all three loads observe the same workspace.
    Throws on the first failed check. A write failure in the middle of a load is not reproduced here.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')
. (Join-Path $PSScriptRoot 'BuildHostForkCommon.ps1')

function Write-Utf8([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

function Assert-Trace([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Trace check failed: $Message" }
    Write-Host "ok: $Message"
}

# App -> Lib (two TFMs, net8.0 adds a file through a conditional import), App -> Tool (ReferenceOutputAssembly=false,
# not in the solution), App and Second -> Native (a .proj: the workspace cannot load it and asks the host for its
# output path). Native is SDK-style so that on Windows the client also sends it to the netcore host: a project
# without Sdk goes to the net472 host there, which does not trace. Second references Lib for net8.0 only. Cpp is a
# .vcxproj solution entry the workspace does not load and never sends to a host. Empty Directory.Build files keep
# the repository's props out.
function New-TraceFixture([string]$Directory) {
    $sdkProject = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework{0}>{1}</TargetFramework{0}></PropertyGroup>{2}</Project>'
    Write-Utf8 (Join-Path $Directory 'Directory.Build.props') '<Project />'
    Write-Utf8 (Join-Path $Directory 'Directory.Build.targets') '<Project />'
    $libImport = '<ItemGroup><Compile Remove="net8/**" /></ItemGroup><Import Project="tfm-$(TargetFramework).props" Condition="Exists(''tfm-$(TargetFramework).props'')" />'
    Write-Utf8 (Join-Path $Directory 'Lib/Lib.csproj') ($sdkProject -f 's', 'net10.0;net8.0', $libImport)
    Write-Utf8 (Join-Path $Directory 'Lib/tfm-net8.0.props') '<Project><ItemGroup><Compile Include="net8/Net8Only.cs" /></ItemGroup></Project>'
    Write-Utf8 (Join-Path $Directory 'Lib/Class1.cs') "namespace Lib;`npublic class Class1 { }`n"
    Write-Utf8 (Join-Path $Directory 'Lib/net8/Net8Only.cs') "namespace Lib;`ninternal static class Net8Only { }`n"
    Write-Utf8 (Join-Path $Directory 'Tool/Tool.csproj') ($sdkProject -f '', 'net10.0', '')
    Write-Utf8 (Join-Path $Directory 'Tool/Program.cs') "namespace Tool;`npublic static class Entry { }`n"
    Write-Utf8 (Join-Path $Directory 'Native/Native.proj') ($sdkProject -f '', 'net10.0', '')
    $appReferences = '<ItemGroup><ProjectReference Include="../Lib/Lib.csproj" /><ProjectReference Include="../Tool/Tool.csproj" ReferenceOutputAssembly="false" /><ProjectReference Include="../Native/Native.proj" /></ItemGroup>'
    Write-Utf8 (Join-Path $Directory 'App/App.csproj') ($sdkProject -f '', 'net10.0', $appReferences)
    Write-Utf8 (Join-Path $Directory 'App/Program.cs') "namespace App;`npublic static class Entry { }`n"
    $secondReferences = '<ItemGroup><ProjectReference Include="../Lib/Lib.csproj" SetTargetFramework="TargetFramework=net8.0" /><ProjectReference Include="../Native/Native.proj" /></ItemGroup>'
    Write-Utf8 (Join-Path $Directory 'Second/Second.csproj') ($sdkProject -f '', 'net10.0', $secondReferences)
    Write-Utf8 (Join-Path $Directory 'Second/Class1.cs') "namespace Second;`npublic class Class1 { }`n"
    $solution = Join-Path $Directory 'Trace.slnx'
    Write-Utf8 (Join-Path $Directory 'Cpp/Cpp.vcxproj') '<Project />'
    Write-Utf8 $solution "<Solution>`n  <Project Path=`"App/App.csproj`" />`n  <Project Path=`"Lib/Lib.csproj`" />`n  <Project Path=`"Second/Second.csproj`" />`n  <Project Path=`"Cpp/Cpp.vcxproj`" />`n</Solution>`n"
    foreach ($project in 'App/App.csproj', 'Second/Second.csproj') {
        Invoke-Native 'dotnet' @('restore', (Join-Path $Directory $project), '-nologo', '-v', 'q') "Fixture restore failed: $project"
    }
    return $solution
}

$copies = Initialize-ForkCopy $repo $PSScriptRoot
$fork = Join-Path $copies.forkBin 'BuildHostFork.dll'
$directory = Join-Path $PSScriptRoot ('results/trace-check-' + [Guid]::NewGuid().ToString('N'))
$solution = New-TraceFixture (Join-Path $directory 'fixture')
$trace = Join-Path $directory 'trace'

# Loads the fixture once with the trace variable set to $TraceDirectory, or unset for $null, and returns the
# observation. The traced and untraced loads must observe the same workspace.
function Invoke-FixtureLoad([string]$Label, [string]$TraceDirectory) {
    $output = Join-Path $directory "observe-$Label.json"
    if ($TraceDirectory) { $env:ROSLYN_BUILDHOST_TRACE_DIRECTORY = $TraceDirectory }
    try { $exitCode = Invoke-Spike $fork @('observe', $solution, $output) (Join-Path $directory "observe-$Label.out.txt") }
    finally { Remove-Item Env:ROSLYN_BUILDHOST_TRACE_DIRECTORY -ErrorAction SilentlyContinue }
    if ($exitCode -ne 0) { throw "Load '$Label' failed (exit code $exitCode); see observe-$Label.out.txt" }
    return Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
}

function Get-Workspace($Observation) {
    ($Observation.Projects | ConvertTo-Json -Depth 20 -Compress) + "`n" + ($Observation.Diagnostics | ConvertTo-Json -Compress)
}

$traced = Invoke-FixtureLoad 'traced' $trace
Invoke-Native 'dotnet' @($fork, 'trace-report', $trace) 'Trace report failed'

$report = Get-Content -LiteralPath (Join-Path $trace 'report.json') -Raw | ConvertFrom-Json
$graph = Get-Content -LiteralPath (Join-Path $trace 'graph.json') -Raw | ConvertFrom-Json
$spans = @(Get-ChildItem -LiteralPath $trace -Filter 'buildhost-*.jsonl' | ForEach-Object { Get-Content -LiteralPath $_.FullName } |
    ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.event -eq 'span' })
function Get-Spans([string]$Kind, [string]$Project) {
    @($spans | Where-Object { $_.kind -eq $Kind -and $_.PSObject.Properties['path'] -and (Split-Path $_.path -Leaf) -eq $Project })
}
function Get-Edges([string]$From, [string]$To, [string]$Provenance) {
    @($graph.Edges | Where-Object { (Split-Path $_.From -Leaf) -eq $From -and (Split-Path $_.To -Leaf) -eq $To -and $_.Provenance -eq $Provenance })
}

$clock = $report.Clock
Assert-Trace ($report.Complete -and $report.UnreadableLines -eq 0 -and $report.SpansWithoutParent -eq 0) 'the host trace is complete: every line reads and every span has its parent'
Assert-Trace ($clock.Agrees -and $clock.Matched -gt 0 -and $clock.OutsideOperation -eq 0 -and $clock.UnclaimedRequests -eq 0 -and $clock.Violations -eq 0) "every Evaluate and Build request lies inside its client operation ($($clock.Matched) matched)"
Assert-Trace (@($clock.Untraced).Count -eq 0) 'every Evaluate and Build operation was served by the traced host'
$libBuilds = @(Get-Spans 'build-instance' 'Lib.csproj')
Assert-Trace (($libBuilds.targetFramework | Sort-Object) -join ',' -eq 'net10.0,net8.0') 'Lib is built once per target framework'
$libReevaluations = @(Get-Spans 'reevaluate' 'Lib.csproj')
$libSwitches = @($libReevaluations | Where-Object { $_.PSObject.Properties['targetFramework'] -and -not $_.PSObject.Properties['restoresTargetFramework'] })
$libRestores = @($libReevaluations | Where-Object { $_.PSObject.Properties['restoresTargetFramework'] -and $_.restoresTargetFramework -eq $true -and -not $_.PSObject.Properties['targetFramework'] })
Assert-Trace ($libReevaluations.Count -eq 3 -and (($libSwitches.targetFramework | Sort-Object) -join ',' -eq 'net10.0,net8.0') -and $libRestores.Count -eq 1) 'Lib is re-evaluated once for each target framework and once, marked restoresTargetFramework, to restore its global properties'
Assert-Trace ((Get-Spans 'evaluate' 'Lib.csproj').Count -eq 1) 'Lib has one root evaluation: the host reuses the Project and switches its TargetFramework'
$libExtracts = @(Get-Spans 'extract' 'Lib.csproj')
$net8 = @($libExtracts | Where-Object { $_.targetFramework -eq 'net8.0' })[0].documents
$net10 = @($libExtracts | Where-Object { $_.targetFramework -eq 'net10.0' })[0].documents
Assert-Trace ($net8 -eq $net10 + 1) "the conditional import adds a document to net8.0 only ($net8 against $net10)"
Assert-Trace ((Get-Edges 'App.csproj' 'Tool.csproj' 'evaluated ProjectReference item' | Where-Object { $_.ReferenceOutputAssembly -eq $false }).Count -eq 1) 'App -> Tool is an item with ReferenceOutputAssembly=false'
Assert-Trace ((Get-Edges 'App.csproj' 'Tool.csproj' 'client load during Resolve').Count -eq 1) 'Tool, outside the solution, is loaded while App resolves its references'
Assert-Trace ((Get-Edges 'App.csproj' 'Lib.csproj' 'client load during Resolve').Count -eq 1) 'Lib is loaded while App resolves its references, before its own solution entry'
Assert-Trace ((Get-Edges 'Second.csproj' 'Lib.csproj' 'evaluated ProjectReference item').Count -eq 1) 'Second -> Lib is an item edge; its SetTargetFramework acts inside the design-time build, outside this trace'
Assert-Trace ($report.Repeats.OutputPathQueries -eq 2 -and (Get-Edges 'App.csproj' 'Native.proj' 'output path query during Resolve').Count -eq 1 -and (Get-Edges 'Second.csproj' 'Native.proj' 'output path query during Resolve').Count -eq 1) 'Native.proj gets one output path query from App and one from Second'
Assert-Trace ($report.Repeats.OutputPathQueriesWithCachedProject -eq 1 -and $report.Repeats.CachedProjectLoads -eq 1) 'the second output path query reuses the Project the first one evaluated, and that is the only load from the cache'
Assert-Trace ($report.Repeats.PathsEvaluatedMoreThanOnce -eq 0 -and $report.Repeats.RepeatedBuildsOfSamePathAndTfm -eq 0) 'no path is evaluated twice and no path and target framework is built twice'
Assert-Trace (@($graph.Nodes | Where-Object { (Split-Path $_.Path -Leaf) -eq 'Lib.csproj' -and $_.Built }).Count -eq 2) 'the graph has one Lib node per target framework'
Assert-Trace (@($graph.Nodes | Where-Object { (Split-Path $_.Path -Leaf) -eq 'Native.proj' -and -not $_.Built -and $null -eq $_.SolutionEntryKind }).Count -eq 1) 'Native, known only from references, is a node that was not built'
Assert-Trace ($report.SolutionEntriesNotLoaded -eq 1 -and @($graph.Nodes | Where-Object { (Split-Path $_.Path -Leaf) -eq 'Cpp.vcxproj' -and -not $_.Built -and $null -ne $_.SolutionEntryKind }).Count -eq 1) 'Cpp.vcxproj, a solution entry the workspace does not load, is a node that was not built'

# The trace must not change the load: the same workspace without the variable, and with a variable the host cannot
# use as a directory. The host then turns its trace off instead of failing to start.
$untraced = Invoke-FixtureLoad 'untraced' $null
$blocked = Join-Path $directory 'trace-is-a-file'
Write-Utf8 $blocked 'not a directory'
$failedTrace = Invoke-FixtureLoad 'trace-failed' $blocked
Assert-Trace ((Get-Workspace $untraced) -eq (Get-Workspace $traced)) 'the traced load observes the same projects and diagnostics as the untraced one'
Assert-Trace ((Get-Workspace $failedTrace) -eq (Get-Workspace $traced) -and (Get-Content -LiteralPath $blocked -Raw) -eq 'not a directory') 'a trace directory that cannot be created leaves the load as it is'
Write-Host "Trace checks passed: $directory"
