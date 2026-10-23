<#
.SYNOPSIS
    BuildHost/task-00a: opens a solution like the server does, keeps the binlog of the design-time builds,
    and writes a timeline of where the load time goes.

.DESCRIPTION
    Three steps in fresh processes, all into one result folder:
    1. capture  - MSBuildWorkspace.OpenSolutionAsync with a binary logger (load*.binlog, capture.json);
    2. evaluate - every C# project evaluated again in a separate process (evaluation.md), an estimate
                  of the evaluations BuildHost runs without its loggers;
    3. analyze  - builds, evaluations and the gaps between them per binlog (timeline.md).

    The binlogs hold full paths and project contents of the solution: hand them over only where that is
    allowed. timeline.md and evaluation.md use paths relative to -Root.

.PARAMETER Solution
    Solution to load, for example Roslyn.slnx of the task stand or the office solution.

.PARAMETER Root
    Folder that paths in the reports are made relative to. Default: the Git root of the solution, or its folder.

.PARAMETER OutDirectory
    Result folder. Default: results\timeline-<time> next to this script (not tracked by Git).

.EXAMPLE
    .\spikes\LoadTimeline\Invoke-LoadTimeline.ps1 -Solution D:\src\roslyn\Roslyn.slnx
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Solution,

    [string]$Root,

    [string]$OutDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')

$Solution = [IO.Path]::GetFullPath($Solution)
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) { throw "Solution not found: $Solution" }
if (-not $Root) {
    $Root = Split-Path $Solution
    $topLevel = Get-GitOutput $Root @('rev-parse', '--show-toplevel')
    if ($topLevel) { $Root = [IO.Path]::GetFullPath($topLevel) }
}
if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('timeline-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null

$pinned = Assert-PinnedSdk (Split-Path $Solution)
if ($pinned) { Write-Host "Solution pins .NET SDK ${pinned}: installed." }
$project = Join-Path $PSScriptRoot 'LoadTimeline.csproj'
$dll = Join-Path $PSScriptRoot (Join-Path 'bin' (Join-Path 'Release' (Join-Path 'net10.0' 'LoadTimeline.dll')))
Write-Host 'Building the spike (Release)'
Invoke-Native 'dotnet' @('build', $project, '-c', 'Release', '-nologo', '-v', 'q') 'Spike build failed'
Write-Host "Restoring $Solution"
Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed'

# A warmup load first: the measured capture should not pay for cold SDK resolution and disk caches.
Write-Host 'Warmup load'
Invoke-Native 'dotnet' @($dll, 'capture', $Solution, (Join-Path $OutDirectory 'warmup')) 'Warmup capture failed'
Remove-Item -LiteralPath (Join-Path $OutDirectory 'warmup') -Recurse -Force
Write-Host 'Measured load with binlog'
Invoke-Native 'dotnet' @($dll, 'capture', $Solution, $OutDirectory) 'Capture failed'
Write-Host 'Evaluation probe'
Invoke-Native 'dotnet' @($dll, 'evaluate', $Solution, $OutDirectory) 'Evaluation probe failed'
Write-Host 'Timeline'
Invoke-Native 'dotnet' @($dll, 'analyze', $OutDirectory, $Root) 'Analysis failed'

Write-Host ''
Get-Content -LiteralPath (Join-Path $OutDirectory 'timeline.md') | Select-Object -First 14 | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'timeline.md, evaluation.md and capture.json are enough for the analysis; the binlogs hold full paths.'
