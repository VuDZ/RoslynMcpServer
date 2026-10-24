<#
.SYNOPSIS
    BuildHost/task-01: checks the load trace of the fork on a generated solution with the cases the trace must
    show: several target frameworks, a conditional import, ReferenceOutputAssembly=false, a project found only
    through a reference, and repeated output path queries for a project the workspace cannot load.

.DESCRIPTION
    Builds the fork and the spike (bin-fork), generates the fixture in results\trace-check-<id>, loads it once
    with the trace on and checks the trace, report.json and graph.json. Throws on the first failed check.
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
# without Sdk goes to the net472 host there, which does not trace. Second references Lib for net8.0 only. Empty
# Directory.Build files keep the repository's props out.
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
    Write-Utf8 $solution "<Solution>`n  <Project Path=`"App/App.csproj`" />`n  <Project Path=`"Lib/Lib.csproj`" />`n  <Project Path=`"Second/Second.csproj`" />`n</Solution>`n"
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

$env:ROSLYN_BUILDHOST_TRACE_DIRECTORY = $trace
try { $exitCode = Invoke-Spike $fork @('observe', $solution, (Join-Path $directory 'observe.json')) (Join-Path $directory 'observe.out.txt') }
finally { Remove-Item Env:ROSLYN_BUILDHOST_TRACE_DIRECTORY -ErrorAction SilentlyContinue }
if ($exitCode -ne 0) { throw "Traced load failed (exit code $exitCode); see observe.out.txt" }
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

Assert-Trace ($report.Clock.Unmatched -eq 0 -and $report.Clock.Violations -eq 0 -and $report.Clock.Matched -gt 0) "every Evaluate and Build request lies inside its client operation ($($report.Clock.Matched) matched)"
$libBuilds = @(Get-Spans 'build-instance' 'Lib.csproj')
Assert-Trace (($libBuilds.targetFramework | Sort-Object) -join ',' -eq 'net10.0,net8.0') 'Lib is built once per target framework'
Assert-Trace ((Get-Spans 'reevaluate' 'Lib.csproj').Count -eq 3) 'Lib is re-evaluated for each target framework and once to restore its global properties'
Assert-Trace ((Get-Spans 'evaluate' 'Lib.csproj').Count -eq 1) 'Lib has one root evaluation: the host reuses the Project and switches its TargetFramework'
$libExtracts = @(Get-Spans 'extract' 'Lib.csproj')
$net8 = @($libExtracts | Where-Object { $_.targetFramework -eq 'net8.0' })[0].documents
$net10 = @($libExtracts | Where-Object { $_.targetFramework -eq 'net10.0' })[0].documents
Assert-Trace ($net8 -eq $net10 + 1) "the conditional import adds a document to net8.0 only ($net8 against $net10)"
Assert-Trace ((Get-Edges 'App.csproj' 'Tool.csproj' 'evaluated ProjectReference item' | Where-Object { $_.ReferenceOutputAssembly -eq $false }).Count -eq 1) 'App -> Tool is an item with ReferenceOutputAssembly=false'
Assert-Trace ((Get-Edges 'App.csproj' 'Tool.csproj' 'client load during Resolve').Count -eq 1) 'Tool, outside the solution, is loaded while App resolves its references'
Assert-Trace ((Get-Edges 'App.csproj' 'Lib.csproj' 'client load during Resolve').Count -eq 1) 'Lib is loaded while App resolves its references, before its own solution entry'
Assert-Trace ((Get-Edges 'Second.csproj' 'Lib.csproj' 'evaluated ProjectReference item').Count -eq 1) 'Second -> Lib is an item edge; its SetTargetFramework acts inside the design-time build, outside this trace'
Assert-Trace ($report.Repeats.OutputPathQueries -eq 2) 'Native.proj gets one output path query from App and one from Second'
Assert-Trace ($report.Repeats.OutputPathQueriesWithCachedProject -eq 1) 'the second output path query reuses the Project the first one evaluated'
Assert-Trace ((Get-Edges 'Second.csproj' 'Native.proj' 'output path query during Resolve').Count -eq 1) 'the repeated query is attributed to Second'
Assert-Trace (@($graph.Nodes | Where-Object { (Split-Path $_.Path -Leaf) -eq 'Lib.csproj' -and $_.Built }).Count -eq 2) 'the graph has one Lib node per target framework'
Assert-Trace (@($graph.Nodes | Where-Object { (Split-Path $_.Path -Leaf) -eq 'Native.proj' -and -not $_.Built -and $null -eq $_.SolutionEntryKind }).Count -eq 1) 'Native, known only from references, is a node that was not built'
Write-Host "Trace checks passed: $directory"
