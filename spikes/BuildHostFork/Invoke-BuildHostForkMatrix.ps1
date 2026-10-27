<#
.SYNOPSIS
    BuildHost/task-02 stage 3: loads a solution with the original BuildHost of Roslyn 5.9.0 and with several variants
    of the fork in one run, rotating the order of the sides every round, and compares each variant with the original.

.DESCRIPTION
    The same stand as Invoke-BuildHostForkBaseline.ps1: the spike is built once, bin-fork holds the fork in
    BuildHost-netcore, and every load is a fresh client process. The variants differ only in the environment of the
    fork side (the GC variants also change the runtimeconfig of their separate host copy):

      fork            no optimizations: the time reference of the task-02 thresholds
      fork-a          part A, references first (ROSLYN_BUILDHOST_REFERENCES_FIRST)
      fork-b          part B, evaluation ahead (ROSLYN_BUILDHOST_EVALUATE_AHEAD)
      fork-ab         A and B
      fork-ab-trace   A and B with the timing trace of BuildHost/task-01, against fork-ab for the cost of tracing
      fork-ab-gc      A and B, server GC with ConserveMemory=9 in the host only (task-04 experiment)
      fork-ab-gc-trace  the same GC experiment with the trace
      fork-abc        A, B and candidate C of task-04: one SDK resolution cache per project
                      (ROSLYN_BUILDHOST_SHARED_SDK_CACHE)
      fork-abc-trace  A, B and C with the trace
      fork-abc-gc     A, B and C with the GC experiment of task-04 (BuildHost/task-05)
      fork-ac         A and C without evaluation ahead, for the build time next to B (BuildHost/task-05)
      fork-ac-trace   A and C with the trace
      fork-abc-w<N>   A, B and C with N workers of part B, one per value of -EvaluateAheadWorkers (BuildHost/task-05)

    Each round runs every side once; the order is rotated by one side per round, so no side always goes first or
    always follows the same one. Round 0 is warmup.

    The original runs go to the top of the result folder, each variant to a subfolder of its own. matrix.md holds the
    answer of every variant against the original by the rules of task-00 and the medians of all sides, as ratios to
    the original and to the fork without optimizations; <variant>\summary.md holds each comparison in full, and
    fork-ab-trace\trace-summary.md where the load time goes. Without the fork side the ratios are to fork-ab, the
    reference of the task-04 candidates, and without both to fork-abc, the production BuildHost and the reference of
    task-05. The GC and C variants are followed by full RPC captures of the original twice
    and of every variant, compared field by field (rpc-*-raw.txt, rpc-*-glob-sets.txt). The script fails when a
    variant is not shown equivalent or its RPC answer differs beyond the order of glob excludes and removes and, on
    Windows, the letter case of paths; every variant is compared before it fails.

.PARAMETER Solution
    Solution to load.

.PARAMETER Name
    Name for the first search. Default: a name no declaration has.

.PARAMETER Runs
    Measured rounds; one warmup round is added. Default 3.

.PARAMETER OutDirectory
    Result folder. Default: results\matrix-<time> next to this script (not tracked by Git).

.PARAMETER Variants
    Fork variants to run, in the order of the table. Default: the original five; GC variants are opt-in.
    With a GC variant, full netcore ProjectFileInfo RPC captures and comparisons follow the timed rounds.

.PARAMETER RpcOnly
    Only the full RPC captures and comparisons of the original and of every listed variant, without timed rounds
    and without matrix.md.

.PARAMETER EvaluateAhead
    Worker threads of part B in the variants with B. Default 4.

.PARAMETER EvaluateAheadWorkers
    Adds a variant fork-abc-w<N> for each value: A, B and C with N workers of part B, measured in the same rounds as
    the listed variants. For the worker count of BuildHost/task-05.

.PARAMETER EvaluateAheadSolution
    The solution entries part B evaluates ahead: all (default), a number of entries after the latest requested one,
    or off.

.EXAMPLE
    .\spikes\BuildHostFork\Invoke-BuildHostForkMatrix.ps1 -Solution D:\src\roslyn\Roslyn.slnx
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

    [ValidateSet('fork', 'fork-a', 'fork-b', 'fork-ab', 'fork-ab-trace', 'fork-ab-gc', 'fork-ab-gc-trace', 'fork-abc', 'fork-abc-trace', 'fork-abc-gc', 'fork-ac', 'fork-ac-trace')]
    [string[]]$Variants = @('fork', 'fork-a', 'fork-b', 'fork-ab', 'fork-ab-trace'),

    [switch]$RpcOnly,

    [ValidateRange(1, 64)]
    [int]$EvaluateAhead = 4,

    [ValidatePattern('^(all|off|[1-9][0-9]*)$')]
    [string]$EvaluateAheadSolution = 'all',

    [ValidateRange(1, 64)]
    [int[]]$EvaluateAheadWorkers = @()
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')
. (Join-Path $PSScriptRoot 'BuildHostForkCommon.ps1')

