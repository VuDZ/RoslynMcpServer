<#
.SYNOPSIS
    Prepares the server and dotnet-trace from this repository tree and measures load_workspace
    and the first find_symbol_definition on one solution (D/E0/task-10).

.DESCRIPTION
    Run it from the checked-out research branch after updating it from Git:

    1. Publishes RoslynMcpServer from this tree into its own folder under artifacts\, never into a
       folder that Cursor uses: a running server locks its DLLs, and mixing EXE and DLL versions
       makes the measurement meaningless.
    2. Installs a pinned dotnet-trace into a local tool folder (not globally), unless -NoTrace or
       -TracerExe is given.
    3. Runs Measure-WorkspaceLoad.ps1 from this tree once per variant: GC mode x search prewarm x
       pause before the first search. Each pass has a warmup, measured fresh-process runs and a RAM
       hit; the first pass also gets one separate trace run of the first search. All passes use the
       same published server and differ only in its environment: DOTNET_gcServer=0 forces workstation
       GC, ROSLYN_MCP_SEARCH_PREWARM=0 turns the index prewarm off (server 1.5.15+).
       comparison.md puts the medians side by side; a prewarm-on row is compared with the prewarm-off
       row of the same GC mode and pause.

    Nothing is sent anywhere and Cursor settings are not touched. The last lines name the folder
    with results and what to send.

.PARAMETER Workspace
    .sln, .slnx or .csproj to load.

.PARAMETER SymbolName
    Type name for the first find_symbol_definition, for example MainViewModel.

.PARAMETER GcModes
    Comma-separated GC modes: server, workstation. Default 'server' (server GC from 1.5.12).

.PARAMETER PrewarmModes
    Comma-separated: on, off. Default 'on,off'. 'off' sets ROSLYN_MCP_SEARCH_PREWARM=0.

.PARAMETER FindDelays
    Comma-separated seconds between load_workspace and the first search. Default '0,30': a search right
    after the load, and one after the time an agent spends reading code.

.PARAMETER TracerExe
    Existing dotnet-trace executable, for example a downloaded single-file dotnet-trace.exe.
    Skips the local tool installation.

.PARAMETER NoTrace
    Skip the trace run and the dotnet-trace installation.

.PARAMETER PrepareOnly
    Publish the server and prepare dotnet-trace, print their paths, and stop before measuring.

.EXAMPLE
    .\scripts\Invoke-FirstSearchDiagnostics.ps1 -Workspace D:\src\App\App.sln -SymbolName MainViewModel -Platform x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Workspace,

    [Parameter(Mandatory = $true)]
    [string]$SymbolName,

    [string]$Configuration,

    [string]$Platform,

    [string]$TargetFramework,

    [ValidateRange(2, 50)]
    [int]$Runs = 4,

    [string]$Note = '',

    # A comma-separated string: powershell -File passes "a,b" as one string, not as an array.
    [string]$GcModes = 'server',

    [string]$PrewarmModes = 'on,off',

    [string]$FindDelays = '0,30',

    [string]$TracerExe,

    [switch]$NoTrace,

    [switch]$PrepareOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Profile names dotnet-common and dotnet-sampled-thread-time, used by the measurement script,
# exist from dotnet-trace 10; older versions name them differently.
$tracerVersion = '10.0.750501'

$repo = Split-Path $PSScriptRoot -Parent
$onWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$rid = 'linux-x64'
$serverFile = 'RoslynMcpServer'
$tracerFile = 'dotnet-trace'
if ($onWindows) {
    $rid = 'win-x64'
    $serverFile = 'RoslynMcpServer.exe'
    $tracerFile = 'dotnet-trace.exe'
}

# Native output goes to the host: inside a function it would otherwise become part of the return value.
function Invoke-Native([string]$FilePath, [string[]]$Arguments, [string]$Failure) {
    & $FilePath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Failure (exit code $LASTEXITCODE)" }
}

function Get-TreeState {
    $commit = (& git -C $repo rev-parse --short=12 HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $commit) { throw "Not a Git checkout: $repo" }
    $branch = (& git -C $repo rev-parse --abbrev-ref HEAD 2>$null)
    $changes = @(& git -C $repo status --porcelain 2>$null)
    return [pscustomobject]@{ Commit = $commit.Trim(); Branch = ([string]$branch).Trim(); Dirty = ($changes.Count -gt 0) }
}

function Publish-Server($Tree, [string]$Root) {
    $out = Join-Path $Root ("server-" + $Tree.Commit)
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
    Write-Host "Publishing the server ($rid) into $out"
    Invoke-Native 'dotnet' @('publish', (Join-Path $repo 'RoslynMcpServer.csproj'), '-c', 'Release', '-r', $rid,
        '--self-contained', 'false', '-o', $out, '-nologo', '-v', 'q') 'dotnet publish failed'
    $exe = Join-Path $out $serverFile
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published server not found: $exe" }
    return $exe
}

function Get-Median($Values) {
    $sorted = @($Values | Where-Object { $null -ne $_ } | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $mid = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$mid] }
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
}

