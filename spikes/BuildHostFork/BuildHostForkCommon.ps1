# Helpers shared by the BuildHost fork scripts. Dot-source it after MatrixCommon.ps1; it defines functions and the
# fork-side environment below.

# Environment variables set only while a fork-side spike runs, such as the opt-in optimizations of BuildHost/task-02;
# the BuildHost inherits them from the client. Scripts fill it through Set-ForkOptimizations.
$script:ForkVariables = [ordered]@{}

# BuildHost/task-02 part A: the fork builds the referenced projects first (ROSLYN_BUILDHOST_REFERENCES_FIRST).
function Set-ForkOptimizations([bool]$ReferencesFirst) {
    $script:ForkVariables.Clear()
    if ($ReferencesFirst) { $script:ForkVariables['ROSLYN_BUILDHOST_REFERENCES_FIRST'] = '1' }
}

# Builds the fork and the spike, and makes bin-fork: the same spike and client, only BuildHost-netcore replaced
# by the fork. Returns the paths of both spike copies and the fork version.
function Initialize-ForkCopy([string]$Repo, [string]$SpikeDirectory) {
    $forkProject = Join-Path $Repo (Join-Path 'forks' (Join-Path 'roslyn-buildhost' (Join-Path 'build' (Join-Path 'BuildHost' 'Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj'))))
    $forkOutput = Join-Path (Split-Path $forkProject) (Join-Path 'bin' (Join-Path 'Release' 'net8.0'))
    $spikeProject = Join-Path $SpikeDirectory 'BuildHostFork.csproj'
    $originalBin = Join-Path $SpikeDirectory (Join-Path 'bin' (Join-Path 'Release' 'net10.0'))
    $forkBin = Join-Path $SpikeDirectory 'bin-fork'

    Write-Host 'Building the BuildHost fork (Release)'
    Invoke-Native 'dotnet' @('build', $forkProject, '-c', 'Release', '-nologo', '-v', 'q') 'Fork build failed'
    Write-Host 'Building the spike (Release)'
    Invoke-Native 'dotnet' @('build', $spikeProject, '-c', 'Release', '-nologo', '-v', 'q') 'Spike build failed'
    Write-Host 'Comparer self-checks'
    Invoke-Native 'dotnet' @((Join-Path $originalBin 'BuildHostFork.dll'), 'check') 'Comparer self-checks failed'

    if (Test-Path -LiteralPath $forkBin) { Remove-Item -LiteralPath $forkBin -Recurse -Force }
    Copy-Item -LiteralPath $originalBin -Destination $forkBin -Recurse
    $forkHostFolder = Join-Path $forkBin 'BuildHost-netcore'
    Remove-Item -LiteralPath $forkHostFolder -Recurse -Force
    New-Item -ItemType Directory -Path $forkHostFolder | Out-Null
    Get-ChildItem -LiteralPath $forkOutput -File | Copy-Item -Destination $forkHostFolder
    $version = (Get-Item -LiteralPath (Join-Path $forkHostFolder 'Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll')).VersionInfo.ProductVersion
    if ($version -notlike '*fork*') { throw "The fork copy does not hold the fork BuildHost: $version" }
    Write-Host "Fork BuildHost: $version"
    return [ordered]@{ originalBin = $originalBin; forkBin = $forkBin; version = $version }
}

# Runs observe for both sides in alternating order; round 0 is warmup. Writes <side>-round<N>.json and .out.txt.
# With $TraceRoot, the fork side writes its trace (BuildHost/task-01) to $TraceRoot/fork-round<N>; the original
# BuildHost cannot trace, so the original side stays the untraced reference for the comparison and the overhead.
function Invoke-ObserveRounds([Collections.IDictionary]$Sides, [string]$Solution, [string]$Name, [int]$Runs, [string]$OutDirectory, [string]$TraceRoot) {
    for ($round = 0; $round -le $Runs; $round++) {
        $order = @('original', 'fork')
        if ($round % 2 -eq 1) { $order = @('fork', 'original') }
        foreach ($side in $order) {
            $label = "$side round $round"
            if ($round -eq 0) { $label += ' (warmup)' }
            Write-Host $label
            $output = Join-Path $OutDirectory "$side-round$round.json"
            if ($TraceRoot -and $side -eq 'fork') {
                # A reused -OutDirectory still holds the trace of an earlier run; the report would read its host files too.
                $roundTrace = Join-Path $TraceRoot "fork-round$round"
                if (Test-Path -LiteralPath $roundTrace) { Remove-Item -LiteralPath $roundTrace -Recurse -Force }
                $env:ROSLYN_BUILDHOST_TRACE_DIRECTORY = $roundTrace
            }
            try { $exitCode = Invoke-Spike $Sides[$side] @('observe', $Solution, $output, $Name) (Join-Path $OutDirectory "$side-round$round.out.txt") ($side -eq 'fork') }
            finally { Remove-Item Env:ROSLYN_BUILDHOST_TRACE_DIRECTORY -ErrorAction SilentlyContinue }
            if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw "$label failed (exit code $exitCode); see $side-round$round.out.txt" }
        }
    }
}

# Runs a spike command with its output in a file and returns the exit code; a fork-side run gets $ForkVariables.
# Windows PowerShell 5.1 turns redirected stderr of a native command into a terminating error under
# ErrorActionPreference=Stop, and a failure scenario is expected to write there.
function Invoke-Spike([string]$Dll, [string[]]$Arguments, [string]$OutputFile, [bool]$Fork) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    if ($Fork) { foreach ($name in $script:ForkVariables.Keys) { Set-Item -LiteralPath "Env:$name" -Value $script:ForkVariables[$name] } }
    try {
        & dotnet $Dll @Arguments 2>&1 | ForEach-Object { "$_" } | Out-File -LiteralPath $OutputFile -Encoding utf8
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
        if ($Fork) { foreach ($name in $script:ForkVariables.Keys) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue } }
    }
}
