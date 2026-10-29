#requires -Version 7.0
<#
.SYNOPSIS
    Checks the real MCP stdio server and its local usage records in an isolated fixture.
.DESCRIPTION
    Creates evidence under artifacts/usage-integration. Does not edit harness settings,
    publish binaries, or stop existing server processes.
#>
[CmdletBinding()]
param(
    [string]$ServerPath = (Join-Path $PSScriptRoot '../bin/Release/net10.0/RoslynMcpServer.dll'),
    [switch]$SkipAnalyzer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ServerPath = [IO.Path]::GetFullPath($ServerPath)
if (-not (Test-Path -LiteralPath $ServerPath -PathType Leaf)) { throw "Server binary not found: $ServerPath" }
$evidenceRoot = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/usage-integration'))) ([Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $evidenceRoot 'fixture'
[void][IO.Directory]::CreateDirectory($fixture)
$projectPath = Join-Path $fixture 'App.csproj'
[IO.File]::WriteAllText($projectPath, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $fixture 'UsageProbe.cs'), @'
namespace UsageIntegration;

public sealed class UsageProbe
{
    public int Value => 42;
}
'@)
@{ 'workspace-path' = $projectPath; configuration = 'Release' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'RoslynMcp.jsonc') -Encoding utf8NoBOM
$script:checks = 0
$privateMarker = 'private-argument-' + [Guid]::NewGuid().ToString('N')
$clients = [Collections.Generic.List[object]]::new()

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Start-Server([string]$Name, [string]$StatePath, [string]$UsageSetting = '', [switch]$DefaultLogDirectory) {
    if (-not (Test-Path -LiteralPath $StatePath)) { [void][IO.Directory]::CreateDirectory($StatePath) }
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    if ([IO.Path]::GetExtension($ServerPath) -eq '.dll') {
        $startInfo.FileName = (Get-Command dotnet -CommandType Application).Source
        $startInfo.ArgumentList.Add($ServerPath)
    }
    else { $startInfo.FileName = $ServerPath }
    $startInfo.WorkingDirectory = $fixture
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $startInfo.Environment['ROSLYN_MCP_HOME'] = $StatePath
    if ($DefaultLogDirectory) { [void]$startInfo.Environment.Remove('ROSLYN_MCP_LOG_DIR') }
    else { $startInfo.Environment['ROSLYN_MCP_LOG_DIR'] = Join-Path $StatePath 'logs' }
    $startInfo.Environment['ROSLYN_MCP_SEARCH_PREWARM'] = '0'
    $startInfo.Environment['MCP_LOG_INCOMING_RPC'] = '0'
    $startInfo.Environment['ROSLYN_MCP_TOOL_PROFILE'] = 'full'
    [void]$startInfo.Environment.Remove('ROSLYN_MCP_TOOL_GROUPS')
    [void]$startInfo.Environment.Remove('ROSLYN_MCP_WORKSPACE')
    if ($UsageSetting) { $startInfo.Environment['ROSLYN_MCP_USAGE'] = $UsageSetting }
    else { [void]$startInfo.Environment.Remove('ROSLYN_MCP_USAGE') }
    $process = [Diagnostics.Process]::Start($startInfo)
    $client = @{
        name = $Name; process = $process; stderr = $process.StandardError.ReadToEndAsync()
        pending = $null; next_id = 1; protocol_lines = 0; state = $StatePath
    }
    $clients.Add($client)
    return $client
}

function Send-Message($Client, $Message) {
    $Client.process.StandardInput.WriteLine(($Message | ConvertTo-Json -Depth 30 -Compress))
    $Client.process.StandardInput.Flush()
}

function Read-Message($Client, [DateTime]$Deadline) {
    if ($null -eq $Client.pending) { $Client.pending = $Client.process.StandardOutput.ReadLineAsync() }
    $remaining = [int][Math]::Max(1, ($Deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if (-not $Client.pending.Wait($remaining)) { throw "Timed out reading $($Client.name)." }
    $line = $Client.pending.GetAwaiter().GetResult()
    $Client.pending = $null
    if ($null -eq $line) { throw "Server $($Client.name) closed stdout before replying." }
    try { $message = ConvertFrom-Json -InputObject $line -AsHashtable }
    catch { throw "Server $($Client.name) polluted stdout with a non-JSON line." }
    Assert-True ($message.Contains('jsonrpc') -and $message.jsonrpc -eq '2.0') 'Every stdout line must be JSON-RPC 2.0.'
    $Client.protocol_lines++
    return $message
}

function Invoke-Request($Client, [string]$Method, $Parameters) {
    $requestId = $Client.next_id++
    Send-Message $Client @{ jsonrpc = '2.0'; id = $requestId; method = $Method; params = $Parameters }
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline) {
        $message = Read-Message $Client $deadline
        if ($message.Contains('method')) {
            if ($message.Contains('id')) {
                if ($message.method -eq 'ping') { Send-Message $Client @{ jsonrpc = '2.0'; id = $message.id; result = @{} } }
                else { Send-Message $Client @{ jsonrpc = '2.0'; id = $message.id; error = @{ code = -32601; message = 'Unsupported integration client request.' } } }
            }
            continue
        }
        if ($message.Contains('id') -and $message.id -eq $requestId) {
            Assert-True (-not $message.Contains('error')) "Unexpected JSON-RPC error for $Method."
            return $message.result
        }
    }
    throw "No response for $Method."
}

function Initialize-Client($Client) {
    $result = Invoke-Request $Client 'initialize' @{
        protocolVersion = '2025-11-25'; capabilities = @{}
        clientInfo = @{ name = 'usage-integration'; version = '1' }
    }
    Assert-True ($result.Contains('serverInfo')) 'Initialize must return server identity.'
    Send-Message $Client @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
}

function Invoke-Tool($Client, [string]$Name, $Arguments) {
    $result = Invoke-Request $Client 'tools/call' @{ name = $Name; arguments = $Arguments }
    Assert-True ($result.Contains('content')) "Tool $Name must return MCP content."
    return $result
}

function Stop-Server($Client) {
    $null = Invoke-Tool $Client 'stop_mcp_server' @{}
    Assert-True ($Client.process.WaitForExit(15000)) "Server $($Client.name) must stop gracefully."
    Assert-True ($Client.process.ExitCode -eq 0) "Server $($Client.name) must exit successfully."
    foreach ($line in @($Client.process.StandardOutput.ReadToEnd() -split '\r?\n' | Where-Object { $_ })) {
        try { $message = $line | ConvertFrom-Json -AsHashtable }
        catch { throw "Server $($Client.name) polluted trailing stdout." }
        Assert-True ($message.Contains('jsonrpc')) 'Trailing stdout must remain protocol-only.'
        $Client.protocol_lines++
    }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot ($Client.name + '.stderr.txt')), $Client.stderr.GetAwaiter().GetResult())
}