function Format-Value($Value, [int]$Digits = 0) {
    if ($null -eq $Value) { return 'n/a' }
    return ([Math]::Round([double]$Value, $Digits)).ToString([Globalization.CultureInfo]::InvariantCulture)
}

function New-Variant([string]$Gc, [string]$Prewarm, [int]$Delay) {
    return [pscustomobject]@{ Name = "$Gc-prewarm-$Prewarm-delay-$Delay"; Gc = $Gc; Prewarm = $Prewarm; Delay = $Delay }
}

# Runs the measurement with the variant's variables set for the server process tree only; $null leaves
# the server's own default (server GC from its runtimeconfig, prewarm on).
function Invoke-Measurement([hashtable]$Arguments, $Variant) {
    $variables = [ordered]@{
        DOTNET_gcServer = $(if ($Variant.Gc -eq 'workstation') { '0' } else { $null })
        ROSLYN_MCP_SEARCH_PREWARM = $(if ($Variant.Prewarm -eq 'off') { '0' } else { $null })
    }
    $saved = @{}
    foreach ($key in $variables.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key) }
    try {
        foreach ($key in $variables.Keys) { [Environment]::SetEnvironmentVariable($key, $variables[$key]) }
        & (Join-Path $PSScriptRoot 'Measure-WorkspaceLoad.ps1') @Arguments
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
}

function Write-Comparison([string]$Root, $Variants) {
    $md = New-Object Collections.Generic.List[string]
    $md.Add('# Variant comparison')
    $md.Add('')
    $md.Add('Medians over measured fresh-process runs of each variant (warmup, RAM hit and trace runs excluded). A prewarm-on ratio compares with the prewarm-off variant of the same GC mode and pause. GC pauses are counted for the whole process, so a prewarm still running during the first search adds its collections. A GC-mode comparison needs -GcModes server,workstation; the default runs server GC only.')
    $md.Add('')
    $md.Add('| variant | runtime GC in search log | prewarm success | runs | load client ms | first find client ms | ratio | process GC pause during first search ms | WS after load MB |')
    $md.Add('|---|---|---|---|---|---|---|---|---|')
    $rows = @(foreach ($variant in $Variants) {
        $document = Get-Content -LiteralPath (Join-Path (Join-Path $Root $variant.Name) 'runs.json') -Raw | ConvertFrom-Json
        $measured = @($document.runs | Where-Object { $_.kind -eq 'measured' -and $null -ne $_.load -and $_.load.state -eq 'ok' -and $null -ne $_.find })
        $searches = @($measured | ForEach-Object { @($_.find.searchSummaries) | Select-Object -First 1 } | Where-Object { $null -ne $_ -and $_.PSObject.Properties['serverGc'] })
        $applied = @($searches | ForEach-Object { if ($_.serverGc) { 'server' } else { 'workstation' } } | Select-Object -Unique)
        # The prewarm record lands in the load or the find window, whichever it ended in.
        $prewarmed = @($measured | Where-Object {
            $run = $_
            @('load', 'find' | Where-Object { $run.$_.PSObject.Properties['searchPrewarm'] -and $null -ne $run.$_.searchPrewarm -and $run.$_.searchPrewarm.outcome -eq 'success' }).Count -gt 0
        })
        [pscustomobject]@{
            Variant = $variant; Applied = ($applied -join ', '); Prewarmed = $prewarmed.Count; Runs = $measured.Count
            LoadMs = Get-Median @($measured | ForEach-Object { $_.load.clientMs })
            FindMs = Get-Median @($measured | ForEach-Object { $_.find.clientMs })
            PauseMs = Get-Median @($searches | ForEach-Object { $_.gcPauseMs })
            # The load log line is the only working-set record; nothing measures it after the search.
            WorkingSetMb = Get-Median @($measured | ForEach-Object { $_.load.workingSetMb })
        }
    })
    foreach ($r in $rows) {
        $base = @($rows | Where-Object { $_.Variant.Gc -eq $r.Variant.Gc -and $_.Variant.Delay -eq $r.Variant.Delay -and $_.Variant.Prewarm -eq 'off' }) | Select-Object -First 1
        $findRatio = $null
        if ($null -ne $base -and $base.FindMs) { $findRatio = $r.FindMs / $base.FindMs }
        $applied = $r.Applied
        if (-not $applied) { $applied = 'n/a (server before 1.5.12)' }
        $md.Add(('| {0} | {1} | {2}/{3} | {3} | {4} | {5} | {6} | {7} | {8} |' -f $r.Variant.Name, $applied, $r.Prewarmed, $r.Runs, (Format-Value $r.LoadMs),
            (Format-Value $r.FindMs), (Format-Value $findRatio 3), (Format-Value $r.PauseMs), (Format-Value $r.WorkingSetMb)))
    }
    [IO.File]::WriteAllLines((Join-Path $Root 'comparison.md'), $md, (New-Object Text.UTF8Encoding($false)))
    $md | ForEach-Object { Write-Host $_ }
}

