# Exercises the GC mode comparison without starting a server; compatible with Windows PowerShell 5.1.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$diagnosticsScript = Join-Path $PSScriptRoot 'Invoke-FirstSearchDiagnostics.ps1'
$parseTokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($diagnosticsScript, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
foreach ($name in @('Get-Median', 'Format-Value', 'Write-Comparison')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Equal($Expected, $Actual, [string]$Label) {
    if ($Expected -ne $Actual) { throw "${Label}: expected '$Expected', got '$Actual'" }
}

function New-Run([string]$Kind, [double]$LoadMs, [double]$FindMs, $ServerGc, [double]$PauseMs, [double]$WorkingSetMb) {
    $search = [ordered]@{ searchId = 'x'; outcome = 'success'; elapsedMs = $FindMs }
    if ($null -ne $ServerGc) {
        $search.serverGc = $ServerGc
        $search.gcPauseMs = $PauseMs
        $search.gcCollections = @(1, 1, 0)
    }
    return [ordered]@{
        kind = $Kind
        load = [ordered]@{ state = 'ok'; clientMs = $LoadMs }
        find = [ordered]@{ state = 'ok'; clientMs = $FindMs; workingSetMb = $WorkingSetMb; searchSummaries = @($search) }
    }
}

function Write-Runs([string]$Folder, $Runs) {
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $document = [ordered]@{ environment = @{}; runs = @($Runs) }
    [IO.File]::WriteAllText((Join-Path $Folder 'runs.json'), ($document | ConvertTo-Json -Depth 8))
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('first-search-diagnostics-test-' + [Guid]::NewGuid().ToString('N'))
try {
    Write-Runs (Join-Path $root 'server') @(
        (New-Run 'warmup' 1 99999 $true 9999 1),
        (New-Run 'measured' 30000 10000 $true 1000 6000),
        (New-Run 'measured' 32000 12000 $true 1200 6200),
        (New-Run 'measured' 31000 11000 $true 1100 6100),
        (New-Run 'trace' 1 99999 $true 9999 1))
    Write-Runs (Join-Path $root 'workstation') @(
        (New-Run 'measured' 33000 20000 $false 8000 5000),
        (New-Run 'measured' 34000 22000 $false 8200 5200))
    Write-Runs (Join-Path $root 'old') @((New-Run 'measured' 40000 30000 $null 0 4000))

    Write-Comparison $root @('server', 'workstation') | Out-Null
    $lines = @(Get-Content -LiteralPath (Join-Path $root 'comparison.md'))
    Assert-Equal '| server | server | 3 | 31000 | 11000 | 0.524 | 1100 | 6100 | 1.196 |' $lines[6] 'Server row'
    Assert-Equal '| workstation | workstation | 2 | 33500 | 21000 | 1 | 8100 | 5100 | 1 |' $lines[7] 'Workstation baseline row'

    Write-Comparison $root @('old') | Out-Null
    $lines = @(Get-Content -LiteralPath (Join-Path $root 'comparison.md'))
    Assert-Equal '| old | n/a (server before 1.5.12) | 1 | 40000 | 30000 | 1 | n/a | 4000 | 1 |' $lines[6] 'Server without GC fields'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
Write-Output 'PASS: GC mode comparison medians, ratios, excluded warmup and trace runs, and servers without GC fields.'
