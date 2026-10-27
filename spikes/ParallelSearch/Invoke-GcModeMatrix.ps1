<#
.SYNOPSIS
    Runs the D/E0/task-12 matrix: the same load and first declaration search under workstation and
    server GC, each measurement in a fresh process, and prints the go/no-go verdict.

.DESCRIPTION
    The task-11 matrix showed GC pauses taking 40-46% of the sequential first search on Roslyn.slnx
    under the default workstation GC. This script measures whether server GC removes that cost
    without unacceptable memory growth.

    For each query and round the script starts one process per (GC mode, degree) configuration.
    Round 0 is warmup and is not counted. The configuration order rotates every round. The GC mode
    is selected through DOTNET_gcServer and DOTNET_GCDynamicAdaptationMode for the child process
    only; every result records the mode the runtime actually applied, and the summary rejects a run
    whose recorded mode differs from the requested one.

    Thresholds were fixed in the task before any measurement and must not be adjusted here:
    go when, on both queries at degree 1, server GC has median first-search wall <= 0.80 of workstation,
    median load <= 1.05 of workstation, median process peak working set <= 1.25 of workstation, median
    final working set <= 1.25 of workstation, and every run of a query returned the same answer.
    Degree 4 rows are reported for information only.

.PARAMETER Solution
    Solution to measure. The task's stand is Roslyn.slnx at commit 013d3a758df6c137497ff37a93f0d4bed103853a.

.PARAMETER GcModes
    Comma-separated: workstation, server (DATAS, the .NET 10 default for server GC), server-nodatas.
    Default 'workstation,server'; workstation is required as the baseline.

.PARAMETER Degrees
    Comma-separated degrees of parallelism. Default '1,4'; 1 is required, the verdict uses it only.

.PARAMETER Runs
    Measured rounds per query; one warmup round is added. Default 5.

.PARAMETER SkipRestore
    Do not run dotnet restore on the solution first.

.PARAMETER OutDirectory
    Result folder. Default: results\gc-<time> next to this script, a folder meant to be committed so
    the results reach the analysis machine through Git; project paths in it are relative to the solution.

.PARAMETER SummarizeOnly
    Folder of an earlier run: only rebuild summary.md and summary.json from its JSON files.

.EXAMPLE
    .\spikes\ParallelSearch\Invoke-GcModeMatrix.ps1 -Solution D:\src\roslyn\Roslyn.slnx
