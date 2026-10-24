<#
.SYNOPSIS
    BuildHost/task-02, step 0: builds a solution's projects the way the BuildHost does, in three orders, and
    checks that the orders build the same results and how long each takes.

.DESCRIPTION
    client: the 5.9.0 loader's order, a project before the projects it references. references-first: the
    references before the project (part A). evaluate-ahead: references first and the references' root evaluations
    on worker threads while builds go on one at a time (part B). Every run is a fresh process; round 0 is warmup,
    the order of the three alternates between rounds. Results go to results\probe-<time>; summary.md has no paths,
    the run JSON files hold full project paths.

.PARAMETER Solution
    Solution to probe, for example Roslyn.slnx or the office solution.

.PARAMETER Rounds
    Measured rounds after the warmup round. Default 3.

.PARAMETER Workers
    Worker threads of evaluate-ahead. Default: logical processors minus one.

.PARAMETER SkipRestore
    Do not restore the solution first, for a solution that is already restored.

.EXAMPLE
    pwsh spikes\BuildOrderProbe\Invoke-BuildOrderProbe.ps1 -Solution D:\src\roslyn\Roslyn.slnx
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Solution,

    [int]$Rounds = 3,

    [int]$Workers = [Math]::Max(1, [Environment]::ProcessorCount - 1),

    [string]$OutDirectory,

    [switch]$SkipRestore
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')

$Solution = (Resolve-Path -LiteralPath $Solution).Path
if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('probe-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
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

$environment = Get-Environment $Solution $repo ([ordered]@{ rounds = $Rounds; workers = $Workers })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

$orders = @('client', 'references-first', 'evaluate-ahead')
for ($round = 0; $round -le $Rounds; $round++) {
    # Rotate the order of the three, so none of them always runs first with the coldest disk cache.
    $shift = $round % $orders.Count
    $roundOrders = @($orders[$shift..($orders.Count - 1)]) + @($orders | Select-Object -First $shift)
    foreach ($order in $roundOrders) {
        $label = "$order round $round"
        if ($round -eq 0) { $label += ' (warmup)' }
        Write-Host $label
        Invoke-Native 'dotnet' @($dll, 'run', $order, $Solution, (Join-Path $OutDirectory "$order-round$round.json"), [string]$Workers) "$label failed"
    }
}

Write-Host ''
& dotnet $dll compare $OutDirectory
$identical = $LASTEXITCODE -eq 0
Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'summary.md is enough for the analysis; the run JSON files and mismatches-private.json hold full project paths.'
if (-not $identical) { throw 'The orders did not build the same results; see summary.md.' }
