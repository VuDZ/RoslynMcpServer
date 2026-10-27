<#
.SYNOPSIS
    Runs the D/E0/task-11 matrix: first name-based declaration search with 1, 2, 4 and 8 projects in
    flight, each measurement in a fresh process, and prints the go/no-go verdict.

.DESCRIPTION
    For each query (a name with a declaration and a name with none) and each round, the script starts
    one process per degree of parallelism. Round 0 is warmup and is not counted. The order of degrees
    rotates every round so no degree always runs first or last. Each process loads the solution with
    MSBuildWorkspace, searches twice and writes one JSON file.

    Thresholds were fixed in the task before any measurement and must not be adjusted here:
    go when, on both queries, the median first-search wall time at degree 2 or 4 is at most 0.70 of
    degree 1, its median search peak working set is at most 1.25 of degree 1, and every run of a query
    returned the same answer (SHA-256 of the ordered declaration list).

.PARAMETER Solution
    Solution to measure. The task's stand is Roslyn.slnx at commit 013d3a758df6c137497ff37a93f0d4bed103853a.

.PARAMETER Runs
    Measured rounds per query; one warmup round is added. Default 5.

.PARAMETER SkipRestore
    Do not run dotnet restore on the solution first.

.PARAMETER OutDirectory
    Result folder. Default: results\<time> next to this script, a folder meant to be committed so the
    results reach the analysis machine through Git; project paths in it are relative to the solution.

.PARAMETER SummarizeOnly
    Folder of an earlier run: only rebuild summary.md and summary.json from its JSON files.

.EXAMPLE
    .\spikes\ParallelSearch\Invoke-ParallelSearchMatrix.ps1 -Solution D:\src\roslyn\Roslyn.slnx
#>
[CmdletBinding()]
param(
    [string]$Solution,

    [string]$MatchName = 'CSharpCompilationOptions',

    [string]$MissingName = 'NoSuchDeclarationName7d3f2a',

    # A comma-separated string: powershell -File passes "1,2" as one string, not as an array.
    [string]$Degrees = '1,2,4,8',

    [ValidateRange(1, 50)]
    [int]$Runs = 5,

    [switch]$SkipRestore,

    [string]$OutDirectory,

    [string]$SummarizeOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$degreeList = @($Degrees -split '[,\s]+' | Where-Object { $_ } | ForEach-Object { [int]$_ })
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$spikeProject = Join-Path $PSScriptRoot 'ParallelSearch.csproj'
$spikeDll = Join-Path (Join-Path (Join-Path (Join-Path $PSScriptRoot 'bin') 'Release') 'net10.0') 'ParallelSearch.dll'
$utf8NoBom = New-Object Text.UTF8Encoding($false)

. (Join-Path $PSScriptRoot 'MatrixCommon.ps1')

function Invoke-Matrix([string]$SolutionPath, [string]$Out) {
    $queries = [ordered]@{ match = $MatchName; missing = $MissingName }
    $total = $queries.Count * ($Runs + 1) * $degreeList.Count
    $done = 0
    foreach ($query in $queries.Keys) {
        for ($round = 0; $round -le $Runs; $round++) {
            # Rotate the start so every degree runs first, last and in between across rounds.
            $order = @(for ($i = 0; $i -lt $degreeList.Count; $i++) { $degreeList[($i + $round) % $degreeList.Count] })
            foreach ($degree in $order) {
                $done++
                $file = Join-Path $Out ("{0}-dop{1}-round{2}.json" -f $query, $degree, $round)
                # Not *.log: the repository ignores that pattern, and these files travel through Git.
                $output = Join-Path $Out ("{0}-dop{1}-round{2}.out.txt" -f $query, $degree, $round)
                $label = "[$done/$total] $query dop=$degree round=$round"
                if ($round -eq 0) { $label += ' (warmup)' }
                Write-Host $label
                $watch = [Diagnostics.Stopwatch]::StartNew()
                & dotnet $spikeDll measure --solution $SolutionPath --name $queries[$query] --dop $degree --output $file 2>&1 |
                    Out-File -LiteralPath $output -Encoding utf8
                if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $file)) {
                    Write-Warning "$label failed (exit code $LASTEXITCODE); see $output"
                    continue
                }
                $run = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
                Write-Host ("    load {0} s, first search {1} ms, second {2} ms, process {3} s" -f
                    (Format-Number ($run.load.loadMs / 1000) 1), (Format-Number $run.firstSearch.wallMs),
                    (Format-Number $run.secondSearch.wallMs), (Format-Number $watch.Elapsed.TotalSeconds 1))
            }
        }
    }
}

