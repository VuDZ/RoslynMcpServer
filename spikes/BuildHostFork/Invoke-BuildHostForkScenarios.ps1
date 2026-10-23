<#
.SYNOPSIS
    BuildHost/task-00: failure scenarios of the unchanged fork next to the original BuildHost of Roslyn 5.9.0.

.DESCRIPTION
    All into one result folder, scenarios.md is the summary:
    1. fixture  - a generated solution with a multi-TFM project and failing entries: a project missing on disk,
                  a reference to a missing project, an F# project (no language services in the server),
                  a C++ project (no language for the workspace) and a project with broken XML. observe for both sides, warmup + one round, compared
                  like the baseline (fixture-runs/summary.md).
    2. broken   - a copy of the fork with its BuildHost dll deleted loads the fixture: the load must fail
                  visibly, there is no other BuildHost for the client to fall back to.
    3. cancel   - both sides load -Solution and cancel -DelayMs after the BuildHost appears.
    4. kill     - both sides load -Solution and kill the BuildHost -DelayMs after it appears.
    Every run also checks that no BuildHost process outlives the load (waits up to 10 s, then kills the rest).

.PARAMETER Solution
    Solution for cancel and kill; the load must last longer than -DelayMs. Default: RoslynMcpServer.sln of this
    repository.

.PARAMETER DelayMs
    Time from the first BuildHost process to the cancellation or the kill. Default 1500.

.PARAMETER OutDirectory
    Result folder. Default: results\scenarios-<time> next to this script (not tracked by Git).

.EXAMPLE
    .\spikes\BuildHostFork\Invoke-BuildHostForkScenarios.ps1 -Solution D:\src\roslyn\Roslyn.slnx -DelayMs 5000