function Read-Usage([string]$StatePath) {
    $records = [Collections.Generic.List[object]]::new()
    $usagePath = Join-Path $StatePath 'usage'
    foreach ($file in @(Get-ChildItem -LiteralPath $usagePath -Filter 'usage-*.jsonl' -File -ErrorAction SilentlyContinue | Sort-Object Name)) {
        foreach ($line in [IO.File]::ReadLines($file.FullName)) {
            Assert-True ([Text.Encoding]::UTF8.GetByteCount($line + "`n") -le 4096) 'Each usage record must fit the size limit.'
            try { $record = $line | ConvertFrom-Json -AsHashtable }
            catch { throw "Usage writer emitted an invalid JSONL record in $($file.Name)." }
            foreach ($field in @('v', 'ts', 'proc', 'seq', 'pid', 'ppid', 'harness', 'ver', 'ev')) {
                Assert-True ($record.Contains($field)) "Usage record must contain $field."
            }
            Assert-True ($record.v -eq 1) 'Usage version must be 1.'
            # ConvertFrom-Json can convert ISO text to a local DateTime; check the wire value itself.
            $document = [Text.Json.JsonDocument]::Parse($line)
            try { $wireTimestamp = $document.RootElement.GetProperty('ts').GetString() }
            finally { $document.Dispose() }
            Assert-True ([DateTimeOffset]::Parse($wireTimestamp).Offset -eq [TimeSpan]::Zero) 'Usage timestamp must be UTC.'
            Assert-True (-not $line.Contains($privateMarker)) 'Usage must not contain tool argument or result text.'
            $records.Add($record)
        }
    }
    return ,$records.ToArray()
}