#>
[CmdletBinding()]
param(
    [string]$Solution,

    [string]$MatchName = 'CSharpCompilationOptions',

    [string]$MissingName = 'NoSuchDeclarationName7d3f2a',

    # Comma-separated strings: powershell -File passes "a,b" as one string, not as an array.
    [string]$GcModes = 'workstation,server',

    [string]$Degrees = '1,4',

    [ValidateRange(1, 50)]
    [int]$Runs = 5,

    [switch]$SkipRestore,

    [string]$OutDirectory,

    [string]$SummarizeOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$degreeList = @($Degrees -split '[,\s]+' | Where-Object { $_ } | ForEach-Object { [int]$_ })
$modeList = @($GcModes -split '[,\s]+' | Where-Object { $_ })
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$spikeProject = Join-Path $PSScriptRoot 'ParallelSearch.csproj'
$spikeDll = Join-Path (Join-Path (Join-Path (Join-Path $PSScriptRoot 'bin') 'Release') 'net10.0') 'ParallelSearch.dll'
$utf8NoBom = New-Object Text.UTF8Encoding($false)

. (Join-Path $PSScriptRoot 'MatrixCommon.ps1')

# Environment of the child process for each mode. Both variables are always set, so a value left in the
# user's shell cannot leak into a mode; DATAS is the .NET 10 default for server GC and is off for workstation.
$modeSettings = [ordered]@{
    'workstation' = @{ Server = $false; Variables = [ordered]@{ DOTNET_gcServer = '0'; DOTNET_GCDynamicAdaptationMode = $null } }
    'server' = @{ Server = $true; Variables = [ordered]@{ DOTNET_gcServer = '1'; DOTNET_GCDynamicAdaptationMode = $null } }
    'server-nodatas' = @{ Server = $true; Variables = [ordered]@{ DOTNET_gcServer = '1'; DOTNET_GCDynamicAdaptationMode = '0' } }
}

function Assert-NoForeignGcSettings {
    $managed = @('DOTNET_gcServer', 'DOTNET_GCDynamicAdaptationMode')
    $foreign = @(Get-ChildItem Env: | Where-Object {
        ($_.Name -match '^(DOTNET|COMPlus)_GC' -or $_.Name -match '^(DOTNET|COMPlus)_gcServer$' -or $_.Name -match '^(DOTNET|COMPlus)_gcConcurrent$') -and
        $managed -notcontains $_.Name
    } | ForEach-Object Name)
    if ($foreign.Count -gt 0) {
        throw "GC settings in the environment would change every mode: $($foreign -join ', '). Remove them and run again."
    }
}

function Invoke-Measure([string]$SolutionPath, [string]$Name, [int]$Degree, [string]$Mode, [string]$File, [string]$Output) {
    $variables = $modeSettings[$Mode].Variables
    $saved = @{}
    foreach ($key in $variables.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key) }
    try {
        foreach ($key in $variables.Keys) { [Environment]::SetEnvironmentVariable($key, $variables[$key]) }
        & dotnet $spikeDll measure --solution $SolutionPath --name $Name --dop $Degree --output $File 2>&1 |
            Out-File -LiteralPath $Output -Encoding utf8
        return $LASTEXITCODE
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
}

function Invoke-Matrix([string]$SolutionPath, [string]$Out) {
    $queries = [ordered]@{ match = $MatchName; missing = $MissingName }
    $configurations = @(foreach ($mode in $modeList) { foreach ($degree in $degreeList) { [pscustomobject]@{ Mode = $mode; Degree = $degree } } })
    $total = $queries.Count * ($Runs + 1) * $configurations.Count
    $done = 0
    foreach ($query in $queries.Keys) {
        for ($round = 0; $round -le $Runs; $round++) {
            # Rotate the start so every configuration runs first, last and in between across rounds.
            $order = @(for ($i = 0; $i -lt $configurations.Count; $i++) { $configurations[($i + $round) % $configurations.Count] })
            foreach ($configuration in $order) {
                $done++
                $stem = "{0}-{1}-dop{2}-round{3}" -f $query, $configuration.Mode, $configuration.Degree, $round
                $file = Join-Path $Out "$stem.json"
                # Not *.log: the repository ignores that pattern, and these files travel through Git.
                $output = Join-Path $Out "$stem.out.txt"
                $label = "[$done/$total] $query gc=$($configuration.Mode) dop=$($configuration.Degree) round=$round"
                if ($round -eq 0) { $label += ' (warmup)' }
                Write-Host $label
                $watch = [Diagnostics.Stopwatch]::StartNew()
                $exitCode = Invoke-Measure $SolutionPath $queries[$query] $configuration.Degree $configuration.Mode $file $output
                if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $file)) {
                    Write-Warning "$label failed (exit code $exitCode); see $output"
                    continue
                }
                $run = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
                Write-Host ("    server GC {0}, load {1} s, first search {2} ms (GC pause {3}), final WS {4} MB, process {5} s" -f
                    $run.gc.server, (Format-Number ($run.load.loadMs / 1000) 1), (Format-Number $run.firstSearch.wallMs),
                    (Format-Number $run.firstSearch.gcPauseMs), (Format-Number $run.final.workingSetMb), (Format-Number $watch.Elapsed.TotalSeconds 1))
            }
        }
    }
}

function Get-Ratio($Value, $Baseline) {
    if ($null -eq $Value -or -not $Baseline) { return $null }
    return $Value / $Baseline
}

function Get-Row([string]$Query, [string]$Mode, [int]$Degree, [object[]]$Set) {
    return [pscustomobject][ordered]@{
        query = $Query; mode = $Mode; degree = $Degree; runs = $Set.Count
        firstWallMs = Get-Median @($Set | ForEach-Object { $_.Data.firstSearch.wallMs })
        firstGcPauseMs = Get-Median @($Set | ForEach-Object { $_.Data.firstSearch.gcPauseMs })
        firstGcCount = Get-Median @($Set | ForEach-Object { $_.Data.firstSearch.gcCollections[0] })
        busyCores = Get-Median @($Set | ForEach-Object { $_.Data.firstSearch.busyCores })
        searchPeakWorkingSetMb = Get-Median @($Set | ForEach-Object { $_.Data.firstSearch.peakWorkingSetMb })
        processPeakWorkingSetMb = Get-Median @($Set | ForEach-Object { $_.Data.final.processPeakWorkingSetMb })
        finalWorkingSetMb = Get-Median @($Set | ForEach-Object { $_.Data.final.workingSetMb })
        finalGcHeapMb = Get-Median @($Set | ForEach-Object { $_.Data.final.gcHeapMb })
        secondWallMs = Get-Median @($Set | ForEach-Object { $_.Data.secondSearch.wallMs })
        loadMs = Get-Median @($Set | ForEach-Object { $_.Data.load.loadMs })
        loadGcPauseMs = Get-Median @($Set | ForEach-Object { $_.Data.loadGcPauseMs })
        projects = Get-Median @($Set | ForEach-Object { $_.Data.load.projects })
        declarations = Get-Median @($Set | ForEach-Object { $_.Data.firstSearch.declarations })
        wallRatio = $null; loadRatio = $null; processPeakRatio = $null; finalRatio = $null
    }
}

