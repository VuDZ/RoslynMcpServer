<#
.SYNOPSIS
Runs every measurement of TestImpactIlProbe on one solution and collects the summaries that can be shared.

.DESCRIPTION
The only required input is the path to a solution. The script builds the probe and the solution, reads the
dependency graph (measure), collects coverage of the whole test run (coverage) and of a random part of the
test classes one by one (class-coverage, recall), and copies the summaries into <Out>\share. Each test
assembly is also run once with no test selected, so that recall can count apart the files the search for
tests executes.

Only the share directory holds counts without names. Everything else under <Out> (raw files, logs, reports
per class) names files, types and tests and must stay on this machine.

.PARAMETER Solution
Path to the solution or project to measure.

.PARAMETER Root
Directory with the sources. By default the root of the git repository of the solution, or its directory.

.PARAMETER Out
Directory for the results. By default il-probe-runs\<solution>-<time> in the user profile.

.PARAMETER ClassSample
How many test classes to run one by one; 0 runs all of them. One class takes several seconds.

.PARAMETER Runner
How one test class is run: vstest, xunit3 or mtp. By default the script tries them in this order.

.PARAMETER Collector
dotnet-coverage, or vstest for the collector built into VSTest. By default dotnet-coverage is used and
installed into <Out>\tools if it is missing; if that fails, the built-in collector is used.

.PARAMETER SkipBuild
Do not build the solution: use the Debug output that is already on disk. It must have portable PDBs.

.PARAMETER SkipTests
Only read the graph; run no tests.

.EXAMPLE
.\Measure-Solution.ps1 C:\src\Shop\Shop.sln
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Solution,
    [string]$Root,
    [string]$Out,
    [int]$ClassSample = 400,
    [ValidateSet('auto', 'vstest', 'xunit3', 'mtp')][string]$Runner = 'auto',
    [ValidateSet('auto', 'dotnet-coverage', 'vstest')][string]$Collector = 'auto',
    [switch]$SkipBuild,
    [switch]$SkipTests
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$steps = New-Object System.Collections.Generic.List[object]

# Runs a program, shows its output and keeps it in a log. Windows PowerShell turns every line a program
# writes to stderr into an error, and with 'Stop' the first of them would end the script, so the
# preference is relaxed for the call and the lines are taken as text.
function Invoke-Logged {
    param([string]$Name, [string]$Program, [string[]]$Arguments, [string]$WorkingDirectory)

    $log = Join-Path $logs ($Name + '.log')
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host ''
    Write-Host ('=== ' + $Name) -ForegroundColor Cyan
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    if ($WorkingDirectory) { Push-Location $WorkingDirectory }
    try {
        & $Program @Arguments 2>&1 | ForEach-Object { $line = "$_"; Write-Host $line; $line } | Set-Content -Path $log -Encoding UTF8
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($WorkingDirectory) { Pop-Location }
        $ErrorActionPreference = $previous
    }

    $steps.Add([pscustomobject]@{ step = $Name; exitCode = $exitCode; seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1) })
    return $exitCode
}

function Invoke-Probe {
    param([string]$Name, [string[]]$Arguments)
    return Invoke-Logged -Name $Name -Program 'dotnet' -Arguments (@($probe) + $Arguments)
}

function Find-CoverageAdapter {
    $packages = $env:NUGET_PACKAGES
    if (-not $packages) { $packages = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
    $versions = Join-Path $packages 'microsoft.codecoverage'
    if (-not (Test-Path $versions)) { return $null }
    $adapter = Get-ChildItem -Path $versions -Directory |
        Sort-Object -Property Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'build/netstandard2.0' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    return $adapter
}

# One trial of three test classes: a runner fits when at least one of them executed a source file.
# Lines of classes start with "[1/3]". The idle run of a test assembly, "[idle]", does not count: it
# executes files even when the runner then selects no test of the class.
function Test-ClassRunner {
    param([string]$Name, [string[]]$RunnerArguments)

    $code = Invoke-Probe -Name ('class-coverage-trial-' + $Name) -Arguments ($classArguments + $RunnerArguments + @('--limit', '3'))
    $log = Join-Path $logs ('class-coverage-trial-' + $Name + '.log')
    $executed = @(Select-String -Path $log -Pattern '^\[\d+/\d+\] .*: ([1-9]\d*) files executed')
    if ($code -eq 0 -and $executed.Count -gt 0) { return $true }
    if (Test-Path $classes) { Remove-Item -Path $classes -Recurse -Force }
    return $false
}

# --- Inputs ---------------------------------------------------------------------------------------------

$Solution = (Resolve-Path -Path $Solution).Path
$solutionDirectory = Split-Path -Path $Solution -Parent
if (-not $Root) {
    $Root = $solutionDirectory
    if (Get-Command git -ErrorAction SilentlyContinue) {
        # Outside a repository git writes to stderr, which must not end the script.
        $ErrorActionPreference = 'Continue'
        $top = & git -C $solutionDirectory rev-parse --show-toplevel 2>$null
        $ErrorActionPreference = 'Stop'
        if ($LASTEXITCODE -eq 0 -and $top) { $Root = "$top" }
    }
}
$Root = (Resolve-Path -Path $Root).Path
if (-not $Out) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($Solution) + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    $Out = Join-Path (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'il-probe-runs') $name
}
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$Out = (Resolve-Path -Path $Out).Path
$logs = Join-Path $Out 'logs'
$share = Join-Path $Out 'share'
$classes = Join-Path $Out 'classes'
New-Item -ItemType Directory -Path $logs, $share -Force | Out-Null

