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
    3. Runs Measure-WorkspaceLoad.ps1 from this tree: warmup, measured fresh-process runs and a RAM
       hit, then one separate trace run of the first search.

    Nothing is sent anywhere and Cursor settings are not touched. The last lines name the folder
    with results and what to send.

.PARAMETER Workspace
    .sln, .slnx or .csproj to load.

.PARAMETER SymbolName
    Type name for the first find_symbol_definition, for example MainViewModel.

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

$measure = @{
    Workspace = $Workspace
    ServerExe = $serverExe
    SymbolName = $SymbolName
    Runs = $Runs
    Note = $fullNote
    OutDirectory = $out
}
if ($Configuration) { $measure.Configuration = $Configuration }
if ($Platform) { $measure.Platform = $Platform }
if ($TargetFramework) { $measure.TargetFramework = $TargetFramework }
if (-not $NoTrace) { $measure.TracerExe = $TracerExe }
& (Join-Path $PSScriptRoot 'Measure-WorkspaceLoad.ps1') @measure

Write-Host ''
Write-Host "Results: $out"
Write-Host 'Send summary.md. Keep runs.json, run-*-log.txt and run-*-response.md until asked for them.'
if (-not $NoTrace) {
    Write-Host 'The run-*-find.nettrace file holds CPU samples and exception messages with file paths;'
    Write-Host 'send it only where sharing those paths is allowed.'
}
