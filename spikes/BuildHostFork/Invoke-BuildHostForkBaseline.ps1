<#
.SYNOPSIS
    BuildHost/task-00: loads a solution with the original BuildHost of Roslyn 5.9.0 and with the unchanged
    fork from forks/roslyn-buildhost, in alternating fresh processes, and compares the workspaces.

.DESCRIPTION
    The spike is built once. A copy of its output (bin-fork) gets the fork in BuildHost-netcore; the
    MSBuildWorkspace client in both copies is the same package build and starts whichever BuildHost lies
    next to it. Each round runs both sides, alternating which goes first. Round 0 is warmup.

    summary.md: equivalence of every project field and workspace diagnostic (natural variance taken from
    the original runs), identical first-search answers, and medians of load, first search, client CPU and
    working set. The fork must be equivalent before any optimization is measured on it.

.PARAMETER Solution
    Solution to load.

.PARAMETER Name
    Name for the first search. Default: a name no declaration has.

.PARAMETER Runs
    Measured rounds; one warmup round is added. Default 3.

.PARAMETER OutDirectory
    Result folder. Default: results\baseline-<time> next to this script (not tracked by Git).

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

    [string]$OutDirectory
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

if (-not $SkipRestore) {
    Write-Host "Restoring $Solution"
    Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed'
}

if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('baseline-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$environment = Get-Environment $Solution $repo ([ordered]@{ name = $Name; runs = $Runs; fork = $version })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

$sides = [ordered]@{ original = (Join-Path $copies.originalBin 'BuildHostFork.dll'); fork = (Join-Path $copies.forkBin 'BuildHostFork.dll') }
Invoke-ObserveRounds $sides $Solution $Name $Runs $OutDirectory

Write-Host ''
& dotnet $sides['original'] compare $OutDirectory
Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'summary.md is enough for the analysis; the JSON files hold project paths and diagnostics of the solution.'