Write-Host ('Solution: ' + $Solution)
Write-Host ('Sources:  ' + $Root)
Write-Host ('Results:  ' + $Out)

# --- The probe and the solution ---------------------------------------------------------------------------

$probeProject = Join-Path $PSScriptRoot 'TestImpactIlProbe.csproj'
if ((Invoke-Logged -Name 'build-probe' -Program 'dotnet' -Arguments @('build', $probeProject, '-c', 'Release', '-v', 'q', '--nologo')) -ne 0) {
    throw 'The probe did not build. It needs the .NET 10 SDK.'
}
$probe = Join-Path $PSScriptRoot 'bin/Release/net10.0/TestImpactIlProbe.dll'

if (-not $SkipBuild) {
    # The probe reads portable PDBs only; a project that still asks for a Windows PDB gets a portable one.
    if ((Invoke-Logged -Name 'build-solution' -Program 'dotnet' -Arguments @('build', $Solution, '-c', 'Debug', '-p:DebugType=portable', '-v', 'm', '--nologo')) -ne 0) {
        throw 'The solution did not build with "dotnet build". Build it in Debug the usual way, with portable PDBs, and run the script again with -SkipBuild.'
    }
}

# --- Graph ------------------------------------------------------------------------------------------------

$measure = Join-Path $Out 'measure'
if ((Invoke-Probe -Name 'measure' -Arguments @('measure', '--root', $Root, '--sample', '40', '--seed', '1', '--out', $measure)) -ne 0) {
    throw 'measure found no test classes. Check that the Debug output is on disk under the sources and has portable PDBs.'
}
Copy-Item -Path (Join-Path $measure 'summary.json') -Destination (Join-Path $share 'summary.json')

