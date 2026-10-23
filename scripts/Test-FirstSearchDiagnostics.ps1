# Exercises the variant comparison without starting a server; compatible with Windows PowerShell 5.1.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$diagnosticsScript = Join-Path $PSScriptRoot 'Invoke-FirstSearchDiagnostics.ps1'
$parseTokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($diagnosticsScript, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
foreach ($name in @('Get-Median', 'Format-Value', 'New-Variant', 'Write-Comparison')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Equal($Expected, $Actual, [string]$Label) {
    if ($Expected -ne $Actual) { throw "${Label}: expected '$Expected', got '$Actual'" }
}

# $Prewarm: $null for a server before 1.5.15, otherwise the outcome logged in the load window.
function New-Run([string]$Kind, [double]$LoadMs, [double]$FindMs, $ServerGc, [double]$PauseMs, [double]$WorkingSetMb, $Prewarm = $null) {
    $search = [ordered]@{ searchId = 'x'; outcome = 'success'; elapsedMs = $FindMs }
    if ($null -ne $ServerGc) {
        $search.serverGc = $ServerGc
        $search.gcPauseMs = $PauseMs
        $search.gcCollections = @(1, 1, 0)
    }
    $load = [ordered]@{ state = 'ok'; clientMs = $LoadMs; workingSetMb = $WorkingSetMb; searchPrewarm = $null }
    if ($null -ne $Prewarm) { $load.searchPrewarm = [ordered]@{ outcome = $Prewarm; elapsedMs = 5000; projects = 2; completedProjects = 2 } }
    return [ordered]@{
        kind = $Kind
        load = $load
        find = [ordered]@{ state = 'ok'; clientMs = $FindMs; searchSummaries = @($search); searchPrewarm = $null }
    }
}

function Write-Runs([string]$Folder, $Runs) {
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $document = [ordered]@{ environment = @{}; runs = @($Runs) }
    [IO.File]::WriteAllText((Join-Path $Folder 'runs.json'), ($document | ConvertTo-Json -Depth 8))
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('first-search-diagnostics-test-' + [Guid]::NewGuid().ToString('N'))
try {
    $on = New-Variant 'server' 'on' 30
    $off = New-Variant 'server' 'off' 30
    $immediate = New-Variant 'server' 'on' 0
    Write-Runs (Join-Path $root $on.Name) @(
        (New-Run 'warmup' 1 99999 $true 9999 1 'success'),
        (New-Run 'measured' 30000 6000 $true 1000 6000 'success'),
        (New-Run 'measured' 32000 8000 $true 1200 6200 'success'),
        (New-Run 'measured' 31000 7000 $true 1100 6100 'cancelled'),
        (New-Run 'trace' 1 99999 $true 9999 1 'success'))
    Write-Runs (Join-Path $root $off.Name) @(
        (New-Run 'measured' 33000 14000 $true 1000 5000),
        (New-Run 'measured' 34000 14000 $true 1000 5200))
    Write-Runs (Join-Path $root $immediate.Name) @((New-Run 'measured' 30000 15000 $true 1000 6000 'success'))
    Write-Runs (Join-Path $root 'old') @((New-Run 'measured' 40000 30000 $null 0 4000))

    Write-Comparison $root @($on, $off, $immediate) | Out-Null
    $lines = @(Get-Content -LiteralPath (Join-Path $root 'comparison.md'))
    Assert-Equal '| server-prewarm-on-delay-30 | server | 2/3 | 3 | 31000 | 7000 | 0.5 | 1100 | 6100 |' $lines[6] 'Prewarm row against prewarm off at the same pause'
    Assert-Equal '| server-prewarm-off-delay-30 | server | 0/2 | 2 | 33500 | 14000 | 1 | 1000 | 5100 |' $lines[7] 'Prewarm-off baseline row'
    Assert-Equal '| server-prewarm-on-delay-0 | server | 1/1 | 1 | 30000 | 15000 | n/a | 1000 | 6000 |' $lines[8] 'No prewarm-off variant at this pause'

    Write-Comparison $root @([pscustomobject]@{ Name = 'old'; Gc = 'server'; Prewarm = 'off'; Delay = 0 }) | Out-Null
    $lines = @(Get-Content -LiteralPath (Join-Path $root 'comparison.md'))
    Assert-Equal '| old | n/a (server before 1.5.12) | 0/1 | 1 | 40000 | 30000 | 1 | n/a | 4000 |' $lines[6] 'Server without GC or prewarm fields'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
Write-Output 'PASS: variant comparison medians, prewarm-off baselines, prewarm outcomes, excluded warmup and trace runs, and older servers.'
