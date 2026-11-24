# Helpers shared by the BuildHost fork scripts. Dot-source it after MatrixCommon.ps1; it defines functions and the
# fork-side environment below.

# Environment variables set only while a fork-side spike runs, such as the opt-in optimizations of BuildHost/task-02;
# the BuildHost inherits them from the client. Scripts fill it through Set-ForkOptimizations.
$script:ForkVariables = [ordered]@{}

# BuildHost/task-02 part A: the fork builds the referenced projects first (ROSLYN_BUILDHOST_REFERENCES_FIRST).
# Part B: worker threads evaluate ahead (ROSLYN_BUILDHOST_EVALUATE_AHEAD, the number of workers; 0 leaves it off),
# with the solution entries as ROSLYN_BUILDHOST_EVALUATE_AHEAD_SOLUTION says: all, a window, or off.
# BuildHost/task-04 candidate C: the evaluations of each project share one SDK resolution cache
# (ROSLYN_BUILDHOST_SHARED_SDK_CACHE).
function Set-ForkOptimizations([bool]$ReferencesFirst, [int]$EvaluateAhead = 0, [string]$EvaluateAheadSolution = 'all', [bool]$SharedSdkCache = $false) {
    $script:ForkVariables.Clear()
    if ($SharedSdkCache) { $script:ForkVariables['ROSLYN_BUILDHOST_SHARED_SDK_CACHE'] = '1' }
    if ($ReferencesFirst) { $script:ForkVariables['ROSLYN_BUILDHOST_REFERENCES_FIRST'] = '1' }
    if ($EvaluateAhead -gt 0) {
        $script:ForkVariables['ROSLYN_BUILDHOST_EVALUATE_AHEAD'] = [string]$EvaluateAhead
        $script:ForkVariables['ROSLYN_BUILDHOST_EVALUATE_AHEAD_SOLUTION'] = $EvaluateAheadSolution
    }
}

# Builds the fork and the spike, and makes bin-fork: the same spike and client, only BuildHost-netcore replaced
# by the fork. Returns the paths of both spike copies and the fork version.
function Initialize-ForkCopy([string]$Repo, [string]$SpikeDirectory) {
    $forkProject = Join-Path $Repo (Join-Path 'forks' (Join-Path 'roslyn-buildhost' (Join-Path 'build' (Join-Path 'BuildHost' 'Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj'))))
    $spikeProject = Join-Path $SpikeDirectory 'BuildHostFork.csproj'
    $forkBin = [IO.Path]::GetFullPath((Join-Path $SpikeDirectory 'bin-fork'))
    if ((Split-Path $forkBin -Parent) -ne [IO.Path]::GetFullPath($SpikeDirectory)) { throw "Unexpected fork copy path: $forkBin" }

    Push-Location -LiteralPath $Repo
    try {
        Write-Host 'Building the BuildHost fork (Release)'
        Invoke-Native 'dotnet' @('build', $forkProject, '-c', 'Release', '-nologo', '-v', 'q') 'Fork build failed'
        Write-Host 'Building the spike (Release)'
        Invoke-Native 'dotnet' @('build', $spikeProject, '-c', 'Release', '-nologo', '-v', 'q') 'Spike build failed'
        $forkDll = Get-ReleaseTargetPath $forkProject
        $spikeDll = Get-ReleaseTargetPath $spikeProject
    }
    finally { Pop-Location }
    $forkOutput = Split-Path $forkDll -Parent
    $originalBin = Split-Path $spikeDll -Parent
    $client = (Get-Item -LiteralPath (Join-Path $originalBin 'Microsoft.CodeAnalysis.Workspaces.MSBuild.dll')).VersionInfo.ProductVersion
    $stock = (Get-Item -LiteralPath (Join-Path $originalBin 'BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll')).VersionInfo.ProductVersion
    $stockFramework = (Get-Item -LiteralPath (Join-Path $originalBin 'BuildHost-net472/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.exe')).VersionInfo.ProductVersion
    $version = (Get-Item -LiteralPath $forkDll).VersionInfo.ProductVersion
    if ($client -cne $stock -or $client -cne $stockFramework -or $client -notmatch '^(?<package>[\d.]+)-[^+]+\+(?<revision>[0-9a-f]{40})$') {
        throw "Inconsistent Roslyn package output: client $client; netcore host $stock; net472 host $stockFramework"
    }
    if ($version -cne "$($Matches.package)-fork+$($Matches.revision)") {
        throw "BuildHost fork $version does not match the Roslyn client $client"
    }
    Write-Host 'Comparer self-checks'
    Invoke-Native 'dotnet' @($spikeDll, 'check') 'Comparer self-checks failed'
    Invoke-Native 'dotnet' @($spikeDll, 'rpc-check') 'RPC comparer self-checks failed'

    if (Test-Path -LiteralPath $forkBin) { Remove-Item -LiteralPath $forkBin -Recurse -Force }
    Copy-Item -LiteralPath $originalBin -Destination $forkBin -Recurse
    $forkHostFolder = [IO.Path]::GetFullPath((Join-Path $forkBin 'BuildHost-netcore'))
    if ((Split-Path $forkHostFolder -Parent) -ne $forkBin) { throw "Unexpected fork host path: $forkHostFolder" }
    Remove-Item -LiteralPath $forkHostFolder -Recurse -Force
    New-Item -ItemType Directory -Path $forkHostFolder | Out-Null
    Get-ChildItem -LiteralPath $forkOutput -File | Copy-Item -Destination $forkHostFolder
    Write-Host "Package client and stock hosts: $client"
    Write-Host "Fork BuildHost: $version"
    return [ordered]@{ originalBin = $originalBin; forkBin = $forkBin; version = $version; client = $client }
}