$runnerUsed = $null
$collectorUsed = $null
if (-not $SkipTests) {
    # --- Collector ----------------------------------------------------------------------------------------

    $collectorUsed = $Collector
    if ($collectorUsed -ne 'vstest' -and -not (Get-Command dotnet-coverage -ErrorAction SilentlyContinue)) {
        $tools = Join-Path $Out 'tools'
        $installed = Invoke-Logged -Name 'install-dotnet-coverage' -Program 'dotnet' -Arguments @('tool', 'install', 'dotnet-coverage', '--tool-path', $tools)
        if ($installed -eq 0) {
            $env:PATH = $tools + [System.IO.Path]::PathSeparator + $env:PATH
        }
        elseif ($collectorUsed -eq 'dotnet-coverage') {
            throw 'dotnet-coverage is not installed and could not be installed. Install it, or pass -Collector vstest.'
        }
        else {
            Write-Warning 'dotnet-coverage could not be installed; using the collector built into VSTest.'
            $collectorUsed = 'vstest'
        }
    }
    if ($collectorUsed -eq 'auto') { $collectorUsed = 'dotnet-coverage' }

    # --- Coverage of the whole test run -------------------------------------------------------------------

    $coverage = Join-Path $Out 'coverage'
    New-Item -ItemType Directory -Path $coverage -Force | Out-Null
    if ($collectorUsed -eq 'dotnet-coverage') {
        $report = Join-Path $coverage 'coverage.cobertura.xml'
        $null = Invoke-Logged -Name 'test-run' -Program 'dotnet-coverage' -Arguments @('collect', '-f', 'cobertura', '-o', $report, '--', 'dotnet', 'test', $Solution, '-c', 'Debug', '--no-build')
    }
    else {
        $null = Invoke-Logged -Name 'test-run' -Program 'dotnet' -Arguments @('test', $Solution, '-c', 'Debug', '--no-build', '--collect', 'Code Coverage;Format=cobertura', '--results-directory', $coverage)
    }

    # Failed tests give a non-zero exit code and still leave a report, so the report decides.
    if (@(Get-ChildItem -Path $coverage -Filter '*.cobertura.xml' -Recurse).Count -gt 0) {
        $coverageOut = Join-Path $Out 'coverage-out'
        if ((Invoke-Probe -Name 'coverage' -Arguments @('coverage', '--root', $Root, '--coverage', $coverage, '--out', $coverageOut)) -eq 0) {
            Copy-Item -Path (Join-Path $coverageOut 'coverage-summary.json') -Destination (Join-Path $share 'coverage-summary.json')
        }
    }
    else {
        Write-Warning 'The test run left no Cobertura report; see logs\test-run.log.'
    }

    # --- Coverage per test class --------------------------------------------------------------------------

    $classArguments = @('class-coverage', '--root', $Root, '--out', $classes)
    if ($ClassSample -gt 0) { $classArguments += @('--sample', "$ClassSample", '--seed', '1') }

    $candidates = New-Object System.Collections.Specialized.OrderedDictionary
    if ($collectorUsed -eq 'dotnet-coverage') {
        foreach ($name in @('vstest', 'xunit3', 'mtp')) {
            if ($Runner -eq 'auto' -or $Runner -eq $name) { $candidates[$name] = @('--runner', $name) }
        }
    }
    else {
        $adapter = Find-CoverageAdapter
        if ($adapter) {
            # The line has quotes inside, which Windows PowerShell does not pass to a program intact, so it goes through a file.
            $line = 'dotnet test "{assembly}" --filter "{filter}" --collect "Code Coverage;Format=cobertura" --test-adapter-path "' + $adapter + '" --results-directory "{report}"'
            $template = Join-Path $Out 'class-run-template.txt'
            Set-Content -Path $template -Value $line -Encoding ASCII
            $candidates['vstest'] = @('--run', ('@' + $template))
        }
        else {
            Write-Warning 'The Microsoft.CodeCoverage package is not in the NuGet cache; coverage per test class is skipped.'
        }
    }

    foreach ($name in $candidates.Keys) {
        if (Test-ClassRunner -Name $name -RunnerArguments $candidates[$name]) { $runnerUsed = $name; break }
    }

    if ($runnerUsed) {
        $null = Invoke-Probe -Name 'class-coverage' -Arguments ($classArguments + $candidates[$runnerUsed])
        # The second pass runs only the classes that have no report yet.
        $null = Invoke-Probe -Name 'class-coverage-retry' -Arguments ($classArguments + $candidates[$runnerUsed])
        $recall = Join-Path $Out 'recall'
        if ((Invoke-Probe -Name 'recall' -Arguments @('recall', '--root', $Root, '--classes', $classes, '--out', $recall)) -eq 0) {
            Copy-Item -Path (Join-Path $recall 'recall-summary.json') -Destination (Join-Path $share 'recall-summary.json')
        }
    }
    elseif ($candidates.Count -gt 0) {
        Write-Warning 'No way of running one test class produced coverage; see logs\class-coverage-trial-*.log. recall is skipped.'
    }
}

# --- What can be shared -----------------------------------------------------------------------------------

# No paths and no names here: this file leaves the machine together with the summaries.
$info = [ordered]@{
    kind        = 'il-run-info'
    date        = (Get-Date -Format 'yyyy-MM-dd')
    dotnet      = (& dotnet --version)
    powershell  = $PSVersionTable.PSVersion.ToString()
    skipBuild   = [bool]$SkipBuild
    skipTests   = [bool]$SkipTests
    collector   = $collectorUsed
    classRunner = $runnerUsed
    classSample = $ClassSample
    steps       = $steps
}
$info | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $share 'run-info.json') -Encoding UTF8

Write-Host ''
Write-Host '=== Done' -ForegroundColor Cyan
$steps | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ('Share only this directory, it holds counts without names: ' + $share)
Get-ChildItem -Path $share | ForEach-Object { Write-Host ('  ' + $_.Name) }
Write-Host ('Everything else under ' + $Out + ' names files, types and tests: keep it on this machine.')