function Write-Summary([string]$Out) {
    $environmentFile = Join-Path $Out 'environment.json'
    $environment = $null
    if (Test-Path -LiteralPath $environmentFile) { $environment = Get-Content -LiteralPath $environmentFile -Raw | ConvertFrom-Json }
    $runs = @(Get-ChildItem -LiteralPath $Out -Filter '*-dop*-round*.json' | ForEach-Object {
        $match = [regex]::Match($_.Name, '^(?<q>\w+)-dop(?<d>\d+)-round(?<r>\d+)\.json$')
        if (-not $match.Success) { return }
        $data = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        [pscustomobject]@{ Query = $match.Groups['q'].Value; Degree = [int]$match.Groups['d'].Value; Round = [int]$match.Groups['r'].Value; Data = $data }
    })
    $failedLogs = @(Get-ChildItem -LiteralPath $Out -Filter '*.out.txt' | Where-Object {
        -not (Test-Path -LiteralPath ($_.FullName -replace '\.out\.txt$', '.json'))
    })

    $rows = New-Object Collections.Generic.List[object]
    $verdicts = [ordered]@{}
    foreach ($query in @($runs | Select-Object -ExpandProperty Query -Unique)) {
        $all = @($runs | Where-Object { $_.Query -eq $query })
        $hashes = @($all | ForEach-Object { $_.Data.firstSearch.resultSha256; $_.Data.secondSearch.resultSha256 } | Select-Object -Unique)
        $measured = @($all | Where-Object { $_.Round -gt 0 })
        $baseline = @($measured | Where-Object { $_.Degree -eq 1 })
        $baseWall = Get-Median @($baseline | ForEach-Object { $_.Data.firstSearch.wallMs })
        $basePeak = Get-Median @($baseline | ForEach-Object { $_.Data.firstSearch.peakWorkingSetMb })
        $passing = @()
        foreach ($degree in @($measured | Select-Object -ExpandProperty Degree -Unique | Sort-Object)) {
            $set = @($measured | Where-Object { $_.Degree -eq $degree })
            $wall = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.wallMs })
            $peak = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.peakWorkingSetMb })
            $wallRatio = $null
            $peakRatio = $null
            if ($baseWall) { $wallRatio = $wall / $baseWall }
            if ($basePeak) { $peakRatio = $peak / $basePeak }
            if (($degree -eq 2 -or $degree -eq 4) -and $null -ne $wallRatio -and $wallRatio -le 0.70 -and $null -ne $peakRatio -and $peakRatio -le 1.25) {
                $passing += $degree
            }
            $rows.Add([pscustomobject][ordered]@{
                query = $query; degree = $degree; runs = $set.Count
                firstWallMs = $wall; wallRatio = $wallRatio
                firstCpuMs = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.cpuMs })
                busyCores = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.busyCores })
                peakWorkingSetMb = $peak; peakRatio = $peakRatio
                gcPauseMs = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.gcPauseMs })
                secondWallMs = Get-Median @($set | ForEach-Object { $_.Data.secondSearch.wallMs })
                loadMs = Get-Median @($set | ForEach-Object { $_.Data.load.loadMs })
                projects = Get-Median @($set | ForEach-Object { $_.Data.load.projects })
                declarations = Get-Median @($set | ForEach-Object { $_.Data.firstSearch.declarations })
            })
        }
        $verdicts[$query] = [ordered]@{ identicalAnswers = ($hashes.Count -eq 1); distinctAnswers = $hashes.Count; degreesWithinThresholds = $passing }
    }

    $complete = $verdicts.Count -eq 2 -and $failedLogs.Count -eq 0
    $go = $complete
    foreach ($key in $verdicts.Keys) {
        if (-not $verdicts[$key].identicalAnswers -or @($verdicts[$key].degreesWithinThresholds).Count -eq 0) { $go = $false }
    }
    $verdict = 'no-go'
    if (-not $complete) { $verdict = 'inconclusive' }
    elseif ($go) { $verdict = 'go' }

    $summary = [ordered]@{ verdict = $verdict; environment = $environment; queries = $verdicts; failedRuns = @($failedLogs | ForEach-Object Name); rows = $rows }
    [IO.File]::WriteAllText((Join-Path $Out 'summary.json'), ($summary | ConvertTo-Json -Depth 6), $utf8NoBom)

    $md = New-Object Collections.Generic.List[string]
    $md.Add('# Parallel declaration search matrix (D/E0/task-11)')
    $md.Add('')
    if ($null -ne $environment) {
        foreach ($p in $environment.PSObject.Properties) { $md.Add("- $($p.Name): $($p.Value)") }
        $md.Add('')
    }
    $md.Add("Verdict: **$verdict**. Thresholds fixed before measuring: on both queries a degree of 2 or 4 with median first-search wall <= 0.70 x degree 1 and median search peak working set <= 1.25 x degree 1; every answer identical.")
    $md.Add('')
    foreach ($key in $verdicts.Keys) {
        $v = $verdicts[$key]
        $md.Add("- ${key}: identical answers $($v.identicalAnswers) ($($v.distinctAnswers) distinct); degrees within thresholds: $(@($v.degreesWithinThresholds) -join ', ')")
    }
    if ($failedLogs.Count -gt 0) { $md.Add("- failed runs: $(@($failedLogs | ForEach-Object Name) -join ', ')") }
    $md.Add('')
    $md.Add('Medians over measured rounds (warmup excluded). Peak working set is sampled every 25 ms during the search only.')
    $md.Add('')
    $md.Add('| query | dop | runs | first search ms | ratio | CPU ms | busy cores | peak WS MB | WS ratio | GC pause ms | second search ms | load s | projects | declarations |')
    $md.Add('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    foreach ($r in $rows) {
        $md.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} |' -f
            $r.query, $r.degree, $r.runs, (Format-Number $r.firstWallMs), (Format-Number $r.wallRatio 3), (Format-Number $r.firstCpuMs),
            (Format-Number $r.busyCores 2), (Format-Number $r.peakWorkingSetMb), (Format-Number $r.peakRatio 3), (Format-Number $r.gcPauseMs),
            (Format-Number $r.secondWallMs), (Format-Number ($r.loadMs / 1000) 1), $r.projects, $r.declarations))
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
if ($degreeList -notcontains 1) { throw 'Degrees must include 1: it is the baseline.' }

Initialize-Stand $spikeProject $spikeDll $Solution $SkipRestore.IsPresent

if (-not $OutDirectory) {
    # Not under artifacts/: the folder is committed to bring the results to the analysis machine, then removed.
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') (Get-Date -Format 'yyyyMMdd-HHmmss')
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$environment = Get-Environment $Solution $repo ([ordered]@{ matchName = $MatchName; missingName = $MissingName; degrees = ($degreeList -join ', '); runs = $Runs })
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), $utf8NoBom)

Invoke-Matrix $Solution $OutDirectory
Write-Host ''
Write-Summary $OutDirectory
Write-Host ''
Write-HandoverCommands $repo $OutDirectory 'Add task-11 parallel search measurements'
