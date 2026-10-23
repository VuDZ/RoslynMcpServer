<#
.SYNOPSIS
    Runs the D/E0/task-14 matrix: first declaration search without Roslyn's persistent storage, with a
    cold SQLite cache and with a warm one, each measurement in a fresh process, and prints the verdict.

.DESCRIPTION
    'none' runs the ParallelSearch spike, which has no SQLite library, so Roslyn rebuilds every index.
    'cold' and 'warm' run the IndexStorage spike: the same code plus SQLite3MC.PCLRaw.bundle 2.3.5, the
    build Roslyn 5.9.0 references. Before a cold run the script deletes this solution's cache, written
    by dotnet-hosted processes, under %LOCALAPPDATA%\Microsoft\VisualStudio\Roslyn\Cache (on Linux,
    ~/.local/share/...); the warm run that follows reuses what the cold run wrote. Visual Studio caches
    live under other process names and are not touched.

    Each round runs cold, then warm, and 'none' first or last in turn. Round 0 is warmup.

    Thresholds were fixed in the task before any measurement and must not be adjusted here:
    go when, on both queries, the median warm first search is at most 0.70 of the median cold one,
    the median cold first search is at most 1.10 of 'none', and every run of a query returned the same answer.

.PARAMETER Solution
    Solution to measure. The task's stand is Roslyn.slnx at commit 013d3a758df6c137497ff37a93f0d4bed103853a.

.PARAMETER Runs
    Measured rounds per query; one warmup round is added. Default 5.

.PARAMETER SkipRestore
    Do not run dotnet restore on the solution first.

.PARAMETER OutDirectory
    Result folder. Default: results\storage-<time> next to this script, meant to be handed over for
    analysis and removed afterwards; project paths in it are relative to the solution.

.PARAMETER SummarizeOnly
    Folder of an earlier run: only rebuild summary.md and summary.json from its JSON files.

.EXAMPLE
    .\spikes\IndexStorage\Invoke-IndexStorageMatrix.ps1 -Solution D:\src\roslyn\Roslyn.slnx
#>
[CmdletBinding()]
param(
    [string]$Solution,

    [string]$MatchName = 'CSharpCompilationOptions',

    [string]$MissingName = 'NoSuchDeclarationName7d3f2a',

    [ValidateRange(1, 50)]
    [int]$Runs = 5,

    [switch]$SkipRestore,

    [string]$OutDirectory,

    [string]$SummarizeOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$spikes = Split-Path $PSScriptRoot -Parent
$baselineProject = Join-Path (Join-Path $spikes 'ParallelSearch') 'ParallelSearch.csproj'
$baselineDll = Join-Path (Join-Path $spikes 'ParallelSearch') (Join-Path 'bin' (Join-Path 'Release' (Join-Path 'net10.0' 'ParallelSearch.dll')))
$storageProject = Join-Path $PSScriptRoot 'IndexStorage.csproj'
$storageDll = Join-Path $PSScriptRoot (Join-Path 'bin' (Join-Path 'Release' (Join-Path 'net10.0' 'IndexStorage.dll')))
$utf8NoBom = New-Object Text.UTF8Encoding($false)

. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')

# Roslyn names the folder after the host process and a hash: dotnet-<hash>\<solution file>-<hash>.
function Get-SolutionCacheFolders([string]$SolutionPath) {
    $root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) (Join-Path 'Microsoft' (Join-Path 'VisualStudio' (Join-Path 'Roslyn' 'Cache')))
    if (-not (Test-Path -LiteralPath $root)) { return @() }
    $leaf = Split-Path $SolutionPath -Leaf
    return @(Get-ChildItem -LiteralPath $root -Directory -Filter 'dotnet-*' | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Directory | Where-Object { $_.Name.StartsWith($leaf + '-', [StringComparison]::OrdinalIgnoreCase) }
    })
}

