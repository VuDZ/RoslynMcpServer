<#
.SYNOPSIS
    BuildHost/task-00: failure scenarios of the unchanged fork next to the original BuildHost of Roslyn 5.9.0.

.DESCRIPTION
    All into one result folder, scenarios.md is the summary:
    1. fixture  - a generated solution with a multi-TFM project and failing entries: a project missing on disk,
                  a reference to a missing project, an F# project (no language services in the server),
                  a C++ project (no language for the workspace) and a project with broken XML. Its first entry
                  reaches, through the second, the multi-TFM project and a project that warn in their evaluations
                  (a repeated import), the latter also in its build and in GetTargetPath, so the client builds
                  both entries before loading those references, and with -ReferencesFirst the fork builds them
                  ahead and must hand over the same diagnostics in the same order. observe for both sides,
                  warmup + three rounds, compared like the baseline (fixture-runs/summary.md, with the report
                  order of the diagnostics); then one load of each side, whose diagnostics must match line by
                  line.
    2. broken   - a copy of the fork with its BuildHost dll deleted loads the fixture: the load must fail
                  visibly, there is no other BuildHost for the client to fall back to.
    3. load     - both sides load -Solution undisturbed; the original's load sets the default delay.
    4. cancel   - both sides load -Solution and cancel -DelayMs after the BuildHost appears.
    5. kill     - both sides load -Solution and kill the BuildHost -DelayMs after it appears. Upstream ends such
                  a load either with a lost connection or, when the kill lands between requests, by starting
                  a new BuildHost for the next project and completing. Each side may end either way; a completed
                  load must have started a new BuildHost and match the undisturbed load of its side.
    6. early cancel - both sides cancel as soon as the BuildHost appears, while the client still sets it up.
    Every run also records the BuildHost processes still running 10 s after the load (then kills them).

    The script fails (exit code 1) when a run does not do what its scenario needs (the load ended before the
    cancel or the kill, an unexpected outcome, a BuildHost from another folder, a cancel at -DelayMs that left
    a BuildHost, which means it landed in the client's setup of the host) or when the fork differs from the
    original: outcome (except after a kill), diagnostics, projects, BuildHost processes left. Processes left the
    same way on both sides by the early cancel are upstream behavior: reported, not a failure of the fork.

.PARAMETER Solution
    Solution for load, cancel and kill; it must load for at least a few seconds. Default: RoslynMcpServer.sln of
    this repository.

.PARAMETER DelayMs
    Time from the first BuildHost process to the cancellation or the kill. Default: half of the time the
    original's undisturbed load runs after its BuildHost appears.

.PARAMETER OutDirectory
    Result folder. Default: results\scenarios-<time> next to this script (not tracked by Git).

.PARAMETER ReferencesFirst
    Run the fork side, the broken copy included, with part A of BuildHost/task-02
    (ROSLYN_BUILDHOST_REFERENCES_FIRST=1).

.PARAMETER EvaluateAhead
    Run the fork side, the broken copy included, with part B of BuildHost/task-02 on this many worker threads
    (ROSLYN_BUILDHOST_EVALUATE_AHEAD). Default 0: off.

.PARAMETER EvaluateAheadSolution
    all (default), a window of entries, or off (ROSLYN_BUILDHOST_EVALUATE_AHEAD_SOLUTION).

.PARAMETER ServerGc
    Task-04 experiment: server GC with ConserveMemory=9 in a separate copy of the fork host. Default off.

.PARAMETER SharedSdkCache
    Run the fork side, the broken copy included, with candidate C of BuildHost/task-04: the evaluations of each
    project share one SDK resolution cache (ROSLYN_BUILDHOST_SHARED_SDK_CACHE=1).

.EXAMPLE
    .\spikes\BuildHostFork\Invoke-BuildHostForkScenarios.ps1 -Solution D:\src\roslyn\Roslyn.slnx
#>
[CmdletBinding()]
param(
    [string]$Solution,

    [ValidateRange(0, 600000)]
    [int]$DelayMs,

    [string]$OutDirectory,

    [switch]$ReferencesFirst,

    [ValidateRange(0, 64)]
    [int]$EvaluateAhead = 0,

    [ValidatePattern('^(all|off|[1-9][0-9]*)$')]
    [string]$EvaluateAheadSolution = 'all',

    [switch]$ServerGc,

    [switch]$SharedSdkCache
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path (Join-Path $spikes 'ParallelSearch') 'MatrixCommon.ps1')
. (Join-Path $PSScriptRoot 'BuildHostForkCommon.ps1')

if (-not $Solution) { $Solution = Join-Path $repo 'RoslynMcpServer.sln' }
# PowerShell's location can differ from the process directory used by .NET and the child host.
$Solution = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Solution)
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) { throw "Solution not found: $Solution" }
# A solution outside this public repository may be private: its name and paths stay out of scenarios.md.
$external = -not $Solution.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
if (-not $OutDirectory) {
    $OutDirectory = Join-Path (Join-Path $PSScriptRoot 'results') ('scenarios-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDirectory)
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null

$copies = Initialize-ForkCopy $repo $PSScriptRoot
if ($ServerGc) { $copies.forkBin = Initialize-GcForkCopy $copies.forkBin }
Set-ForkOptimizations $ReferencesFirst $EvaluateAhead $EvaluateAheadSolution $SharedSdkCache
$sides = [ordered]@{ original = (Join-Path $copies.originalBin 'BuildHostFork.dll'); fork = (Join-Path $copies.forkBin 'BuildHostFork.dll') }
$brokenBin = Join-Path $PSScriptRoot 'bin-fork-broken'
if (Test-Path -LiteralPath $brokenBin) { Remove-Item -LiteralPath $brokenBin -Recurse -Force }
Copy-Item -LiteralPath $copies.forkBin -Destination $brokenBin -Recurse
Remove-Item -LiteralPath (Join-Path (Join-Path $brokenBin 'BuildHost-netcore') 'Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll')

function Write-Utf8([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

# The fixture may sit under the repository: empty Directory.Build files stop the repository's own props, and
# BuildHostFork.csproj keeps results/ out of its own compilation.
function New-Fixture([string]$Directory) {
    $sdkProject = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework{0}>{1}</TargetFramework{0}></PropertyGroup>{2}</Project>'
    Write-Utf8 (Join-Path $Directory 'Directory.Build.props') '<Project />'
    Write-Utf8 (Join-Path $Directory 'Directory.Build.targets') '<Project />'
    Write-Utf8 (Join-Path $Directory 'Ok/Ok.csproj') ($sdkProject -f '', 'net8.0', '')
    Write-Utf8 (Join-Path $Directory 'Ok/Class1.cs') "namespace Ok;`npublic class Class1 { }`n"
    $okReference = '<ItemGroup><ProjectReference Include="../Ok/Ok.csproj" /></ItemGroup>'
    Write-Utf8 (Join-Path $Directory 'MultiTfm/MultiTfm.csproj') ($sdkProject -f 's', 'net10.0;net8.0', ($okReference + '<Import Project="MultiTfm.props" /><Import Project="MultiTfm.props" />'))
    Write-Utf8 (Join-Path $Directory 'MultiTfm/MultiTfm.props') '<Project />'
    Write-Utf8 (Join-Path $Directory 'MultiTfm/Class1.cs') "namespace MultiTfm;`n#if NET10_0`npublic class Class1 : Ok.Class1 { }`n#else`npublic class Class1 { }`n#endif`n"
    $noisyBody = '<Import Project="Noisy.props" /><Import Project="Noisy.props" /><Target Name="Noise" BeforeTargets="CoreCompile"><Warning Text="Noisy build warning" /></Target><Target Name="PathNoise" AfterTargets="GetTargetPath"><Warning Text="Noisy target path warning" /></Target>'
    Write-Utf8 (Join-Path $Directory 'Noisy/Noisy.csproj') ($sdkProject -f '', 'net8.0', $noisyBody)
    Write-Utf8 (Join-Path $Directory 'Noisy/Noisy.props') '<Project />'
    Write-Utf8 (Join-Path $Directory 'Noisy/Class1.cs') "namespace Noisy;`npublic class Class1 { }`n"
    $consumerReferences = '<ItemGroup><ProjectReference Include="../MultiTfm/MultiTfm.csproj" /><ProjectReference Include="../Noisy/Noisy.csproj" /></ItemGroup>'
    Write-Utf8 (Join-Path $Directory 'Consumer/Consumer.csproj') ($sdkProject -f '', 'net10.0', $consumerReferences)
    Write-Utf8 (Join-Path $Directory 'Consumer/Class1.cs') "namespace Consumer;`npublic class Class1 : MultiTfm.Class1 { }`n"
    # References only Consumer: its build reaches MultiTfm and Noisy through the transitive references.
    Write-Utf8 (Join-Path $Directory 'Outer/Outer.csproj') ($sdkProject -f '', 'net10.0', '<ItemGroup><ProjectReference Include="../Consumer/Consumer.csproj" /></ItemGroup>')
    Write-Utf8 (Join-Path $Directory 'Outer/Class1.cs') "namespace Outer;`npublic class Class1 : Consumer.Class1 { }`n"
    $absentReference = '<ItemGroup><ProjectReference Include="../Absent/Absent.csproj" /></ItemGroup>'
    Write-Utf8 (Join-Path $Directory 'DanglingReference/DanglingReference.csproj') ($sdkProject -f '', 'net10.0', $absentReference)
    Write-Utf8 (Join-Path $Directory 'DanglingReference/Class1.cs') "namespace DanglingReference;`npublic class Class1 { }`n"
    # Not restored: the server has no F# services, the workspace keeps it as a project without documents.
    Write-Utf8 (Join-Path $Directory 'FSharp/FSharp.fsproj') ($sdkProject -f '', 'net10.0', '<ItemGroup><Compile Include="Library.fs" /></ItemGroup>')
    Write-Utf8 (Join-Path $Directory 'FSharp/Library.fs') "module Library`nlet value = 1`n"
    # The workspace rejects it by extension before any build.
    Write-Utf8 (Join-Path $Directory 'Unsupported/Unsupported.vcxproj') '<Project><Target Name="Build" /></Project>'
    Write-Utf8 (Join-Path $Directory 'Broken/Broken.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
    $projects = 'Outer/Outer.csproj', 'Consumer/Consumer.csproj', 'Ok/Ok.csproj', 'MultiTfm/MultiTfm.csproj', 'DanglingReference/DanglingReference.csproj', 'FSharp/FSharp.fsproj', 'Unsupported/Unsupported.vcxproj', 'Broken/Broken.csproj', 'Missing/Missing.csproj'
    $solution = Join-Path $Directory 'Scenarios.slnx'
    Write-Utf8 $solution ("<Solution>`n" + (($projects | ForEach-Object { "  <Project Path=`"$_`" />" }) -join "`n") + "`n</Solution>`n")
    # Only the projects that can be restored; the dangling reference fails restore and stays unrestored on purpose.
    foreach ($project in 'Ok/Ok.csproj', 'MultiTfm/MultiTfm.csproj', 'Noisy/Noisy.csproj', 'Consumer/Consumer.csproj', 'Outer/Outer.csproj') {
        Invoke-Native 'dotnet' @('restore', (Join-Path $Directory $project), '-nologo', '-v', 'q') "Fixture restore failed: $project"
    }
    return $solution
}

# $Expect: completed, canceled, killed (an error, or completed by a new BuildHost) or no-host (the BuildHost dll is missing).
function Invoke-Scenario([string]$Label, [string]$Side, [string]$Dll, [string]$Mode, [string]$Target, [int]$Delay, [string]$Expect, [string]$File) {
    Write-Host $Label
    $output = Join-Path $OutDirectory "$File.json"
    $exitCode = Invoke-Spike $Dll @($Mode, $Target, $output, "$Delay") (Join-Path $OutDirectory "$File.out.txt") ($Side -ne 'original')
    $result = $null
    if (Test-Path -LiteralPath $output) { $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json }
    return [pscustomobject]@{
        Label = $Label; Side = $Side; Mode = $Mode; Delay = $Delay; Expect = $Expect; External = ($Target -eq $Solution -and $external)
        ExitCode = $exitCode; Result = $result; Problems = (New-Object Collections.Generic.List[string]); Note = ''
    }
}

# How the client reports a BuildHost that went away during a request (the RPC client, or the pipe on Linux and
# Windows; a kill in the middle of writing a response leaves the client a truncated line it cannot deserialize).
# Any other error after a kill is a problem: it is not explained by the kill.
$connectionLost = '^failed: (System\.Exception: The server disconnected unexpectedly\.|System\.IO\.IOException: (Connection reset by peer|Broken pipe|Pipe is broken\.|The pipe has been ended\.)|System\.AggregateException: Failed to deserialize response from build host:)'

# What a single run must show for its scenario; $Undisturbed is the undisturbed load of the same side, the measure
# of a load completed after a kill. Processes left after the load are judged per pair, not here.
function Test-Run($Run, $Undisturbed) {
    $r = $Run.Result
    if ($null -eq $r) { $Run.Problems.Add("no report (exit code $($Run.ExitCode))"); return }
    $outcomeOk = switch ($Run.Expect) {
        'completed' { $r.Outcome -eq 'completed' }
        'canceled' { $r.Outcome -eq 'canceled' }
        'killed' { $r.Outcome -eq 'completed' -or $r.Outcome -match $connectionLost }
        'no-host' { $r.Outcome -like 'failed:*could not be found*' }
    }
    if (-not $outcomeOk) { $Run.Problems.Add("expected $($Run.Expect)") }
    if ($Run.Expect -eq 'killed' -and $r.Outcome -eq 'completed') { Test-CompletedAfterKill $Run $Undisturbed }
    if ($Run.Mode -ne 'load' -and $null -eq $r.DisturbedAtMs) {
        $Run.Problems.Add('not disturbed: the load ended first, inconclusive; use a larger solution or -DelayMs')
    }
    $netcore = @(@($r.BuildHostProcesses.hosts) | Where-Object { $_ -like 'netcore: *' })
    if ($Run.Expect -eq 'no-host') {
        if ($r.BuildHostProcesses.count -ne 0) { $Run.Problems.Add('a BuildHost started without the fork dll') }
    }
    elseif ($netcore.Count -eq 0) { $Run.Problems.Add('no netcore BuildHost observed') }
    foreach ($hostFolder in $netcore) {
        if ($hostFolder -ne $r.ExpectedHost) { $Run.Problems.Add("BuildHost from another folder: $hostFolder") }
    }
    if ($r.BuildHostProcesses.unidentified -gt 0) { $Run.Problems.Add("$($r.BuildHostProcesses.unidentified) child dotnet process(es) not identified") }
}

# A load that completed after its BuildHost was killed went on with a new host, so it saw one more host than the
# undisturbed load of its side, and it must have loaded the same workspace. The client reports the exit of the
# killed host as one workspace failure; apart from that the diagnostics must be those of the undisturbed load.
function Test-CompletedAfterKill($Run, $Undisturbed) {
    $r = $Run.Result
    $u = $Undisturbed.Result
    if ($null -eq $u -or $u.Outcome -ne 'completed') { $Run.Problems.Add('completed after the kill, but no undisturbed load of this side to compare with'); return }
    if ($r.BuildHostProcesses.count -le $u.BuildHostProcesses.count) { $Run.Problems.Add('completed after the kill without a new BuildHost') }
    if ($r.Projects -ne $u.Projects -or $r.Documents -ne $u.Documents) { $Run.Problems.Add('completed after the kill with other projects or documents than the undisturbed load') }
    $exitPattern = '^Failure: \[BuildHost PID \d+\] The BuildHost process exited'
    $exits = @($r.Diagnostics | Where-Object { $_ -match $exitPattern })
    $rest = @($r.Diagnostics | Where-Object { $_ -notmatch $exitPattern })
    if ($exits.Count -ne 1) { $Run.Problems.Add("completed after the kill with $($exits.Count) BuildHost exit failures, expected 1") }
    if (($rest -join "`n") -ne (@($u.Diagnostics) -join "`n")) { $Run.Problems.Add('completed after the kill with other diagnostics than the undisturbed load') }
}

# The fork must do what the original does. After a kill the outcome depends on timing in upstream, so one run per
# side cannot be compared; each completed one is compared with the undisturbed load of its side instead, and those
# two are compared with each other. A BuildHost left on both sides by the early cancel is upstream behavior,
# reported only; left by a cancel at -DelayMs, it means the cancel landed in the same setup window, which makes
# that scenario a second early cancel instead of a cancel during the load.
function Test-Pair($Original, $Fork, [bool]$CompareOutcome, [bool]$CompareDiagnostics) {
    $o = $Original.Result
    $f = $Fork.Result
    if ($null -eq $o -or $null -eq $f) { return }
    if ($CompareOutcome -and $o.Outcome -ne $f.Outcome) { $Fork.Problems.Add('outcome differs from the original') }
    if ($CompareOutcome -and ($o.Projects -ne $f.Projects -or $o.Documents -ne $f.Documents)) { $Fork.Problems.Add('projects or documents differ from the original') }
    if ($CompareDiagnostics -and (($o.Diagnostics -join "`n") -ne ($f.Diagnostics -join "`n"))) { $Fork.Problems.Add('diagnostics differ from the original') }
    if ($o.RemainingBuildHosts -ne $f.RemainingBuildHosts) {
        $Fork.Problems.Add("BuildHost processes left: fork $($f.RemainingBuildHosts), original $($o.RemainingBuildHosts)")
    }
    elseif ($o.RemainingBuildHosts -gt 0 -and $Original.Mode -eq 'cancel' -and $Original.Delay -gt 0) {
        $Fork.Problems.Add('the cancel left a BuildHost on both sides: it landed while the client set the host up, inconclusive; use a larger -DelayMs')
    }
    elseif ($o.RemainingBuildHosts -gt 0) {
        $note = "$($o.RemainingBuildHosts) BuildHost left on both sides (upstream)"
        $Original.Note = $note
        $Fork.Note = $note
    }
}

Write-Host 'Fixture'
$fixture = Join-Path $OutDirectory 'fixture'
$fixtureSolution = New-Fixture $fixture
$fixtureResults = Join-Path $OutDirectory 'fixture-runs'
New-Item -ItemType Directory -Force -Path $fixtureResults | Out-Null
Invoke-ObserveRounds $sides $fixtureSolution 'Class1' 3 $fixtureResults
& dotnet $sides['original'] compare $fixtureResults | Out-Host
$fixtureEquivalent = $LASTEXITCODE -eq 0

Write-Host "Restoring $Solution"
Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed'
$broken = Join-Path $brokenBin 'BuildHostFork.dll'
$fixtureLoads = @(
    (Invoke-Scenario 'fixture load, original' 'original' $sides['original'] 'load' $fixtureSolution 0 'completed' 'fixture-load-original'),
    (Invoke-Scenario 'fixture load, fork' 'fork' $sides['fork'] 'load' $fixtureSolution 0 'completed' 'fixture-load-fork'),
    (Invoke-Scenario 'fixture load, fork without its BuildHost dll' 'broken' $broken 'load' $fixtureSolution 0 'no-host' 'broken-fork')
)
$loads = @(
    (Invoke-Scenario 'load, original' 'original' $sides['original'] 'load' $Solution 0 'completed' 'load-original'),
    (Invoke-Scenario 'load, fork' 'fork' $sides['fork'] 'load' $Solution 0 'completed' 'load-fork')
)
$delaySource = "-DelayMs"
if (-not $PSBoundParameters.ContainsKey('DelayMs')) {
    $probe = $loads[0].Result
    if ($null -eq $probe -or $probe.Outcome -ne 'completed' -or $null -eq $probe.FirstHostAtMs) { throw 'The undisturbed load of the original failed; pass -DelayMs.' }
    $DelayMs = [int][Math]::Floor(($probe.LoadMs - $probe.FirstHostAtMs) / 2)
    $delaySource = 'half of the undisturbed original load after its BuildHost appeared'
}
$disturbed = @(
    (Invoke-Scenario 'cancel, original' 'original' $sides['original'] 'cancel' $Solution $DelayMs 'canceled' 'cancel-original'),
    (Invoke-Scenario 'cancel, fork' 'fork' $sides['fork'] 'cancel' $Solution $DelayMs 'canceled' 'cancel-fork'),
    (Invoke-Scenario 'kill BuildHost, original' 'original' $sides['original'] 'kill-host' $Solution $DelayMs 'killed' 'kill-original'),
    (Invoke-Scenario 'kill BuildHost, fork' 'fork' $sides['fork'] 'kill-host' $Solution $DelayMs 'killed' 'kill-fork'),
    (Invoke-Scenario 'cancel at BuildHost start, original' 'original' $sides['original'] 'cancel' $Solution 0 'canceled' 'early-cancel-original'),
    (Invoke-Scenario 'cancel at BuildHost start, fork' 'fork' $sides['fork'] 'cancel' $Solution 0 'canceled' 'early-cancel-fork')
)
$runs = @($fixtureLoads) + @($loads) + @($disturbed)
$undisturbed = @{ original = $loads[0]; fork = $loads[1] }
foreach ($run in $runs) { Test-Run $run $undisturbed[$run.Side] }
Test-Pair $fixtureLoads[0] $fixtureLoads[1] $true $true
Test-Pair $loads[0] $loads[1] $true $true
for ($i = 0; $i -lt $disturbed.Count; $i += 2) { Test-Pair $disturbed[$i] $disturbed[$i + 1] ($disturbed[$i].Mode -ne 'kill-host') $false }

# Paths in the summary are relative: the fixture, the spike folder, the solution folder and the user profile,
# in any letter case and with either separator.
$redactions = @(@($fixture, '<fixture>'), @($PSScriptRoot, '<spike>'), @((Split-Path $Solution), '<solution>'), @($HOME, '<user>'))
function Format-Redacted([string]$Text) {
    foreach ($pair in $redactions) {
        foreach ($variant in @($pair[0], $pair[0].Replace('\', '/'), $pair[0].Replace('/', '\'))) {
            $Text = [regex]::Replace($Text, [regex]::Escape($variant), $pair[1], [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        }
    }
    return $Text.Replace('|', '\|')
}

# An error message about a solution outside the repository may name its projects with or without a path: only
# the exception type is kept.
function Format-Outcome($Run) {
    $text = Format-Redacted $Run.Result.Outcome
    if ($Run.External -and $text -match '^failed: ([^:]+):') {
        return "failed: $($Matches[1]) (message not shown)"
    }
    return $text
}

$problems = @($runs | Where-Object { $_.Problems.Count -gt 0 })
$solutionName = Split-Path $Solution -Leaf
if ($external) { $solutionName = '(outside this repository, name not shown)' }
$md = New-Object Text.StringBuilder
[void]$md.AppendLine('# BuildHost fork failure scenarios (BuildHost/task-00)')
[void]$md.AppendLine('')
[void]$md.AppendLine("- fork: $($copies.version); references first (BuildHost/task-02 A): $([bool]$ReferencesFirst); evaluate ahead (B): $(if ($EvaluateAhead -gt 0) { "$EvaluateAhead workers, solution entries: $EvaluateAheadSolution" } else { 'off' }); solution for load, cancel and kill: $solutionName")
[void]$md.AppendLine("- experimental server GC with ConserveMemory=9 in netcore host only: $([bool]$ServerGc)")
[void]$md.AppendLine("- shared SDK resolution cache per project (BuildHost/task-04 C): $([bool]$SharedSdkCache)")
[void]$md.AppendLine("- delay after the first BuildHost: $DelayMs ms ($delaySource); early cancel: 0 ms")
[void]$md.AppendLine('- fixture observe, warmup + 3 rounds: ' + (((Get-Content -LiteralPath (Join-Path $fixtureResults 'summary.md')) -match '^- (fork rounds|result|diagnostics in report order)') -replace '^- ', '' -join '; '))
$resultText = 'all checks passed'
if ($problems.Count -gt 0 -or -not $fixtureEquivalent) { $resultText = "FAILED: $($problems.Count) run(s) with problems; fixture equivalent: $fixtureEquivalent" }
[void]$md.AppendLine("- result: **$resultText**")
[void]$md.AppendLine('')
[void]$md.AppendLine('| scenario | outcome | host seen at ms | disturbed at ms | load ms | projects | failures / warnings | BuildHosts | left after load | check |')
[void]$md.AppendLine('|---|---|---|---|---|---|---|---|---|---|')
foreach ($run in $runs) {
    $check = 'ok'
    if ($run.Problems.Count -gt 0) { $check = 'FAIL: ' + ($run.Problems -join '; ') }
    elseif ($run.Note) { $check = $run.Note }
    $r = $run.Result
    if ($null -eq $r) {
        [void]$md.AppendLine("| $($run.Label) | no report | | | | | | | | $(Format-Redacted $check) |")
        continue
    }
    $failures = @($r.Diagnostics | Where-Object { $_ -like 'Failure:*' }).Count
    $warnings = @($r.Diagnostics | Where-Object { $_ -like 'Warning:*' }).Count
    $hosts = "$($r.BuildHostProcesses.count): " + (@($r.BuildHostProcesses.hosts) -join '; ')
    [void]$md.AppendLine("| $($run.Label) | $(Format-Outcome $run) | $(Format-Number $r.FirstHostAtMs) | $(Format-Number $r.DisturbedAtMs) | $(Format-Number $r.LoadMs) | $($r.Projects) | $failures / $warnings | $(Format-Redacted $hosts) | $($r.RemainingBuildHosts) | $(Format-Redacted $check) |")
}
[void]$md.AppendLine('')
[void]$md.AppendLine('Fixture diagnostics (fork load; the check above compares them with the original load line by line):')
[void]$md.AppendLine('')
if ($null -ne $fixtureLoads[1].Result) {
    foreach ($diagnostic in $fixtureLoads[1].Result.Diagnostics) { [void]$md.AppendLine('- ' + (Format-Redacted $diagnostic)) }
}
Write-Utf8 (Join-Path $OutDirectory 'scenarios.md') $md.ToString()

Write-Host ''
Write-Host $md.ToString()
Write-Host "Results: $OutDirectory"
Write-Host 'scenarios.md is enough for the analysis; the JSON files hold paths and diagnostics of the solution.'
if ($problems.Count -gt 0 -or -not $fixtureEquivalent) { exit 1 }
