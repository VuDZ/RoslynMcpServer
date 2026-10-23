# Helpers shared by the matrix scripts of this spike. Dot-source it; it defines functions only.

function Invoke-Native([string]$FilePath, [string[]]$Arguments, [string]$Failure) {
    & $FilePath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Failure (exit code $LASTEXITCODE)" }
}

# First line of a git query, or $null outside Git or without git. Windows PowerShell 5.1 turns stderr of
# a native command into a terminating error under ErrorActionPreference=Stop even when it is redirected,
# so a solution kept in another version control system stopped the script.
function Get-GitOutput([string]$Directory, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git -C $Directory @Arguments 2>$null
        if ($LASTEXITCODE -ne 0 -or $null -eq $output) { return $null }
        return ([string](@($output)[0])).Trim()
    }
    catch { return $null }
    finally { $ErrorActionPreference = $previous }
}

function Get-Median($Values) {
    $sorted = @($Values | Where-Object { $null -ne $_ } | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $mid = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$mid] }
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
}

function Format-Number($Value, [int]$Digits = 0) {
    if ($null -eq $Value) { return 'n/a' }
    return ([Math]::Round([double]$Value, $Digits)).ToString([Globalization.CultureInfo]::InvariantCulture)
}

function Assert-PinnedSdk([string]$SolutionDirectory) {
    for ($dir = Get-Item -LiteralPath $SolutionDirectory; $null -ne $dir; $dir = $dir.Parent) {
        $globalJson = Join-Path $dir.FullName 'global.json'
        if (-not (Test-Path -LiteralPath $globalJson)) { continue }
        $json = Get-Content -LiteralPath $globalJson -Raw | ConvertFrom-Json
        if (-not $json.PSObject.Properties['sdk'] -or -not $json.sdk.PSObject.Properties['version']) { return }
        $pinned = [string]$json.sdk.version
        $installed = @(& dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] })
        if ($installed -notcontains $pinned) {
            throw ("The solution pins .NET SDK $pinned in $globalJson, but dotnet lists: $($installed -join ', '). " +
                "Install it (for example: winget install Microsoft.DotNet.SDK.10 --version $pinned) and run again.")
        }
        return $pinned
    }
    return $null
}

# Machine and input description written next to the results; $Parameters are the script's own settings.
function Get-Environment([string]$SolutionPath, [string]$Repo, [Collections.IDictionary]$Parameters) {
    $info = [ordered]@{
        solution = Split-Path $SolutionPath -Leaf
        solutionCommit = (Get-GitOutput (Split-Path $SolutionPath) @('rev-parse', 'HEAD'))
        spikeCommit = (Get-GitOutput $Repo @('rev-parse', '--short=12', 'HEAD'))
        logicalProcessors = [Environment]::ProcessorCount
        os = [Environment]::OSVersion.VersionString
        dotnetSdks = (@(& dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] }) -join ', ')
    }
    foreach ($key in $Parameters.Keys) { $info[$key] = $Parameters[$key] }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $os = Get-CimInstance Win32_OperatingSystem
        $info.cpu = "$($cpu.Name.Trim()) ($($cpu.NumberOfCores) cores)"
        $info.ramGb = [Math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    }
    return $info
}

# Build, self-check and restore before any measurement, so a broken stand fails in minutes, not after an hour.
function Initialize-Stand([string]$SpikeProject, [string]$SpikeDll, [string]$SolutionPath, [bool]$SkipRestore) {
    $pinned = Assert-PinnedSdk (Split-Path $SolutionPath)
    if ($pinned) { Write-Host "Solution pins .NET SDK ${pinned}: installed." }
    Write-Host 'Building the spike (Release)'
    Invoke-Native 'dotnet' @('build', $SpikeProject, '-c', 'Release', '-nologo', '-v', 'q') 'Spike build failed'
    Write-Host 'Self-checks'
    Invoke-Native 'dotnet' @($SpikeDll, 'check') 'Spike self-checks failed'
    if (-not $SkipRestore) {
        Write-Host "Restoring $SolutionPath"
        Invoke-Native 'dotnet' @('restore', $SolutionPath, '-nologo', '-v', 'q') 'Solution restore failed'
    }
}

function Write-HandoverCommands([string]$Repo, [string]$OutDirectory, [string]$Message) {
    Write-Host "Results: $OutDirectory"
    $relative = $OutDirectory
    if ($OutDirectory.StartsWith($Repo, [StringComparison]::OrdinalIgnoreCase)) { $relative = $OutDirectory.Substring($Repo.Length).TrimStart('\', '/') }
    Write-Host 'To hand the results over for analysis:'
    Write-Host "  git -C `"$Repo`" add `"$relative`""
    Write-Host "  git -C `"$Repo`" commit -m `"$Message`""
    Write-Host "  git -C `"$Repo`" push"
}