function Get-ReleaseTargetPath([string]$ProjectPath) {
    $output = @(& dotnet msbuild $ProjectPath '-nologo' '-p:Configuration=Release' '-getProperty:TargetPath')
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1 -or -not (Test-Path -LiteralPath $output[0] -PathType Leaf)) {
        throw "Could not resolve the built Release target of ${ProjectPath}: $output"
    }
    return $output[0]
}

# dotnet resolves the consumer global.json from its working directory.
function Restore-SpikeSolution([string]$Solution) {
    Write-Host "Restoring $Solution"
    Push-Location -LiteralPath (Split-Path $Solution -Parent)
    try { Invoke-Native 'dotnet' @('restore', $Solution, '-nologo', '-v', 'q') 'Solution restore failed' }
    finally { Pop-Location }
}

# Experimental task-04 runtime settings, confined to a separate netcore BuildHost copy. The client keeps its GC.
function Initialize-GcForkCopy([string]$ForkBin) {
    foreach ($name in @('DOTNET_gcServer', 'COMPlus_gcServer', 'DOTNET_GCConserveMemory', 'COMPlus_GCConserveMemory')) {
        if ($null -ne [Environment]::GetEnvironmentVariable($name)) { throw "Unset $name before the GC experiment: it can override runtimeconfig.json." }
    }
    $parent = Split-Path ([IO.Path]::GetFullPath($ForkBin)) -Parent
    $directory = [IO.Path]::GetFullPath((Join-Path $parent 'bin-fork-gc'))
    if ((Split-Path $directory -Parent) -ne $parent -or (Split-Path $directory -Leaf) -ne 'bin-fork-gc') { throw "Unexpected GC copy path: $directory" }
    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    Copy-Item -LiteralPath $ForkBin -Destination $directory -Recurse
    $configPath = Join-Path $directory 'BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.runtimeconfig.json'
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $config.runtimeOptions.configProperties | Add-Member -MemberType NoteProperty -Name 'System.GC.Server' -Value $true -Force
    $config.runtimeOptions.configProperties | Add-Member -MemberType NoteProperty -Name 'System.GC.ConserveMemory' -Value 9 -Force
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
    return $directory
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
    $previousVariables = @{}
    foreach ($name in 'ROSLYN_BUILDHOST_REFERENCES_FIRST', 'ROSLYN_BUILDHOST_EVALUATE_AHEAD', 'ROSLYN_BUILDHOST_EVALUATE_AHEAD_SOLUTION', 'ROSLYN_BUILDHOST_SHARED_SDK_CACHE') {
        $previousVariables[$name] = [Environment]::GetEnvironmentVariable($name)
        $value = if ($Fork) { $script:ForkVariables[$name] }
        [Environment]::SetEnvironmentVariable($name, $value)
    }
    try {
        & dotnet $Dll @Arguments 2>&1 | ForEach-Object { "$_" } | Out-File -LiteralPath $OutputFile -Encoding utf8
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
        foreach ($name in $previousVariables.Keys) { [Environment]::SetEnvironmentVariable($name, $previousVariables[$name]) }
    }
}
