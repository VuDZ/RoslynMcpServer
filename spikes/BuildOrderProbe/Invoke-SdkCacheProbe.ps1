<#
.SYNOPSIS
    BuildHost/task-04: how much of the host's evaluations is SDK resolution, and what a shared SDK cache saves.

.DESCRIPTION
    Every run loads the solution in a fresh process the way the BuildHost serves it with references first: root
    evaluation, design-time builds, TargetFramework switches of multi-targeting projects and the restore after them,
    then GetAllGlobs, whose first call re-evaluates the project. Each kind of step is timed apart. Three modes give
    those evaluations different evaluation contexts:
      isolated - none, a context of its own per evaluation, as the 5.9.0 BuildHost;
      project  - one SharedSDKCache context per project;
      solution - one SharedSDKCache context for the whole run.
    SharedSDKCache shares SDK resolution results and resolver state (the workload manifests), not the file system
    cache. Round 0 is warmup, the order of the modes rotates between rounds. Then one attribution run per mode listens
    to MSBuild's events and adds up the time spent in SDK resolution per kind of step and per resolver; those runs are
    slower and are not timed against the others.

    summary.md: medians per kind of step against isolated, whether every run produced the same fingerprints of root
    evaluations, restored evaluations, builds and globs, and the attribution. The script fails when a fingerprint
    differs. Results go to results\sdk-cache-<time>; summary.md and environment.json have no paths, the run JSON
    files and mismatches-private.json hold full project paths.

.PARAMETER Solution
    Solution to probe, for example Roslyn.slnx.

.PARAMETER Rounds
    Measured rounds after the warmup round. Default 3.

.PARAMETER SkipRestore
    Do not restore the solution first, for a solution that is already restored.

.EXAMPLE
    pwsh spikes\BuildOrderProbe\Invoke-SdkCacheProbe.ps1 -Solution D:\src\roslyn\Roslyn.slnx
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Solution,

    [int]$Rounds = 3,

    [string]$OutDirectory,

    [switch]$SkipRestore
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')

$Solution = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Solution)
if (-not (Test-Path -LiteralPath $Solution)) { throw "Solution not found: $Solution" }
if ($OutDirectory) {
    $OutDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDirectory)
}
else {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('sdk-cache-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
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

$environment = Get-Environment $Solution $repo ([ordered]@{ rounds = $Rounds })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

$modes = @('isolated', 'project', 'solution')
for ($round = 0; $round -le $Rounds; $round++) {
    # Rotate the modes, so none of them always runs first with the coldest disk cache.
    $shift = $round % $modes.Count
    $roundModes = @($modes[$shift..($modes.Count - 1)]) + @($modes | Select-Object -First $shift)
    foreach ($mode in $roundModes) {
        $label = "$mode round $round"
        if ($round -eq 0) { $label += ' (warmup)' }
        Write-Host $label
        Invoke-Native 'dotnet' @($dll, 'sdk-cache', $Solution, (Join-Path $OutDirectory "sdk-cache-$mode-round$round.json"), $mode) "$label failed"
    }
}

foreach ($mode in $modes) {
    Write-Host "$mode attribution"
    Invoke-Native 'dotnet' @($dll, 'sdk-cache', $Solution, (Join-Path $OutDirectory "sdk-cache-$mode-events.json"), $mode, 'events') "$mode attribution failed"
}

Write-Host ''
Invoke-Native 'dotnet' @($dll, 'sdk-cache-compare', $OutDirectory) 'Fingerprints differ between modes or the summary failed; see summary.md and mismatches-private.json'

Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'summary.md and environment.json have no paths; the run JSON files and mismatches-private.json hold project paths.'
