<#
.SYNOPSIS
    BuildHost/task-02 stage 3: loads a solution with the original BuildHost of Roslyn 5.9.0 and with several variants
    of the fork in one run, rotating the order of the sides every round, and compares each variant with the original.

.DESCRIPTION
    The same stand as Invoke-BuildHostForkBaseline.ps1: the spike is built once, bin-fork holds the fork in
    BuildHost-netcore, and every load is a fresh client process. The variants differ only in the environment of the
    fork side:

      fork            no optimizations: the time reference of the task-02 thresholds
      fork-a          part A, references first (ROSLYN_BUILDHOST_REFERENCES_FIRST)
      fork-b          part B, evaluation ahead (ROSLYN_BUILDHOST_EVALUATE_AHEAD)
      fork-ab         A and B
      fork-ab-trace   A and B with the timing trace of BuildHost/task-01, against fork-ab for the cost of tracing

    Each round runs every side once; the order is rotated by one side per round, so no side always goes first or
    always follows the same one. Round 0 is warmup.

    The original runs go to the top of the result folder, each variant to a subfolder of its own. matrix.md holds the
    answer of every variant against the original by the rules of task-00 and the medians of all sides, as ratios to
    the original and to the fork without optimizations; <variant>\summary.md holds each comparison in full, and
    fork-ab-trace\trace-summary.md where the load time goes. The script fails when a variant is not shown equivalent.

.PARAMETER Solution
    Solution to load.

.PARAMETER Name
    Name for the first search. Default: a name no declaration has.

.PARAMETER Runs
    Measured rounds; one warmup round is added. Default 3.

.PARAMETER OutDirectory
    Result folder. Default: results\matrix-<time> next to this script (not tracked by Git).

.PARAMETER Variants
    Fork variants to run, in the order of the table. Default: all five.

.PARAMETER EvaluateAhead
    Worker threads of part B in the variants with B. Default 4.

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

    [ValidateSet('fork', 'fork-a', 'fork-b', 'fork-ab', 'fork-ab-trace')]
    [string[]]$Variants = @('fork', 'fork-a', 'fork-b', 'fork-ab', 'fork-ab-trace'),

    [ValidateRange(1, 64)]
    [int]$EvaluateAhead = 4,

    [ValidatePattern('^(all|off|[1-9][0-9]*)$')]
    [string]$EvaluateAheadSolution = 'all'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')
. (Join-Path $PSScriptRoot 'BuildHostForkCommon.ps1')

$definitions = @{
    'fork' = @{ referencesFirst = $false; evaluateAhead = 0; trace = $false }
    'fork-a' = @{ referencesFirst = $true; evaluateAhead = 0; trace = $false }
    'fork-b' = @{ referencesFirst = $false; evaluateAhead = $EvaluateAhead; trace = $false }
    'fork-ab' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; trace = $false }
    'fork-ab-trace' = @{ referencesFirst = $true; evaluateAhead = $EvaluateAhead; trace = $true }
}
$Variants = @($Variants | Select-Object -Unique)

# PowerShell's location can differ from the process directory used by .NET and the child host.
$Solution = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Solution)
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) { throw "Solution not found: $Solution" }
$pinned = Assert-PinnedSdk (Split-Path $Solution)
if ($pinned) { Write-Host "Solution pins .NET SDK ${pinned}: installed." }

$copies = Initialize-ForkCopy $repo $PSScriptRoot
$originalDll = Join-Path $copies.originalBin 'BuildHostFork.dll'
$forkDll = Join-Path $copies.forkBin 'BuildHostFork.dll'

if (-not $SkipRestore) {
    Write-Host "Restoring $Solution"
    Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed'
}

if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('matrix-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDirectory)
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
foreach ($variant in $Variants) {
    # A reused -OutDirectory still holds the runs and trace of an earlier matrix; the comparison would read them too.
    $variantDirectory = Join-Path $OutDirectory $variant
    if (Test-Path -LiteralPath $variantDirectory) { Remove-Item -LiteralPath $variantDirectory -Recurse -Force }
    New-Item -ItemType Directory -Path $variantDirectory | Out-Null
}
$environment = Get-Environment $Solution $repo ([ordered]@{ name = $Name; runs = $Runs; fork = $copies.version; variants = ($Variants -join ', ')
        evaluateAhead = $EvaluateAhead; evaluateAheadSolution = $EvaluateAheadSolution
    })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

$sides = @('original') + $Variants
for ($round = 0; $round -le $Runs; $round++) {
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
            Set-ForkOptimizations $definition.referencesFirst $definition.evaluateAhead $EvaluateAheadSolution
            if ($definition.trace) { $env:ROSLYN_BUILDHOST_TRACE_DIRECTORY = Join-Path $directory (Join-Path 'trace' "fork-round$round") }
        }

        $prefix = if ($fork) { 'fork' } else { 'original' }
        $output = Join-Path $directory "$prefix-round$round.json"
        try { $exitCode = Invoke-Spike $(if ($fork) { $forkDll } else { $originalDll }) @('observe', $Solution, $output, $Name) (Join-Path $directory "$prefix-round$round.out.txt") $fork }
        finally { Remove-Item Env:ROSLYN_BUILDHOST_TRACE_DIRECTORY -ErrorAction SilentlyContinue }
        if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw "$label failed (exit code $exitCode); see $output" }
    }
}

Write-Host ''
& dotnet $originalDll matrix $OutDirectory @Variants
$equivalent = $LASTEXITCODE -eq 0
foreach ($variant in $Variants | Where-Object { $definitions[$_].trace }) {
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
Write-Host 'matrix.md and <variant>\summary.md are enough for the analysis; fork-ab-trace\trace-summary.md shows where the time goes. The JSON files and the trace folders hold project paths of the solution.'
if (-not $equivalent) { throw 'A fork variant is not shown equivalent to the original; see matrix.md.' }