# gc: the variant runs on the separate host copy with the GC experiment of task-04. rpc: the run ends with full RPC
# captures of every side, because the variant changes how the host collects ProjectFileInfo or runs on another GC.
$definitions = @{
    'fork' = @{ referencesFirst = $false; evaluateAhead = 0; sharedSdkCache = $false; trace = $false; rpc = $false; gc = $false }
    'fork-a' = @{ referencesFirst = $true; evaluateAhead = 0; sharedSdkCache = $false; trace = $false; rpc = $false; gc = $false }
    'fork-b' = @{ referencesFirst = $false; evaluateAhead = $EvaluateAhead; sharedSdkCache = $false; trace = $false; rpc = $false; gc = $false }
    'fork-ab' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; sharedSdkCache = $false; trace = $false; rpc = $false; gc = $false }
    'fork-ab-trace' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; sharedSdkCache = $false; trace = $true; rpc = $false; gc = $false }
    'fork-ab-gc' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; sharedSdkCache = $false; trace = $false; rpc = $true; gc = $true }
    'fork-ab-gc-trace' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; sharedSdkCache = $false; trace = $true; rpc = $true; gc = $true }
    'fork-abc' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; sharedSdkCache = $true; trace = $false; rpc = $true; gc = $false }
    'fork-abc-trace' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; sharedSdkCache = $true; trace = $true; rpc = $true; gc = $false }
    'fork-abc-gc' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; sharedSdkCache = $true; trace = $false; rpc = $true; gc = $true }
    'fork-ac' = @{ referencesFirst = $true; evaluateAhead = 0; sharedSdkCache = $true; trace = $false; rpc = $false; gc = $false }
    'fork-ac-trace' = @{ referencesFirst = $true; evaluateAhead = 0; sharedSdkCache = $true; trace = $true; rpc = $false; gc = $false }
}
# The sides of this run: the listed variants and one per worker count. A separate list, since -Variants keeps its
# ValidateSet for every later assignment.
$selected = @($Variants)
foreach ($workers in @($EvaluateAheadWorkers | Select-Object -Unique)) {
    $definitions["fork-abc-w$workers"] = @{ referencesFirst = $true; evaluateAhead = $workers; sharedSdkCache = $true; trace = $false; rpc = $false; gc = $false }
    $selected += "fork-abc-w$workers"
}
$selected = @($selected | Select-Object -Unique)

# PowerShell's location can differ from the process directory used by .NET and the child host.
$Solution = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Solution)
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) { throw "Solution not found: $Solution" }
$pinned = Assert-PinnedSdk (Split-Path $Solution)
if ($pinned) { Write-Host "Solution pins .NET SDK ${pinned}: installed." }

$copies = Initialize-ForkCopy $repo $PSScriptRoot
$originalDll = Join-Path $copies.originalBin 'BuildHostFork.dll'
$forkDll = Join-Path $copies.forkBin 'BuildHostFork.dll'
$gcDll = $null
if (@($selected | Where-Object { $definitions[$_].gc }).Count -gt 0) {
    $gcDll = Join-Path (Initialize-GcForkCopy $copies.forkBin) 'BuildHostFork.dll'
}

if (-not $SkipRestore) {
    Write-Host "Restoring $Solution"
    Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed'
}

if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('matrix-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDirectory)
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
foreach ($variant in $selected) {
    # A reused -OutDirectory still holds the runs and trace of an earlier matrix; the comparison would read them too.
    $variantDirectory = Join-Path $OutDirectory $variant
    if (Test-Path -LiteralPath $variantDirectory) { Remove-Item -LiteralPath $variantDirectory -Recurse -Force }
    New-Item -ItemType Directory -Path $variantDirectory | Out-Null
}
$environment = Get-Environment $Solution $repo ([ordered]@{ name = $Name; runs = $Runs; fork = $copies.version; variants = ($selected -join ', ')
        evaluateAhead = $EvaluateAhead; evaluateAheadSolution = $EvaluateAheadSolution; evaluateAheadWorkers = (@($EvaluateAheadWorkers) -join ', ')
    })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

$sides = @('original') + $selected
for ($round = 0; -not $RpcOnly -and $round -le $Runs; $round++) {
    $first = $round % $sides.Count
    $order = @($sides[$first..($sides.Count - 1)]) + @(if ($first -gt 0) { $sides[0..($first - 1)] })
    foreach ($side in $order) {
        $label = "$side round $round"
        if ($round -eq 0) { $label += ' (warmup)' }
        Write-Host $label
        $directory = $OutDirectory
        $fork = $side -ne 'original'
        if ($fork) {
            $directory = Join-Path $OutDirectory $side
            $definition = $definitions[$side]
            Set-ForkOptimizations $definition.referencesFirst $definition.evaluateAhead $EvaluateAheadSolution $definition.sharedSdkCache
            if ($definition.trace) { $env:ROSLYN_BUILDHOST_TRACE_DIRECTORY = Join-Path $directory (Join-Path 'trace' "fork-round$round") }
        }

        $prefix = if ($fork) { 'fork' } else { 'original' }
        $output = Join-Path $directory "$prefix-round$round.json"
        $dll = if ($fork -and $definitions[$side].gc) { $gcDll } elseif ($fork) { $forkDll } else { $originalDll }
        try { $exitCode = Invoke-Spike $dll @('observe', $Solution, $output, $Name) (Join-Path $directory "$prefix-round$round.out.txt") $fork }
        finally { Remove-Item Env:ROSLYN_BUILDHOST_TRACE_DIRECTORY -ErrorAction SilentlyContinue }
        if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw "$label failed (exit code $exitCode); see $output" }
    }
}

