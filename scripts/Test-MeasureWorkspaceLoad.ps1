# Exercises log parsing without starting a server; compatible with Windows PowerShell 5.1.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$measurementScript = Join-Path $PSScriptRoot 'Measure-WorkspaceLoad.ps1'
$parseTokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($measurementScript, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
foreach ($name in @('Get-RedactedText', 'ConvertTo-LogEntries', 'Get-StageEstimate')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
$pattern = $ast.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$linePattern' }, $true)
. ([scriptblock]::Create($pattern.Extent.Text))
$workspaceRoot = 'C:\Measurement Fixture'

function Assert-Equal($Expected, $Actual, [string]$Label) {
    if ($Expected -ne $Actual) { throw "${Label}: expected '$Expected', got '$Actual'" }
}

function New-Line([int]$Milliseconds, [string]$Message, [string]$Session = '[cli-test]') {
    $stamp = ([DateTimeOffset]'2026-10-22T20:00:00+03:00').AddMilliseconds($Milliseconds)
    return $stamp.ToString('yyyy-MM-dd HH:mm:ss.fff zzz') + " [INF] $Session $Message"
}

$dt = @{ status = 'incomplete'; projects = @(@{ project = "$workspaceRoot\App.csproj"; totalMs = 3000 }); replayFailures = @('replay_failed:1:InvalidDataException') } | ConvertTo-Json -Compress -Depth 5
$project = @{ searchId = 'first'; project = "$workspaceRoot\App.csproj"; name = 'App'; documents = 10; elapsedMs = 1000; outcome = 'success'; matches = 4 } | ConvertTo-Json -Compress
$summary = @{ searchId = 'first'; elapsedMs = 1010; projects = 1; completedProjects = 1; outcome = 'success' } | ConvertTo-Json -Compress
$lines = @(
    (New-Line 0 'MCP request initialize marker-test'),
    (New-Line 10 'MCP \u2190 request id=2 method=tools/call params={"name":"load_workspace"}'.Replace('\u2190', [string][char]0x2190)),
    (New-Line 100 'workspace_load_designtime {"status":"foreign"}' '[cli-other]'),
    (New-Line 1050 ('workspace_load_designtime ' + $dt)),
    (New-Line 1110 "Server method 'tools/call' request handler completed in 1100ms."),
    (New-Line 1200 'MCP \u2190 request id=3 method=tools/call params={"name":"find_symbol_definition"}'.Replace('\u2190', [string][char]0x2190)),
    (New-Line 2200 ('workspace_search_project ' + $project)),
    (New-Line 2210 ('workspace_search_summary ' + $summary)),
    (New-Line 2300 "Server method 'tools/call' request handler completed in 1100ms."),
    (New-Line 2400 'MCP \u2190 request id=4 method=tools/call params={"name":"load_workspace"}'.Replace('\u2190', [string][char]0x2190)),
    (New-Line 2410 "Server method 'tools/call' request handler completed in 10ms.")
)
$entries = @(ConvertTo-LogEntries $lines 'marker-test')
$load = Get-StageEstimate $entries 2
Assert-Equal 'incomplete' $load.designTime.status 'Design-time status'
Assert-Equal '<repo>\App.csproj' $load.designTime.projects[0].project 'Redacted design-time path'
Assert-Equal 1 $load.designTime.replayFailures.Count 'Replay failure retained'
Assert-Equal 0 $load.searchProjects.Count 'Request boundary'
$find = Get-StageEstimate $entries 3
Assert-Equal 1 $find.searchProjects.Count 'Search row count'
Assert-Equal '<repo>\App.csproj' $find.searchProjects[0].project 'Redacted search path'
Assert-Equal 90 $find.searchOutsideLoopMs 'Time outside search loop'
$ram = Get-StageEstimate $entries 4
Assert-Equal $null $ram.designTime 'RAM hit must not inherit physical load timings'
Assert-Equal 0 $ram.searchProjects.Count 'RAM hit must not inherit search timings'
$missing = Get-StageEstimate $entries 99
Assert-Equal $null $missing.designTime 'Missing record stays unavailable'
Assert-Equal $null $missing.searchOutsideLoopMs 'Missing search stays unavailable'
Write-Output 'PASS: session/request isolation, partial replay, project rows, path redaction, search remainder and missing records.'