function Get-CacheSizeMb([string]$SolutionPath) {
    $bytes = 0L
    foreach ($folder in Get-SolutionCacheFolders $SolutionPath) {
        $bytes += (Get-ChildItem -LiteralPath $folder.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum
    }
    return $bytes / 1MB
}

function Invoke-Matrix([string]$SolutionPath, [string]$Out) {
    $queries = [ordered]@{ match = $MatchName; missing = $MissingName }
    $total = $queries.Count * ($Runs + 1) * 3
    $done = 0
    foreach ($query in $queries.Keys) {
        for ($round = 0; $round -le $Runs; $round++) {
            # Cold must come right before warm; 'none' alternates between first and last.
            $order = @('none', 'cold', 'warm')
            if ($round % 2 -eq 1) { $order = @('cold', 'warm', 'none') }
            foreach ($mode in $order) {
                $done++
                $stem = "{0}-{1}-round{2}" -f $query, $mode, $round
                $file = Join-Path $Out "$stem.json"
                # Not *.log: these files travel to the analysis machine, where *.log is ignored.
                $output = Join-Path $Out "$stem.out.txt"
                $label = "[$done/$total] $query $mode round=$round"
                if ($round -eq 0) { $label += ' (warmup)' }
                Write-Host $label
                $dll = $storageDll
                if ($mode -eq 'none') { $dll = $baselineDll }
                if ($mode -eq 'cold') {
                    foreach ($folder in Get-SolutionCacheFolders $SolutionPath) { Remove-Item -LiteralPath $folder.FullName -Recurse -Force }
                }
                & dotnet $dll measure --solution $SolutionPath --name $queries[$query] --dop 1 --output $file 2>&1 |
                    Out-File -LiteralPath $output -Encoding utf8
                if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $file)) {
                    Write-Warning "$label failed (exit code $LASTEXITCODE); see $output"
                    continue
                }
                # The cache size is a fact about this run; keep it next to the spike's own JSON.
                $cacheMb = $null
                if ($mode -ne 'none') { $cacheMb = Get-CacheSizeMb $SolutionPath }
                [IO.File]::WriteAllText((Join-Path $Out "$stem.cache.json"), (@{ cacheMb = $cacheMb } | ConvertTo-Json), $utf8NoBom)
                $run = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
                Write-Host ("    load {0} s, first search {1} ms, second {2} ms, cache {3} MB" -f
                    (Format-Number ($run.load.loadMs / 1000) 1), (Format-Number $run.firstSearch.wallMs),
                    (Format-Number $run.secondSearch.wallMs), (Format-Number $cacheMb 1))
            }
        }
    }
}