# These are separate correctness loads, after all timing rounds. Raw captures retain every field and array order.
$rpcFailures = @()
if ($RpcOnly -or @($selected | Where-Object { $definitions[$_].rpc }).Count -gt 0) {
    $rpcOriginal = Join-Path $OutDirectory 'rpc-original.json'
    foreach ($side in @('original', 'original-repeat') + $selected) {
        $fork = $side -notlike 'original*'
        $dll = if ($fork -and $definitions[$side].gc) { $gcDll } elseif ($fork) { $forkDll } else { $originalDll }
        if ($fork) {
            $definition = $definitions[$side]
            Set-ForkOptimizations $definition.referencesFirst $definition.evaluateAhead $EvaluateAheadSolution $definition.sharedSdkCache
        }
        $directory = if ($fork) { Join-Path $OutDirectory $side } else { $OutDirectory }
        $output = if ($fork) { Join-Path $directory 'rpc.json' } else { Join-Path $directory "rpc-$side.json" }
        $hostDll = Join-Path (Split-Path $dll) 'BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll'
        Write-Host "Full RPC capture: $side"
        $exitCode = Invoke-Spike $dll @('rpc-capture', $hostDll, $Solution, $output) (Join-Path $directory "rpc-$side.out.txt") $fork
        if ($exitCode -ne 0) { throw "RPC capture failed: $side (exit code $exitCode)" }
        if ($side -eq 'original') { continue }
        # Upstream hash sets change Excludes/Removes order even between the two original controls.
        & dotnet $originalDll rpc-compare $rpcOriginal $output | Out-File -LiteralPath (Join-Path $directory "rpc-$side-raw.txt") -Encoding utf8
        if ($LASTEXITCODE -notin @(0, 1)) { throw "Raw RPC comparison failed: $side" }
        & dotnet $originalDll rpc-compare $rpcOriginal $output glob-sets | Out-File -LiteralPath (Join-Path $directory "rpc-$side-glob-sets.txt") -Encoding utf8
        if ($LASTEXITCODE -eq 1) { $rpcFailures += $side } elseif ($LASTEXITCODE -ne 0) { throw "RPC comparison failed: $side" }
    }
}

if ($RpcOnly) {
    Write-Host ''
    Write-Host "Results: $OutDirectory"
    Write-Host 'rpc-*-glob-sets.txt holds each comparison; JSON files hold project paths.'
    if ($rpcFailures.Count -gt 0) { throw "Full RPC fields differ beyond glob set order and path case: $($rpcFailures -join ', ')" }
    return
}

Write-Host ''
& dotnet $originalDll matrix $OutDirectory @selected
$equivalent = $LASTEXITCODE -eq 0
foreach ($variant in $selected | Where-Object { $definitions[$_].trace }) {
    # As in the baseline: a solution outside this public repository may be private, so its reports name projects by
    # placeholders.
    $external = -not $Solution.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    $summaryArguments = @('trace-summary', (Join-Path $OutDirectory $variant))
    if ($external) { $summaryArguments += 'redact' }
    Write-Host ''
    Invoke-Native 'dotnet' (@($originalDll) + $summaryArguments) 'Trace summary failed'
}
Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'matrix.md and <variant>\summary.md compare the workspace; <variant>\trace-summary.md shows where the time goes. GC and C variants also write rpc-*-raw.txt and rpc-*-glob-sets.txt. JSON and trace files hold project paths.'
if (-not $equivalent) { throw 'A fork variant is not shown equivalent to the original; see matrix.md.' }
if ($rpcFailures.Count -gt 0) { throw "Full RPC fields differ beyond glob set order and path case: $($rpcFailures -join ', ')" }
