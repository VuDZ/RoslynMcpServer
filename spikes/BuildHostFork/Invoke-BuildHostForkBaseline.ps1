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

$Solution = [IO.Path]::GetFullPath($Solution)
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) { throw "Solution not found: $Solution" }
$pinned = Assert-PinnedSdk (Split-Path $Solution)
if ($pinned) { Write-Host "Solution pins .NET SDK ${pinned}: installed." }

$forkProject = Join-Path $repo (Join-Path 'forks' (Join-Path 'roslyn-buildhost' (Join-Path 'build' (Join-Path 'BuildHost' 'BuildHost.Fork.csproj'))))
$forkOutput = Join-Path (Split-Path $forkProject) (Join-Path 'bin' (Join-Path 'Release' 'net8.0'))
$spikeProject = Join-Path $PSScriptRoot 'BuildHostFork.csproj'
$originalBin = Join-Path $PSScriptRoot (Join-Path 'bin' (Join-Path 'Release' 'net10.0'))
$forkBin = Join-Path $PSScriptRoot 'bin-fork'

Write-Host 'Building the BuildHost fork (Release)'
Invoke-Native 'dotnet' @('build', $forkProject, '-c', 'Release', '-nologo', '-v', 'q') 'Fork build failed'
Write-Host 'Building the spike (Release)'
Invoke-Native 'dotnet' @('build', $spikeProject, '-c', 'Release', '-nologo', '-v', 'q') 'Spike build failed'

# The fork copy: the same spike and client, only BuildHost-netcore replaced.
if (Test-Path -LiteralPath $forkBin) { Remove-Item -LiteralPath $forkBin -Recurse -Force }
Copy-Item -LiteralPath $originalBin -Destination $forkBin -Recurse
$forkHostFolder = Join-Path $forkBin 'BuildHost-netcore'
Remove-Item -LiteralPath $forkHostFolder -Recurse -Force
New-Item -ItemType Directory -Path $forkHostFolder | Out-Null
Get-ChildItem -LiteralPath $forkOutput -File | Copy-Item -Destination $forkHostFolder
$version = (Get-Item -LiteralPath (Join-Path $forkHostFolder 'Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll')).VersionInfo.ProductVersion
if ($version -notlike '*fork*') { throw "The fork copy does not hold the fork BuildHost: $version" }
Write-Host "Fork BuildHost: $version"

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

$sides = [ordered]@{ original = (Join-Path $originalBin 'BuildHostFork.dll'); fork = (Join-Path $forkBin 'BuildHostFork.dll') }
for ($round = 0; $round -le $Runs; $round++) {
    $order = @('original', 'fork')
    if ($round % 2 -eq 1) { $order = @('fork', 'original') }
    foreach ($side in $order) {
        $label = "$side round $round"
        if ($round -eq 0) { $label += ' (warmup)' }
        Write-Host $label
        $output = Join-Path $OutDirectory "$side-round$round.json"
        & dotnet $sides[$side] observe $Solution $output $Name 2>&1 | Out-File -LiteralPath (Join-Path $OutDirectory "$side-round$round.out.txt") -Encoding utf8
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw "$label failed (exit code $LASTEXITCODE); see $side-round$round.out.txt" }
    }
}

Write-Host ''
& dotnet $sides['original'] compare $OutDirectory
Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'summary.md is enough for the analysis; the JSON files hold project paths and diagnostics of the solution.'
