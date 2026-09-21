<#
.SYNOPSIS
  Fail if the TRX run discovered no tests, failed, or skipped any case.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    throw "Test results directory not found: $ResultsDirectory"
}

$trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse -File)
if ($trxFiles.Count -eq 0) {
    throw "No .trx files under $ResultsDirectory"
}

$ns = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
$total = 0
$executed = 0
$passed = 0
$failed = 0
$notExecuted = 0

foreach ($file in $trxFiles) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
    $nsmgr = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $nsmgr.AddNamespace('t', $ns)
    $counters = $xml.SelectSingleNode('//t:ResultSummary/t:Counters', $nsmgr)
    if ($null -eq $counters) {
        throw "TRX is missing ResultSummary/Counters: $($file.FullName)"
    }

    $total += [int]$counters.GetAttribute('total')
    $executed += [int]$counters.GetAttribute('executed')
    $passed += [int]$counters.GetAttribute('passed')
    $failed += [int]$counters.GetAttribute('failed')
    $notExecuted += [int]$counters.GetAttribute('notExecuted')
}

Write-Host "TRX counters: total=$total executed=$executed passed=$passed failed=$failed notExecuted=$notExecuted"

if ($total -le 0) {
    throw 'No tests discovered. CI must run the full RoslynMcpServer.Tests assembly.'
}

if ($failed -gt 0) {
    throw "Failed tests: $failed (total=$total)."
}

if ($notExecuted -gt 0 -or $executed -lt $total) {
    throw @"
Skipped/not-executed tests: notExecuted=$notExecuted executed=$executed total=$total.
CI must run every discovered test (including AnalyzerLifecycle). Skips from AnalyzerLifecycleFact (MSBuild host unavailable) are a job failure, not a pass.
"@
}

if ($passed -ne $total) {
    throw "Passed ($passed) does not match total ($total)."
}

Write-Host "All $total discovered tests executed and passed."
