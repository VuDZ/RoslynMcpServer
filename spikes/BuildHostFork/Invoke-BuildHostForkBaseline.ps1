<#
.SYNOPSIS
    BuildHost/task-00: loads a solution with the original BuildHost of Roslyn 5.9.0 and with the unchanged
    fork from forks/roslyn-buildhost, in alternating fresh processes, and compares the workspaces.

.DESCRIPTION
    The spike is built once. A copy of its output (bin-fork) gets the fork in BuildHost-netcore; the
    MSBuildWorkspace client in both copies is the same package build and starts whichever BuildHost lies
    next to it. Each round runs both sides, alternating which goes first. Round 0 is warmup.

    summary.md: equivalence of every captured project field and workspace diagnostic by the rules of
    spikes/ParallelLoad/compare.py (reference: original round 1; natural variance: original round 1 against
    round 2), identical first-search answers, the BuildHost processes of each side, and medians of load,
    first search, client CPU of the load and working set. The script fails when the fork is not shown
    equivalent. The fork must be equivalent before any optimization is measured on it.

    With -Trace (BuildHost/task-01) the fork side also writes a timing trace of its BuildHost and of the
    loader's operations to trace\fork-round<N>, and trace-summary.md gives the medians of where the load time
    goes. The original side stays untraced, so the comparison checks that tracing changes nothing and the load
    ratio shows its overhead. For a solution outside this repository the reports name projects by placeholders;
    the trace folders themselves hold full paths and stay on the machine.

.PARAMETER Solution
    Solution to load.

.PARAMETER Name
    Name for the first search. Default: a name no declaration has.

.PARAMETER Runs
    Measured rounds; one warmup round is added. Default 3.

.PARAMETER OutDirectory
    Result folder. Default: results\baseline-<time> next to this script (not tracked by Git).

.PARAMETER Trace
    Trace the fork side's loads (BuildHost/task-01).

.PARAMETER ReferencesFirst
    Run the fork side with part A of BuildHost/task-02: the fork builds the projects a project references before
    the project (ROSLYN_BUILDHOST_REFERENCES_FIRST=1). The original side stays the reference of the answer.

.PARAMETER EvaluateAhead
    Run the fork side with part B of BuildHost/task-02 on this many worker threads: they evaluate ahead the
    referenced projects and the solution entries (ROSLYN_BUILDHOST_EVALUATE_AHEAD). Default 0: off. Combines with
    -ReferencesFirst.

.PARAMETER EvaluateAheadSolution
    The solution entries part B evaluates ahead: all (default), a number of entries after the latest requested one,
    or off (ROSLYN_BUILDHOST_EVALUATE_AHEAD_SOLUTION).

.EXAMPLE
    .\spikes\BuildHostFork\Invoke-BuildHostForkBaseline.ps1 -Solution D:\src\roslyn\Roslyn.slnx -Name CSharpCompilationOptions
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Solution,

    [string]$Name = 'NoSuchDeclarationName7d3f2a',

    [ValidateRange(1, 50)]
    [int]$Runs = 3,

    [switch]$SkipRestore,

    [string]$OutDirectory,

    [switch]$Trace,

    [switch]$ReferencesFirst,

    [ValidateRange(0, 64)]
    [int]$EvaluateAhead = 0,

    [ValidatePattern('^(all|off|[1-9][0-9]*)$')]
    [string]$EvaluateAheadSolution = 'all'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')
. (Join-Path $PSScriptRoot 'BuildHostForkCommon.ps1')

$Solution = [IO.Path]::GetFullPath($Solution)
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) { throw "Solution not found: $Solution" }
$pinned = Assert-PinnedSdk (Split-Path $Solution)
if ($pinned) { Write-Host "Solution pins .NET SDK ${pinned}: installed." }

$copies = Initialize-ForkCopy $repo $PSScriptRoot
$version = $copies.version
Set-ForkOptimizations $ReferencesFirst $EvaluateAhead $EvaluateAheadSolution

if (-not $SkipRestore) {
    Write-Host "Restoring $Solution"
    Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed'
}

if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('baseline-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$environment = Get-Environment $Solution $repo ([ordered]@{ name = $Name; runs = $Runs; fork = $version; trace = [bool]$Trace; referencesFirst = [bool]$ReferencesFirst
        evaluateAhead = $EvaluateAhead; evaluateAheadSolution = $(if ($EvaluateAhead -gt 0) { $EvaluateAheadSolution } else { '' })
    })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

$sides = [ordered]@{ original = (Join-Path $copies.originalBin 'BuildHostFork.dll'); fork = (Join-Path $copies.forkBin 'BuildHostFork.dll') }
$traceRoot = $null
if ($Trace) { $traceRoot = Join-Path $OutDirectory 'trace' }
Invoke-ObserveRounds $sides $Solution $Name $Runs $OutDirectory $traceRoot

Write-Host ''
& dotnet $sides['original'] compare $OutDirectory
$equivalent = $LASTEXITCODE -eq 0
if ($Trace) {
    # A solution outside this public repository may be private: its reports name projects by placeholders. Paths
    # outside the solution folder get placeholders in any case.
    $external = -not $Solution.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    $summaryArguments = @('trace-summary', $OutDirectory)
    if ($external) { $summaryArguments += 'redact' }
    Write-Host ''
    Invoke-Native 'dotnet' (@($sides['original']) + $summaryArguments) 'Trace summary failed'
}
Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'summary.md is enough for the analysis. It names projects only when it lists differences; the JSON files hold project paths and diagnostics of the solution.'
if ($Trace) { Write-Host 'With -Trace also trace-summary.md and trace\fork-round<N>\report.md and graph.json; the rest of the trace folders (client.json, buildhost-*.jsonl, names-private.json) holds full paths.' }
if (-not $equivalent) { throw 'The fork is not shown equivalent to the original; see summary.md.' }
