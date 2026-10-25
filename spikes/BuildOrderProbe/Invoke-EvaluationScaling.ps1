<#
.SYNOPSIS
    BuildHost/task-02: do root evaluations of one solution run in parallel in one process, with no builds beside them?

.DESCRIPTION
    One references-first run of the probe lists the projects the BuildHost would evaluate, in its order. Then every
    variant evaluates that list in a fresh process on dedicated worker threads, with no build manager, the way the
    BuildHost evaluates: one worker in one collection (the baseline), and for each larger worker count of -Workers
    one shared project collection, one collection per worker, and for comparison one shared collection through
    ProjectCollection.LoadProject, which holds the collection's lock for the whole evaluation. Round 0 is warmup, and the order of the variants rotates between rounds.
    summary.md compares the variants: speedup against one worker, evaluations at once, and the time-weighted slowdown
    of the evaluations. A slowdown that grows with the evaluations at once means they wait for each other; if that
    goes away with a collection per worker, what they wait for belongs to the project collection.

    With -Trace, one more run of the shared collection with the most workers goes under dotnet-trace (stack samples
    of every managed thread, waiting ones too, and lock contention events), written as trace-shared-w<N>.nettrace and
    .speedscope.json. It needs the dotnet-trace global tool: dotnet tool install --global dotnet-trace.

    Results go to results\scaling-<time>. summary.md has no paths; paths-private.txt and the run JSON files hold full
    project paths.

.PARAMETER Solution
    Solution to probe, for example Roslyn.slnx.

.PARAMETER Rounds
    Measured rounds after the warmup round. Default 3.

.PARAMETER Workers
    Worker counts above one. Default: 2 and 4.

.PARAMETER Trace
    Also record one traced run; see the description.

.PARAMETER SkipRestore
    Do not restore the solution first, for a solution that is already restored.

.EXAMPLE
    pwsh spikes\BuildOrderProbe\Invoke-EvaluationScaling.ps1 -Solution D:\src\roslyn\Roslyn.slnx -Trace
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Solution,

    [int]$Rounds = 3,

    [int[]]$Workers = @(2, 4),

    [switch]$Trace,

    [string]$OutDirectory,

    [switch]$SkipRestore
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')

$Solution = (Resolve-Path -LiteralPath $Solution).Path
if ($Trace -and -not (Get-Command 'dotnet-trace' -ErrorAction SilentlyContinue)) {
    throw 'dotnet-trace is not installed: dotnet tool install --global dotnet-trace'
}

if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('scaling-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null

$pinned = Assert-PinnedSdk (Split-Path $Solution)
if ($pinned) { Write-Host "Solution pins .NET SDK ${pinned}: installed." }
$project = Join-Path $PSScriptRoot 'BuildOrderProbe.csproj'
$dll = Join-Path $PSScriptRoot (Join-Path 'bin' (Join-Path 'Release' (Join-Path 'net10.0' 'BuildOrderProbe.dll')))
Write-Host 'Building the probe (Release)'
Invoke-Native 'dotnet' @('build', $project, '-c', 'Release', '-nologo', '-v', 'q') 'Probe build failed'
if (-not $SkipRestore) {
    Write-Host "Restoring $Solution"
    Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed'
}

$workerCounts = @($Workers | Where-Object { $_ -ge 2 } | Sort-Object -Unique)
$environment = Get-Environment $Solution $repo ([ordered]@{ rounds = $Rounds; workers = ($workerCounts -join ', '); trace = [bool]$Trace })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

# The projects in the order the references-first run evaluated them; a project MSBuild cannot load has thread -1.
Write-Host 'Listing the projects (references-first run)'
$listing = Join-Path $OutDirectory 'listing-references-first.json'
Invoke-Native 'dotnet' @($dll, 'run', 'references-first', $Solution, $listing, '1') 'Listing run failed'
$paths = Join-Path $OutDirectory 'paths-private.txt'
$steps = (Get-Content -LiteralPath $listing -Raw | ConvertFrom-Json).EvaluationSteps | Where-Object { $_.Thread -ge 0 } | Sort-Object StartMs
[IO.File]::WriteAllLines($paths, [string[]]@($steps | ForEach-Object { $_.Path }), (New-Object Text.UTF8Encoding($false)))

$variants = New-Object System.Collections.Generic.List[object]
$variants.Add([pscustomobject]@{ Name = 'shared-w1'; Collections = 'shared'; Workers = 1 })
foreach ($count in $workerCounts) {
    $variants.Add([pscustomobject]@{ Name = "shared-w$count"; Collections = 'shared'; Workers = $count })
    $variants.Add([pscustomobject]@{ Name = "separate-w$count"; Collections = 'separate'; Workers = $count })
    $variants.Add([pscustomobject]@{ Name = "load-project-w$count"; Collections = 'load-project'; Workers = $count })
}
Write-Host "Variants: $(($variants | ForEach-Object { $_.Name }) -join ', '); $($Rounds + 1) rounds"

for ($round = 0; $round -le $Rounds; $round++) {
    # Rotate the variants, so none of them always runs first with the coldest disk cache.
    $shift = $round % $variants.Count
    $roundVariants = @($variants[$shift..($variants.Count - 1)]) + @($variants | Select-Object -First $shift)
    foreach ($variant in $roundVariants) {
        $label = "$($variant.Name) round $round"
        if ($round -eq 0) { $label += ' (warmup)' }
        Write-Host $label
        $output = Join-Path $OutDirectory "evaluate-$($variant.Name)-round$round.json"
        Invoke-Native 'dotnet' @($dll, 'evaluate', $Solution, $paths, $output, [string]$variant.Workers, $variant.Collections) "$label failed"
    }
}

Write-Host ''
Invoke-Native 'dotnet' @($dll, 'scaling-compare', $OutDirectory) 'Summary failed'

if ($Trace) {
    $most = if ($workerCounts.Count -gt 0) { $workerCounts[-1] } else { 1 }
    $traceFile = Join-Path $OutDirectory "trace-shared-w$most.nettrace"
    Write-Host "Traced run: shared-w$most"
    # Stack samples of every managed thread, waiting ones too, and the runtime's lock contention events (keyword
    # 0x4000). dotnet-trace 10 names the sampling profile dotnet-sampled-thread-time; older versions call it
    # cpu-sampling, which already includes contention.
    $profiles = (& dotnet-trace list-profiles) -join "`n"
    $profile = if ($profiles -match 'dotnet-sampled-thread-time') {
        @('--profile', 'dotnet-sampled-thread-time', '--providers', 'Microsoft-Windows-DotNETRuntime:0x4000:4')
    }
    else {
        @('--profile', 'cpu-sampling')
    }
    Invoke-Native 'dotnet-trace' (@('collect') + $profile + @('--format', 'Speedscope', '-o', $traceFile, '--',
        'dotnet', $dll, 'evaluate', $Solution, $paths, (Join-Path $OutDirectory "traced-shared-w$most.json"), [string]$most, 'shared')) 'Traced run failed'
}

Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'summary.md has no paths; paths-private.txt, the run JSON files and the trace files hold project paths and are needed for the analysis.'
