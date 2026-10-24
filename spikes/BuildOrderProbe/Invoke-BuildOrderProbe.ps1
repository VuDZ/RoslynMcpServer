<#
.SYNOPSIS
    BuildHost/task-02, step 0: builds a solution's projects the way the BuildHost does, in three orders, and
    checks that the orders build the same results, how long each takes, and how the evaluations lay on the clock.

.DESCRIPTION
    client: the 5.9.0 loader's order, a project before the projects it references. references-first: the
    references before the project (part A). evaluate-ahead: references first and the references' root evaluations
    on worker threads while builds go on one at a time (part B), once per worker count of -Workers. With
    -ServerGc, references-first and evaluate-ahead with the most workers also run with the server garbage
    collector. Every run is a fresh process; round 0 is warmup, and the order of the variants rotates between
    rounds. Results go to results\probe-<time>; summary.md has no paths, the run JSON files hold full project paths.

.PARAMETER Solution
    Solution to probe, for example Roslyn.slnx or the office solution.

.PARAMETER Rounds
    Measured rounds after the warmup round. Default 3.

.PARAMETER Workers
    Worker counts of evaluate-ahead. Default: 2, 4 and logical processors minus one.

.PARAMETER ServerGc
    Also run references-first and evaluate-ahead with the most workers under the server garbage collector.

.PARAMETER SkipRestore
    Do not restore the solution first, for a solution that is already restored.

.EXAMPLE
    pwsh spikes\BuildOrderProbe\Invoke-BuildOrderProbe.ps1 -Solution D:\src\roslyn\Roslyn.slnx -ServerGc
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Solution,

    [int]$Rounds = 3,

    [int[]]$Workers = @(2, 4, [Math]::Max(1, [Environment]::ProcessorCount - 1)),

    [switch]$ServerGc,

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

$workerCounts = @($Workers | Where-Object { $_ -ge 1 } | Sort-Object -Unique)
$environment = Get-Environment $Solution $repo ([ordered]@{ rounds = $Rounds; workers = ($workerCounts -join ', '); serverGc = [bool]$ServerGc })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

# A variant is an order, its worker count and the garbage collector mode; its name is the file name prefix.
$variants = New-Object System.Collections.Generic.List[object]
$variants.Add([pscustomobject]@{ Name = 'client'; Order = 'client'; Workers = 1; ServerGc = $false })
$variants.Add([pscustomobject]@{ Name = 'references-first'; Order = 'references-first'; Workers = 1; ServerGc = $false })
foreach ($count in $workerCounts) {
    $variants.Add([pscustomobject]@{ Name = "evaluate-ahead-w$count"; Order = 'evaluate-ahead'; Workers = $count; ServerGc = $false })
}
if ($ServerGc) {
    $most = $workerCounts[-1]
    $variants.Add([pscustomobject]@{ Name = 'references-first-servergc'; Order = 'references-first'; Workers = 1; ServerGc = $true })
    $variants.Add([pscustomobject]@{ Name = "evaluate-ahead-w$most-servergc"; Order = 'evaluate-ahead'; Workers = $most; ServerGc = $true })
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
        if ($variant.ServerGc) { $env:DOTNET_gcServer = '1' }
        try {
            Invoke-Native 'dotnet' @($dll, 'run', $variant.Order, $Solution, (Join-Path $OutDirectory "$($variant.Name)-round$round.json"), [string]$variant.Workers) "$label failed"
        }
        finally { Remove-Item Env:DOTNET_gcServer -ErrorAction SilentlyContinue }
    }
}

Write-Host ''
& dotnet $dll compare $OutDirectory
$identical = $LASTEXITCODE -eq 0
Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'summary.md is enough for the analysis; the run JSON files and mismatches-private.json hold full project paths.'
if (-not $identical) { throw 'The orders did not build the same results; see summary.md.' }