function Write-Summary([string]$Out) {
    $environmentFile = Join-Path $Out 'environment.json'
    $environment = $null
    if (Test-Path -LiteralPath $environmentFile) { $environment = Get-Content -LiteralPath $environmentFile -Raw | ConvertFrom-Json }
    $runs = @(Get-ChildItem -LiteralPath $Out -Filter '*-dop*-round*.json' | ForEach-Object {
        $match = [regex]::Match($_.Name, '^(?<q>[a-z]+)-(?<m>[a-z-]+)-dop(?<d>\d+)-round(?<r>\d+)\.json$')
        if (-not $match.Success) { return }
        $data = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        [pscustomobject]@{ Query = $match.Groups['q'].Value; Mode = $match.Groups['m'].Value; Degree = [int]$match.Groups['d'].Value; Round = [int]$match.Groups['r'].Value; Data = $data; Name = $_.Name }
    })
    $failedLogs = @(Get-ChildItem -LiteralPath $Out -Filter '*.out.txt' | Where-Object {
        -not (Test-Path -LiteralPath ($_.FullName -replace '\.out\.txt$', '.json'))
    })
    # A run whose runtime applied another GC mode than requested would silently mix the modes.
    $wrongMode = @($runs | Where-Object { -not $modeSettings.Contains($_.Mode) -or [bool]$_.Data.gc.server -ne $modeSettings[$_.Mode].Server } | ForEach-Object Name)

    $rows = New-Object Collections.Generic.List[object]
    $verdicts = [ordered]@{}
    foreach ($query in @($runs | Select-Object -ExpandProperty Query -Unique)) {
        $all = @($runs | Where-Object { $_.Query -eq $query })
        $hashes = @($all | ForEach-Object { $_.Data.firstSearch.resultSha256; $_.Data.secondSearch.resultSha256 } | Select-Object -Unique)
        $measured = @($all | Where-Object { $_.Round -gt 0 })
        $baselines = @{}
        foreach ($degree in @($measured | Select-Object -ExpandProperty Degree -Unique | Sort-Object)) {
            $set = @($measured | Where-Object { $_.Mode -eq 'workstation' -and $_.Degree -eq $degree })
            if ($set.Count -gt 0) { $baselines[$degree] = Get-Row $query 'workstation' $degree $set }
        }
        $passing = @()
        foreach ($mode in @($modeSettings.Keys | Where-Object { $measured.Mode -contains $_ })) {
            foreach ($degree in @($measured | Where-Object { $_.Mode -eq $mode } | Select-Object -ExpandProperty Degree -Unique | Sort-Object)) {
                $row = Get-Row $query $mode $degree @($measured | Where-Object { $_.Mode -eq $mode -and $_.Degree -eq $degree })
                if ($baselines.ContainsKey($degree)) {
                    $base = $baselines[$degree]
                    $row.wallRatio = Get-Ratio $row.firstWallMs $base.firstWallMs
                    $row.loadRatio = Get-Ratio $row.loadMs $base.loadMs
                    $row.processPeakRatio = Get-Ratio $row.processPeakWorkingSetMb $base.processPeakWorkingSetMb
                    $row.finalRatio = Get-Ratio $row.finalWorkingSetMb $base.finalWorkingSetMb
                }
                if ($mode -ne 'workstation' -and $degree -eq 1 -and $null -ne $row.wallRatio -and
                    $row.wallRatio -le 0.80 -and $row.loadRatio -le 1.05 -and $row.processPeakRatio -le 1.25 -and $row.finalRatio -le 1.25) {
                    $passing += $mode
                }
                $rows.Add($row)
            }
        }
        $verdicts[$query] = [ordered]@{ identicalAnswers = ($hashes.Count -eq 1); distinctAnswers = $hashes.Count; modesWithinThresholds = $passing }
    }

    $complete = $verdicts.Count -eq 2 -and $failedLogs.Count -eq 0 -and $wrongMode.Count -eq 0
    $verdict = 'inconclusive'
    if ($complete) {
        # A mode goes only when it passes on both queries and every answer of both queries is identical.
        $goModes = @($modeSettings.Keys | Where-Object { $_ -ne 'workstation' } | Where-Object {
            $mode = $_
            @($verdicts.Values | Where-Object { $_.identicalAnswers -and @($_.modesWithinThresholds) -contains $mode }).Count -eq $verdicts.Count
        })
        $verdict = 'no-go'
        if ($goModes.Count -gt 0) { $verdict = "go ($($goModes -join ', '))" }
    }

    $summary = [ordered]@{ verdict = $verdict; environment = $environment; queries = $verdicts; failedRuns = @($failedLogs | ForEach-Object Name); wrongModeRuns = $wrongMode; rows = $rows }
    [IO.File]::WriteAllText((Join-Path $Out 'summary.json'), ($summary | ConvertTo-Json -Depth 6), $utf8NoBom)

    $md = New-Object Collections.Generic.List[string]
    $md.Add('# GC mode matrix for load and first declaration search (D/E0/task-12)')
    $md.Add('')
    if ($null -ne $environment) {
        foreach ($p in $environment.PSObject.Properties) { $md.Add("- $($p.Name): $($p.Value)") }
        $md.Add('')
    }
    $md.Add("Verdict: **$verdict**. Thresholds fixed before measuring, degree 1, both queries, against workstation GC: first-search wall <= 0.80, load <= 1.05, process peak working set <= 1.25, final working set <= 1.25; every answer identical. Degree 4 is informational.")
    $md.Add('')
    foreach ($key in $verdicts.Keys) {
        $v = $verdicts[$key]
        $md.Add("- ${key}: identical answers $($v.identicalAnswers) ($($v.distinctAnswers) distinct); modes within thresholds: $(@($v.modesWithinThresholds) -join ', ')")
    }
    if ($failedLogs.Count -gt 0) { $md.Add("- failed runs: $(@($failedLogs | ForEach-Object Name) -join ', ')") }
    if ($wrongMode.Count -gt 0) { $md.Add("- runs with another GC mode than requested: $($wrongMode -join ', ')") }
    $md.Add('')
    $md.Add('Medians over measured rounds (warmup excluded). Ratios compare with workstation GC at the same degree. Search peak WS is sampled every 25 ms during the first search; process peak and final WS are read after the second search, without a forced collection.')
    $md.Add('')
    $md.Add('| query | gc | dop | runs | first search ms | ratio | GC pause ms | GCs | busy cores | search peak WS MB | process peak WS MB | ratio | final WS MB | ratio | final GC heap MB | second search ms | load s | ratio | load GC pause ms | projects | declarations |')
    $md.Add('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    foreach ($r in $rows) {
        $md.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} | {14} | {15} | {16} | {17} | {18} | {19} | {20} |' -f
            $r.query, $r.mode, $r.degree, $r.runs, (Format-Number $r.firstWallMs), (Format-Number $r.wallRatio 3), (Format-Number $r.firstGcPauseMs),
            (Format-Number $r.firstGcCount), (Format-Number $r.busyCores 2), (Format-Number $r.searchPeakWorkingSetMb), (Format-Number $r.processPeakWorkingSetMb),
            (Format-Number $r.processPeakRatio 3), (Format-Number $r.finalWorkingSetMb), (Format-Number $r.finalRatio 3), (Format-Number $r.finalGcHeapMb),
            (Format-Number $r.secondWallMs), (Format-Number ($r.loadMs / 1000) 1), (Format-Number $r.loadRatio 3), (Format-Number $r.loadGcPauseMs),
            $r.projects, $r.declarations))
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
if ($degreeList -notcontains 1) { throw 'Degrees must include 1: the verdict uses it.' }
if ($modeList -notcontains 'workstation') { throw 'GcModes must include workstation: it is the baseline.' }
foreach ($mode in $modeList) {
    if (-not $modeSettings.Contains($mode)) { throw "Unknown GC mode '$mode'. Known: $($modeSettings.Keys -join ', ')." }
}
Assert-NoForeignGcSettings

Initialize-Stand $spikeProject $spikeDll $Solution $SkipRestore.IsPresent

if (-not $OutDirectory) {
    # Not under artifacts/: the folder is committed to bring the results to the analysis machine, then removed.
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('gc-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$environment = Get-Environment $Solution $repo ([ordered]@{
    matchName = $MatchName; missingName = $MissingName; gcModes = ($modeList -join ', '); degrees = ($degreeList -join ', '); runs = $Runs
})
[IO.File]::WriteAllText((Join-Path $OutDirectory 'environment.json'), ($environment | ConvertTo-Json), $utf8NoBom)

Invoke-Matrix $Solution $OutDirectory
Write-Host ''
Write-Summary $OutDirectory
Write-Host ''
Write-HandoverCommands $repo $OutDirectory 'Add task-12 GC mode measurements'