try {
    $normalState = Join-Path $evidenceRoot 'normal'
    $main = Start-Server 'main' $normalState
    Initialize-Client $main
    $null = Invoke-Tool $main 'get_tool_help' @{ toolName = 'load_workspace' }
    $null = Invoke-Tool $main 'get_tool_help' @{ toolName = $privateMarker }
    $peer = Start-Server 'peer' $normalState
    Initialize-Client $peer
    $null = Invoke-Tool $peer 'get_tool_help' @{ toolName = 'reset_workspace' }
    $semantic = Invoke-Tool $main 'find_symbol_definition' @{ symbolName = 'UsageProbe' }
    $semanticText = @($semantic.content | ForEach-Object { if ($_.Contains('text')) { $_.text } }) -join "`n"
    Assert-True ($semanticText.Contains('UsageProbe.cs')) 'The semantic call must load and find the fixture declaration.'
    $null = Invoke-Tool $main 'load_workspace' @{ workspacePath = $projectPath }
    $null = Invoke-Tool $main 'load_workspace' @{ workspacePath = $projectPath; configuration = 'Debug' }
    $null = Invoke-Tool $main 'reset_workspace' @{}
    $null = Invoke-Tool $main 'load_workspace' @{ workspacePath = (Join-Path $fixture 'Missing.csproj') }
    $null = Invoke-Tool $main 'load_workspace' @{ workspacePath = $projectPath; configuration = 'Release' }
    Stop-Server $peer
    Stop-Server $main

    $records = Read-Usage $normalState
    Assert-True ($records.Count -gt 0) 'Default collection must create records.'
    $groups = @($records | Group-Object proc)
    Assert-True ($groups.Count -eq 2) 'Two live servers must have separate process identities in the shared file.'
    foreach ($group in $groups) {
        $processRecords = @($group.Group | Sort-Object { [long]$_.seq })
        Assert-True ($processRecords[0].ev -eq 'start') 'The first process record must be start.'
        $expected = 1L
        foreach ($record in $processRecords) {
            Assert-True ([long]$record.seq -eq $expected) 'Normal collection must preserve contiguous process sequence numbers.'
            $expected++
        }
        Assert-True (@($processRecords | Where-Object { $_.ev -eq 'client' -and $_.name -eq 'usage-integration' }).Count -eq 1) 'Each initialized server must record its client.'
        Assert-True (@($processRecords | Where-Object { $_.ev -eq 'exit' -and $_.reason -eq 'stop_mcp_server' }).Count -eq 1) 'Each stopped server must record its stop reason.'
    }
    $starts = @($records | Where-Object { $_.ev -eq 'tool_start' })
    $ends = @($records | Where-Object { $_.ev -eq 'tool' })
    Assert-True ($starts.Count -eq $ends.Count) 'Every completed integration call must have start and end records.'
    foreach ($start in $starts) {
        Assert-True (@($ends | Where-Object { $_.proc -eq $start.proc -and $_.call -eq $start.call }).Count -eq 1) 'Tool correlation must be unique within its process.'
    }
    Assert-True (@($ends | Where-Object { $_.name -eq 'get_tool_help' -and $_.requires_workspace }).Count -eq 0) 'Help must not require a workspace.'
    Assert-True (@($ends | Where-Object { $_.name -eq 'find_symbol_definition' -and $_.requires_workspace -and $_.lazy_load }).Count -eq 1) 'The first semantic call must identify its lazy load.'
    $loads = @($records | Where-Object { $_.ev -eq 'load' })
    Assert-True (@($loads | Where-Object { $_.success -and $_.source -eq 'lazy' }).Count -eq 1) 'The stream must contain the successful lazy load.'
    Assert-True (@($loads | Where-Object { $_.success -and $_.cache_hit }).Count -ge 1) 'A same-key explicit load must be a cache hit.'
    Assert-True (@($loads | Where-Object { $_.success -and -not $_.cache_hit -and $_.source -eq 'explicit' }).Count -ge 2) 'Changed configuration and reload must open fresh workspaces.'
    Assert-True (@($loads | Where-Object { -not $_.success -and $null -eq $_.workspace_key }).Count -eq 1) 'The failed load after reset must leave no resident key.'
    foreach ($load in $loads) {
        Assert-True ($load.duration_ms -ge 0 -and $load.lock_wait_ms -ge 0) 'Load timings must be nonnegative.'
        Assert-True (-not [string]::IsNullOrEmpty($load.requested_key)) 'Each load attempt must identify its requested key.'
    }
    $resets = @($records | Where-Object { $_.ev -eq 'reset' })
    Assert-True ($resets.Count -eq 1 -and $resets[0].success -and $null -eq $resets[0].resident_key -and $resets[0].workspace_key) 'Reset must report the old key and cleared resident state.'
    $memory = @($records | Where-Object { $_.ev -eq 'mem' })
    Assert-True ($memory.Count -gt 0) 'Load/reset must trigger memory snapshots.'
    foreach ($sample in $memory) {
        foreach ($field in @('workspace_key', 'active_calls', 'idle_seconds', 'memory_bytes', 'buildhost_bytes', 'buildhost_count', 'working_set_bytes', 'managed_heap_bytes', 'committed_bytes')) {
            Assert-True ($sample.Contains($field)) "Memory snapshot must contain $field."
        }
        Assert-True ($sample.active_calls -ge 0 -and $sample.idle_seconds -ge 0) 'Activity state must be nonnegative.'
        if ($sample.active_calls -gt 0) { Assert-True ($sample.idle_seconds -eq 0) 'A busy process cannot accumulate idle time.' }
        foreach ($field in @('memory_bytes', 'buildhost_bytes', 'working_set_bytes', 'managed_heap_bytes', 'committed_bytes')) {
            Assert-True ($null -eq $sample[$field] -or $sample[$field] -ge 0) 'Memory counters must be nonnegative or unavailable.'
        }
    }

    $disabledState = Join-Path $evidenceRoot 'disabled'
    $disabled = Start-Server 'disabled' $disabledState '0'
    Initialize-Client $disabled
    $null = Invoke-Tool $disabled 'get_tool_help' @{ toolName = 'load_workspace' }
    Stop-Server $disabled
    Assert-True ((Read-Usage $disabledState).Count -eq 0) 'Opt-out must suppress usage records.'

    $failedState = Join-Path $evidenceRoot 'unwritable'
    [void][IO.Directory]::CreateDirectory($failedState)
    [IO.File]::WriteAllText((Join-Path $failedState 'usage'), 'This regular file intentionally blocks directory creation.')
    $failedWriter = Start-Server 'unwritable' $failedState
    Initialize-Client $failedWriter
    $null = Invoke-Tool $failedWriter 'get_tool_help' @{ toolName = 'load_workspace' }
    Stop-Server $failedWriter
    Assert-True (Test-Path -LiteralPath (Join-Path $failedState 'usage') -PathType Leaf) 'A failed writer must not alter the blocking file.'

    $failedHome = Join-Path $evidenceRoot 'unavailable-home'
    [IO.File]::WriteAllText($failedHome, 'This regular file intentionally blocks the user directory.')
    $unavailableHome = Start-Server 'unavailable-home' $failedHome -DefaultLogDirectory
    Initialize-Client $unavailableHome
    $null = Invoke-Tool $unavailableHome 'get_tool_help' @{ toolName = 'load_workspace' }
    Stop-Server $unavailableHome
    Assert-True (Test-Path -LiteralPath $failedHome -PathType Leaf) 'An unavailable user directory must not prevent startup or alter its blocking file.'

    if (-not $SkipAnalyzer) {
        $analyzerPath = Join-Path $PSScriptRoot 'Measure-McpUsage.ps1'
        $reportPrefix = Join-Path $evidenceRoot 'report'
        $null = & $analyzerPath -InputDirectory (Join-Path $normalState 'usage') -Machine 'integration' -OutputPath $reportPrefix
        foreach ($extension in @('.json', '.md')) {
            $export = [IO.File]::ReadAllText($reportPrefix + $extension)
            foreach ($sensitive in @($fixture, $projectPath, $privateMarker) + @($loads | ForEach-Object workspace_key | Where-Object { $_ } | Select-Object -Unique)) {
                Assert-True (-not $export.Contains($sensitive)) 'Export must not contain raw paths or workspace keys.'
                Assert-True (-not $export.Contains($sensitive.Replace('\', '\\'))) 'JSON export must not contain escaped raw paths.'
            }
        }
        Assert-True ([bool]((Get-Content -LiteralPath ($reportPrefix + '.json') -Raw | ConvertFrom-Json))) 'The exported JSON must parse.'
    }

    $summary = [ordered]@{
        status = 'passed'; checks = $script:checks; records = $records.Count
        processes = $groups.Count; loads = $loads.Count; completed_calls = $ends.Count
        analyzer_checked = -not $SkipAnalyzer; server = $ServerPath
        harness_validation = 'Protocol probe only; real harness launches were not exercised.'
        evidence = $evidenceRoot
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'summary.json') -Encoding utf8NoBOM
    $summary | ConvertTo-Json -Depth 6
}
catch {
    [ordered]@{ status = 'failed'; checks = $script:checks; error = $_.Exception.Message; evidence = $evidenceRoot } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceRoot 'summary.json') -Encoding utf8NoBOM
    throw
}
finally {
    foreach ($client in $clients) {
        if (-not $client.process.HasExited) {
            $client.process.Kill($true)
            [void]$client.process.WaitForExit(5000)
        }
        if ($client.stderr.IsCompleted) {
            [IO.File]::WriteAllText((Join-Path $evidenceRoot ($client.name + '.stderr.txt')), $client.stderr.GetAwaiter().GetResult())
        }
        $client.process.Dispose()
    }
}