function Write-Summary([string]$Out) {
    $environmentFile = Join-Path $Out 'environment.json'
    $environment = $null
    if (Test-Path -LiteralPath $environmentFile) { $environment = Get-Content -LiteralPath $environmentFile -Raw | ConvertFrom-Json }
    $runs = @(Get-ChildItem -LiteralPath $Out -Filter '*-round*.json' | Where-Object { $_.Name -notlike '*.cache.json' } | ForEach-Object {
        $match = [regex]::Match($_.Name, '^(?<q>[a-z]+)-(?<m>none|cold|warm)-round(?<r>\d+)\.json$')
        if (-not $match.Success) { return }
        $cacheFile = $_.FullName -replace '\.json$', '.cache.json'
        $cache = $null
        if (Test-Path -LiteralPath $cacheFile) { $cache = (Get-Content -LiteralPath $cacheFile -Raw | ConvertFrom-Json).cacheMb }
        [pscustomobject]@{ Query = $match.Groups['q'].Value; Mode = $match.Groups['m'].Value; Round = [int]$match.Groups['r'].Value; Data = (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json); CacheMb = $cache }
    })
    $failedLogs = @(Get-ChildItem -LiteralPath $Out -Filter '*.out.txt' | Where-Object { -not (Test-Path -LiteralPath ($_.FullName -replace '\.out\.txt$', '.json')) })

    $rows = New-Object Collections.Generic.List[object]
    $verdicts = [ordered]@{}
    foreach ($query in @($runs | Select-Object -ExpandProperty Query -Unique)) {
        $all = @($runs | Where-Object { $_.Query -eq $query })
        $hashes = @($all | ForEach-Object { $_.Data.firstSearch.resultSha256; $_.Data.secondSearch.resultSha256 } | Select-Object -Unique)
        $medians = @{}
        foreach ($mode in 'none', 'cold', 'warm') {
            $set = @($all | Where-Object { $_.Round -gt 0 -and $_.Mode -eq $mode })
            $row = [pscustomobject][ordered]@{
                query = $query; mode = $mode; runs = $set.Count
                firstWallMs = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.wallMs })
                firstCpuMs = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.cpuMs })
                searchPeakWorkingSetMb = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.peakWorkingSetMb })
                secondWallMs = Get-Median @($set | ForEach-Object { $_.Data.secondSearch.wallMs })
                loadMs = Get-Median @($set | ForEach-Object { $_.Data.load.loadMs })
                cacheMb = Get-Median @($set | ForEach-Object { $_.CacheMb })
                declarations = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.declarations })
            }
            $medians[$mode] = $row.firstWallMs
            $rows.Add($row)
        }
        $warmToCold = $null
        $coldToNone = $null
        if ($medians['cold']) { $warmToCold = $medians['warm'] / $medians['cold'] }
        if ($medians['none']) { $coldToNone = $medians['cold'] / $medians['none'] }
        $within = $null -ne $warmToCold -and $null -ne $coldToNone -and $warmToCold -le 0.70 -and $coldToNone -le 1.10
        $verdicts[$query] = [ordered]@{ identicalAnswers = ($hashes.Count -eq 1); distinctAnswers = $hashes.Count; warmToCold = $warmToCold; coldToNone = $coldToNone; withinThresholds = $within }
    }

    $complete = $verdicts.Count -eq 2 -and $failedLogs.Count -eq 0
    $verdict = 'inconclusive'
    if ($complete) {
        $verdict = 'go'
        foreach ($v in $verdicts.Values) { if (-not $v.identicalAnswers -or -not $v.withinThresholds) { $verdict = 'no-go' } }
    }

    $summary = [ordered]@{ verdict = $verdict; environment = $environment; queries = $verdicts; failedRuns = @($failedLogs | ForEach-Object Name); rows = $rows }
    [IO.File]::WriteAllText((Join-Path $Out 'summary.json'), ($summary | ConvertTo-Json -Depth 6), $utf8NoBom)

    $md = New-Object Collections.Generic.List[string]
    $md.Add('# Roslyn index storage matrix (D/E0/task-14)')
    $md.Add('')
    if ($null -ne $environment) {
        foreach ($p in $environment.PSObject.Properties) { $md.Add("- $($p.Name): $($p.Value)") }
        $md.Add('')
    }
    $md.Add("Verdict: **$verdict**. Thresholds fixed before measuring, on both queries: median warm first search <= 0.70 x cold, median cold first search <= 1.10 x none; every answer identical.")
    $md.Add('')
    foreach ($key in $verdicts.Keys) {
        $v = $verdicts[$key]
        $md.Add("- ${key}: identical answers $($v.identicalAnswers) ($($v.distinctAnswers) distinct); warm/cold $(Format-Number $v.warmToCold 3); cold/none $(Format-Number $v.coldToNone 3); within thresholds $($v.withinThresholds)")
    }
    if ($failedLogs.Count -gt 0) { $md.Add("- failed runs: $(@($failedLogs | ForEach-Object Name) -join ', ')") }
    $md.Add('')
    $md.Add('Medians over measured rounds (warmup excluded). Cache size is this solution''s Roslyn cache folder after the run.')
    $md.Add('')
    $md.Add('| query | mode | runs | first search ms | CPU ms | search peak WS MB | second search ms | load s | cache MB | declarations |')
    $md.Add('|---|---|---|---|---|---|---|---|---|---|')
    foreach ($r in $rows) {
        $md.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |' -f $r.query, $r.mode, $r.runs, (Format-Number $r.firstWallMs),
            (Format-Number $r.firstCpuMs), (Format-Number $r.searchPeakWorkingSetMb), (Format-Number $r.secondWallMs),
            (Format-Number ($r.loadMs / 1000) 1), (Format-Number $r.cacheMb 1), $r.declarations))
    }
    [IO.File]::WriteAllLines((Join-Path $Out 'summary.md'), $md, $utf8NoBom)
    $md | ForEach-Object { Write-Host $_ }
}

if ($SummarizeOnly) {
    Write-Summary ([IO.Path]::GetFullPath($SummarizeOnly))
    return
}

if (-not $Solution) { throw 'Pass -Solution, or -SummarizeOnly with an earlier result folder.' }
$Solution = [IO.Path]::GetFullPath($Solution)
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) { throw "Solution not found: $Solution" }

Initialize-Stand $baselineProject $baselineDll $Solution $SkipRestore.IsPresent
Write-Host 'Building the storage spike (Release)'
Invoke-Native 'dotnet' @('build', $storageProject, '-c', 'Release', '-nologo', '-v', 'q') 'Storage spike build failed'

if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('storage-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$environment = Get-Environment $Solution $repo ([ordered]@{ matchName = $MatchName; missingName = $MissingName; runs = $Runs; sqlite = 'SQLite3MC.PCLRaw.bundle 2.3.5' })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), $utf8NoBom)

Invoke-Matrix $Solution $OutDirectory
Write-Host ''
Write-Summary $OutDirectory
Write-Host ''
Write-Host "Results: $OutDirectory"
Write-Host 'Pack this folder and send it for analysis; it holds no absolute paths of the stand.'