function Install-Tracer([string]$Root) {
    $toolPath = Join-Path $Root 'tools'
    $exe = Join-Path $toolPath $tracerFile
    if (Test-Path -LiteralPath $exe -PathType Leaf) {
        $installed = (& $exe --version 2>$null | Select-Object -First 1)
        if ([string]$installed -like "$tracerVersion*") { return $exe }
        Invoke-Native 'dotnet' @('tool', 'uninstall', 'dotnet-trace', '--tool-path', $toolPath) 'dotnet tool uninstall failed'
    }
    Write-Host "Installing dotnet-trace $tracerVersion into $toolPath"
    & dotnet tool install dotnet-trace --version $tracerVersion --tool-path $toolPath | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw ("dotnet-trace could not be installed from NuGet. Download the single-file build from " +
            "https://aka.ms/dotnet-trace/$rid, then pass its path with -TracerExe, or run with -NoTrace.")
    }
    return $exe
}

$Workspace = [IO.Path]::GetFullPath($Workspace)
if (-not (Test-Path -LiteralPath $Workspace -PathType Leaf)) { throw "Workspace file not found: $Workspace" }
if ($null -eq (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)) { throw 'dotnet was not found on PATH.' }
if ($TracerExe -and $NoTrace) { throw 'Use either -TracerExe or -NoTrace.' }
# $null keeps the server's runtimeconfig (server GC from 1.5.12); '0' forces workstation GC.
$gcList = @($GcModes -split '[,\s]+' | Where-Object { $_ })
$prewarmList = @($PrewarmModes -split '[,\s]+' | Where-Object { $_ })
$delayList = @($FindDelays -split '[,\s]+' | Where-Object { $_ } | ForEach-Object { [int]$_ })
foreach ($gc in $gcList) { if (@('server', 'workstation') -notcontains $gc) { throw "Unknown GC mode '$gc'. Known: server, workstation." } }
foreach ($prewarm in $prewarmList) { if (@('on', 'off') -notcontains $prewarm) { throw "Unknown prewarm mode '$prewarm'. Known: on, off." } }
$variants = @(foreach ($gc in $gcList) { foreach ($prewarm in $prewarmList) { foreach ($delay in $delayList) { New-Variant $gc $prewarm $delay } } })
if ($variants.Count -eq 0) { throw 'No variant to measure: GcModes, PrewarmModes or FindDelays is empty.' }
$foreignGc = @(Get-ChildItem Env: | Where-Object { $_.Name -match '^(DOTNET|COMPlus)_(GC|gc)' -and $_.Name -ne 'DOTNET_gcServer' } | ForEach-Object Name)
if ($foreignGc.Count -gt 0) { throw "GC settings in the environment would apply to every mode: $($foreignGc -join ', '). Remove them and run again." }

$tree = Get-TreeState
if ($tree.Dirty) {
    Write-Warning "The repository tree has local changes; the report records commit $($tree.Commit) with a dirty-tree note."
}
$root = Join-Path (Join-Path $repo 'artifacts') 'first-search-diagnostics'
New-Item -ItemType Directory -Force -Path $root | Out-Null

$serverExe = Publish-Server $tree $root
if (-not $NoTrace -and -not $TracerExe) { $TracerExe = Install-Tracer $root }

Write-Host ''
Write-Host "Server: $serverExe"
if ($NoTrace) { Write-Host 'dotnet-trace: skipped (-NoTrace)' }
else { Write-Host "dotnet-trace: $TracerExe" }
if ($PrepareOnly) { return }

$treeNote = "branch $($tree.Branch), commit $($tree.Commit)"
if ($tree.Dirty) { $treeNote += ' (dirty tree)' }
$fullNote = $treeNote
if ($Note) { $fullNote = "$Note; $treeNote" }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $root ([IO.Path]::GetFileNameWithoutExtension($Workspace) + "-$stamp")

$index = 0
foreach ($variant in $variants) {
    $measure = @{
        Workspace = $Workspace
        ServerExe = $serverExe
        SymbolName = $SymbolName
        Runs = $Runs
        Note = "$fullNote; $($variant.Name)"
        OutDirectory = (Join-Path $out $variant.Name)
        FindDelaySeconds = $variant.Delay
    }
    if ($Configuration) { $measure.Configuration = $Configuration }
    if ($Platform) { $measure.Platform = $Platform }
    if ($TargetFramework) { $measure.TargetFramework = $TargetFramework }
    # One trace is enough to explain the first search; a second would add a run without new evidence.
    if (-not $NoTrace -and $index -eq 0) { $measure.TracerExe = $TracerExe }
    Write-Host ''
    Write-Host "== $($variant.Name) =="
    Invoke-Measurement $measure $variant
    $index++
}

Write-Host ''
if ($variants.Count -gt 1) { Write-Comparison $out $variants }
Write-Host ''
Write-Host "Results: $out"
Write-Host 'Send comparison.md and summary.md of each variant folder. Keep runs.json, run-*-log.txt and run-*-response.md until asked for them.'
if (-not $NoTrace) {
    Write-Host 'The run-*-find.nettrace file holds CPU samples and exception messages with file paths;'
    Write-Host 'send it only where sharing those paths is allowed.'
}
