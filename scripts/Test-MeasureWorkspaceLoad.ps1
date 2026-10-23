# Exercises log parsing without starting a server; compatible with Windows PowerShell 5.1.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$measurementScript = Join-Path $PSScriptRoot 'Measure-WorkspaceLoad.ps1'
$parseTokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($measurementScript, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
foreach ($name in @('Get-RedactedText', 'ConvertTo-LogEntries', 'Get-StageEstimate', 'Format-Ms', 'Format-SearchSummaryLine', 'Format-SearchExceptionLines')) {
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
$summary = @{ searchId = 'first'; elapsedMs = 1010; projects = 1; completedProjects = 1; outcome = 'success'; serverGc = $true; gcPauseMs = 120.4; gcCollections = @(3, 1, 0) } | ConvertTo-Json -Compress
$exceptions = @{
    searchId = 'first'; total = 7; attributed = 6; unattributed = 1; distinctGroups = 2; droppedGroups = 0
    groups = @(
        @{ type = 'System.IO.IOException'; project = "$workspaceRoot\App.csproj"; detail = "The process cannot access the file '$workspaceRoot\obj\A|B.g.cs'"; count = 6; firstMs = 12.4 },
        @{ type = 'System.InvalidOperationException'; project = $null; detail = $null; count = 1; firstMs = 3 }
    )
} | ConvertTo-Json -Compress -Depth 5
$lines = @(
    (New-Line 0 'MCP request initialize marker-test'),
    (New-Line 10 'MCP \u2190 request id=2 method=tools/call params={"name":"load_workspace"}'.Replace('\u2190', [string][char]0x2190)),
    (New-Line 100 'workspace_load_designtime {"status":"foreign"}' '[cli-other]'),
    (New-Line 1050 ('workspace_load_designtime ' + $dt)),
    (New-Line 1110 "Server method 'tools/call' request handler completed in 1100ms."),
    (New-Line 1200 'MCP \u2190 request id=3 method=tools/call params={"name":"find_symbol_definition"}'.Replace('\u2190', [string][char]0x2190)),
    (New-Line 2200 ('workspace_search_project ' + $project)),
    (New-Line 2205 ('workspace_search_exceptions ' + $exceptions)),
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
Assert-Equal 1 $find.searchExceptions.Count 'Search exception summary count'
Assert-Equal '<repo>\App.csproj' $find.searchExceptions[0].groups[0].project 'Redacted exception project'
Assert-Equal 0 $load.searchExceptions.Count 'Exception summary stays with its request'
$exceptionLines = @(Format-SearchExceptionLines $find.searchExceptions)
Assert-Equal 'Search first first-chance exceptions: 7 total, 6 in searched projects, 1 elsewhere in the process; 2 groups, 0 dropped.' $exceptionLines[0] 'Exception headline'
Assert-Equal "| ``System.IO.IOException`` | ``<repo>\App.csproj`` | The process cannot access the file '<repo>\obj\A\|B.g.cs' | 6 | 12 |" $exceptionLines[4] 'Attributed exception row'
Assert-Equal '| `System.InvalidOperationException` | (none) |  | 1 | 3 |' $exceptionLines[5] 'Unattributed exception row'
Assert-Equal 'Search first: success, 1010 ms, 1/1 projects completed; server GC, pauses 120 ms, collections gen0/1/2 3/1/0.' (Format-SearchSummaryLine $find.searchSummaries[0]) 'Search line with GC'
$oldSummary = '{"searchId":"old","elapsedMs":5,"projects":2,"completedProjects":2,"outcome":"success"}' | ConvertFrom-Json
Assert-Equal 'Search old: success, 5 ms, 2/2 projects completed.' (Format-SearchSummaryLine $oldSummary) 'Search line before 1.5.12'
$oldServer = @(Format-SearchExceptionLines @())
Assert-Equal 'Search exception summary unavailable: server before 1.5.11 or no workspace_search_exceptions record.' $oldServer[0] 'Old server message'
$ram = Get-StageEstimate $entries 4
Assert-Equal $null $ram.designTime 'RAM hit must not inherit physical load timings'
Assert-Equal 0 $ram.searchProjects.Count 'RAM hit must not inherit search timings'
$missing = Get-StageEstimate $entries 99
Assert-Equal $null $missing.designTime 'Missing record stays unavailable'
Assert-Equal $null $missing.searchOutsideLoopMs 'Missing search stays unavailable'
Write-Output 'PASS: session/request isolation, partial replay, project rows, path redaction, search remainder, search GC, search exceptions and missing records.'