#>
[CmdletBinding()]
param(
    [string]$Solution,

    [ValidateRange(0, 600000)]
    [int]$DelayMs = 1500,

    [string]$OutDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')
. (Join-Path $PSScriptRoot 'BuildHostForkCommon.ps1')

if (-not $Solution) { $Solution = Join-Path $repo 'RoslynMcpServer.sln' }
$Solution = [IO.Path]::GetFullPath($Solution)
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) { throw "Solution not found: $Solution" }
if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('scenarios-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutDirectory = [IO.Path]::GetFullPath($OutDirectory)
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null

$copies = Initialize-ForkCopy $repo $PSScriptRoot
$sides = [ordered]@{ original = (Join-Path $copies.originalBin 'BuildHostFork.dll'); fork = (Join-Path $copies.forkBin 'BuildHostFork.dll') }
$brokenBin = Join-Path $PSScriptRoot 'bin-fork-broken'
if (Test-Path -LiteralPath $brokenBin) { Remove-Item -LiteralPath $brokenBin -Recurse -Force }
Copy-Item -LiteralPath $copies.forkBin -Destination $brokenBin -Recurse
Remove-Item -LiteralPath (Join-Path (Join-Path $brokenBin 'BuildHost-netcore') 'Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll')

function Write-Utf8([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

# The fixture sits under the repository: empty Directory.Build files stop the repository's own props.
function New-Fixture([string]$Directory) {
    $sdkProject = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework{0}>{1}</TargetFramework{0}></PropertyGroup>{2}</Project>'
    Write-Utf8 (Join-Path $Directory 'Directory.Build.props') '<Project />'
    Write-Utf8 (Join-Path $Directory 'Directory.Build.targets') '<Project />'
    Write-Utf8 (Join-Path $Directory 'Ok/Ok.csproj') ($sdkProject -f '', 'net8.0', '')
    Write-Utf8 (Join-Path $Directory 'Ok/Class1.cs') "namespace Ok;`npublic class Class1 { }`n"
    $okReference = '<ItemGroup><ProjectReference Include="../Ok/Ok.csproj" /></ItemGroup>'
    Write-Utf8 (Join-Path $Directory 'MultiTfm/MultiTfm.csproj') ($sdkProject -f 's', 'net10.0;net8.0', $okReference)
    Write-Utf8 (Join-Path $Directory 'MultiTfm/Class1.cs') "namespace MultiTfm;`n#if NET10_0`npublic class Class1 : Ok.Class1 { }`n#else`npublic class Class1 { }`n#endif`n"
    $absentReference = '<ItemGroup><ProjectReference Include="../Absent/Absent.csproj" /></ItemGroup>'
    Write-Utf8 (Join-Path $Directory 'DanglingReference/DanglingReference.csproj') ($sdkProject -f '', 'net10.0', $absentReference)
    Write-Utf8 (Join-Path $Directory 'DanglingReference/Class1.cs') "namespace DanglingReference;`npublic class Class1 { }`n"
    # Not restored: the server has no F# services, the workspace keeps it as a project without documents.
    Write-Utf8 (Join-Path $Directory 'FSharp/FSharp.fsproj') ($sdkProject -f '', 'net10.0', '<ItemGroup><Compile Include="Library.fs" /></ItemGroup>')
    Write-Utf8 (Join-Path $Directory 'FSharp/Library.fs') "module Library`nlet value = 1`n"
    # The workspace rejects it by extension before any build.
    Write-Utf8 (Join-Path $Directory 'Unsupported/Unsupported.vcxproj') '<Project><Target Name="Build" /></Project>'
    Write-Utf8 (Join-Path $Directory 'Broken/Broken.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
    $projects = 'Ok/Ok.csproj', 'MultiTfm/MultiTfm.csproj', 'DanglingReference/DanglingReference.csproj', 'FSharp/FSharp.fsproj', 'Unsupported/Unsupported.vcxproj', 'Broken/Broken.csproj', 'Missing/Missing.csproj'
    $solution = Join-Path $Directory 'Scenarios.slnx'
    Write-Utf8 $solution ("<Solution>`n" + (($projects | ForEach-Object { "  <Project Path=`"$_`" />" }) -join "`n") + "`n</Solution>`n")
    # Only the projects that can be restored; the dangling reference fails restore and stays unrestored on purpose.
    foreach ($project in 'Ok/Ok.csproj', 'MultiTfm/MultiTfm.csproj') {
        Invoke-Native 'dotnet' @('restore', (Join-Path $Directory $project), '-nologo', '-v', 'q') "Fixture restore failed: $project"
    }
    return $solution
}

function Invoke-Scenario([string]$Label, [string]$Dll, [string]$Mode, [string]$Target, [string]$File) {
    Write-Host $Label
    $output = Join-Path $OutDirectory "$File.json"
    $exitCode = Invoke-Spike $Dll @($Mode, $Target, $output, "$DelayMs") (Join-Path $OutDirectory "$File.out.txt")
    $result = $null
    if (Test-Path -LiteralPath $output) { $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json }
    return [pscustomobject]@{ Label = $Label; ExitCode = $exitCode; Result = $result }
}

Write-Host 'Fixture'
$fixture = Join-Path $OutDirectory 'fixture'
$fixtureSolution = New-Fixture $fixture
$fixtureResults = Join-Path $OutDirectory 'fixture-runs'
New-Item -ItemType Directory -Force -Path $fixtureResults | Out-Null
Invoke-ObserveRounds $sides $fixtureSolution 'Class1' 1 $fixtureResults
Invoke-Native 'dotnet' @($sides['original'], 'compare', $fixtureResults) 'Fixture comparison failed'

Write-Host "Restoring $Solution"
Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed'
$runs = @(
    (Invoke-Scenario 'fixture load, original' $sides['original'] 'load' $fixtureSolution 'load-original'),
    (Invoke-Scenario 'fixture load, fork' $sides['fork'] 'load' $fixtureSolution 'load-fork'),
    (Invoke-Scenario 'fixture load, fork without its BuildHost dll' (Join-Path $brokenBin 'BuildHostFork.dll') 'load' $fixtureSolution 'broken-fork'),
    (Invoke-Scenario 'cancel, original' $sides['original'] 'cancel' $Solution 'cancel-original'),
    (Invoke-Scenario 'cancel, fork' $sides['fork'] 'cancel' $Solution 'cancel-fork'),
    (Invoke-Scenario 'kill BuildHost, original' $sides['original'] 'kill-host' $Solution 'kill-original'),
    (Invoke-Scenario 'kill BuildHost, fork' $sides['fork'] 'kill-host' $Solution 'kill-fork')
)

# Paths in the summary are relative: the spike folder, the fixture, the solution folder and the user profile.
$redactions = @(@($fixture, '<fixture>'), @($PSScriptRoot, '<spike>'), @((Split-Path $Solution), '<solution>'), @($HOME, '<user>'))
function Format-Redacted([string]$Text) {
    foreach ($pair in $redactions) { $Text = $Text.Replace($pair[0], $pair[1]) }
    return $Text.Replace('|', '\|')
}

$md = New-Object Text.StringBuilder
[void]$md.AppendLine('# BuildHost fork failure scenarios (BuildHost/task-00)')
[void]$md.AppendLine('')
[void]$md.AppendLine("- fork: $($copies.version); solution for cancel and kill: $(Split-Path $Solution -Leaf); delay after the first BuildHost: $DelayMs ms")
[void]$md.AppendLine('- fixture equivalence: ' + ((Get-Content -LiteralPath (Join-Path $fixtureResults 'summary.md')) -match '^- equivalence' -replace '^- equivalence: ', ''))
[void]$md.AppendLine('')
[void]$md.AppendLine('| scenario | exit | outcome | disturbed at ms | load ms | projects | failures / warnings | BuildHosts | left after load |')
[void]$md.AppendLine('|---|---|---|---|---|---|---|---|---|')
foreach ($run in $runs) {
    $r = $run.Result
    if ($null -eq $r) {
        [void]$md.AppendLine("| $($run.Label) | $($run.ExitCode) | no report | | | | | | |")
        continue
    }
    $failures = @($r.Diagnostics | Where-Object { $_ -like 'Failure:*' }).Count
    $warnings = @($r.Diagnostics | Where-Object { $_ -like 'Warning:*' }).Count
    $hosts = "$($r.BuildHostProcesses.count): " + (@($r.BuildHostProcesses.hosts) -join '; ')
    [void]$md.AppendLine("| $($run.Label) | $($run.ExitCode) | $(Format-Redacted $r.Outcome) | $(Format-Number $r.DisturbedAtMs) | $(Format-Number $r.LoadMs) | $($r.Projects) | $failures / $warnings | $(Format-Redacted $hosts) | $($r.RemainingBuildHosts) |")
}
[void]$md.AppendLine('')
[void]$md.AppendLine('Fixture diagnostics (fork load; the original load must list the same):')
[void]$md.AppendLine('')
$fixtureLoad = $runs[1].Result
if ($null -ne $fixtureLoad) {
    foreach ($diagnostic in $fixtureLoad.Diagnostics) { [void]$md.AppendLine('- ' + (Format-Redacted $diagnostic)) }
}
$sameDiagnostics = $null -ne $runs[0].Result -and $null -ne $fixtureLoad -and (($runs[0].Result.Diagnostics -join "`n") -eq ($fixtureLoad.Diagnostics -join "`n"))
[void]$md.AppendLine('')
[void]$md.AppendLine("Same fixture diagnostics in both loads: $sameDiagnostics")
Write-Utf8 (Join-Path $OutDirectory 'scenarios.md') $md.ToString()

Write-Host ''
Write-Host $md.ToString()
Write-Host "Results: $OutDirectory"
Write-Host 'scenarios.md is enough for the analysis; the JSON files hold paths and diagnostics of the solution.'
