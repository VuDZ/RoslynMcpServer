#requires -Version 7.0
# Exercises the public analyzer against synthetic JSONL; never starts a server.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$measurementScript = Join-Path $PSScriptRoot 'Measure-McpUsage.ps1'
$tokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile($measurementScript, [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count) { throw 'The analyzer has PowerShell parse errors.' }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('mcp-usage-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$script:assertions = 0
$script:cases = 0
$script:events = $null
$epoch = [DateTimeOffset]'2026-10-29T23:55:00Z'

function Assert-Equal($Expected, $Actual, [string]$Label) {
    $script:assertions++
    if ($Expected -ne $Actual) { throw ($Label + ": expected '$Expected', got '$Actual'.") }
}
function Assert-Near([double]$Expected, $Actual, [string]$Label) {
    $script:assertions++
    if ($null -eq $Actual -or [Math]::Abs($Expected - [double]$Actual) -gt 0.0000001) {
        throw ($Label + ": expected $Expected, got '$Actual'.")
    }
}
function New-Tape {
    $script:events = [Collections.Generic.List[object]]::new()
}
function Add-Event([string]$Process, [double]$Second, [string]$Type, [hashtable]$Fields = @{}) {
    $row = [ordered]@{
        v = 1; ts = $epoch.AddSeconds($Second).ToString('o'); proc = $Process
        seq = 0; pid = 42; ppid = 1; harness = 'codex'; ver = 'test'; ev = $Type
    }
    foreach ($key in $Fields.Keys) { $row[$key] = $Fields[$key] }
    $order = switch ($Type) { 'start' { 0 }; 'tool_start' { 10 }; 'load' { 20 }; 'tool' { 30 }; 'mem' { 40 }; 'exit' { 100 }; default { 25 } }
    $script:events.Add(@{ second = $Second; order = $order; row = $row; skip = 0 })
}
function Add-Process(
    [string]$Process = 'a', [string]$Key = 'PRIVATE:/src/App.sln', [double]$GiB = 1,
    [double]$ChildGiB = 0, [int]$Start = 0, [int]$End = 600, [object[]]$Calls = @(),
    [switch]$NoExit, [switch]$Cached
) {
    Add-Event $Process $Start 'start' @{ cwd = 'PRIVATE:/src'; gc = 'server'; cores = 8; profile = 'full' }
    Add-Event $Process $Start 'load' @{
        requested_key = $Key; workspace_key = $Key; success = $true; cache_hit = [bool]$Cached
        source = 'explicit'; graph_stale = $false; duration_ms = 10000; lock_wait_ms = 0; projects = 1
    }
    $callIndex = 0
    foreach ($call in $Calls) {
        $id = ++$callIndex
        $complete = -not $call.Contains('complete') -or $call.complete
        $required = -not $call.Contains('required') -or $call.required
        $name = if ($call.Contains('name')) { $call.name } else { 'find_usages' }
        $fields = @{
            call = $id; name = $name; start_ts = $epoch.AddSeconds($call.start).ToString('o')
            workspace_key = $Key; requires_workspace = $required
        }
        Add-Event $Process $call.start 'tool_start' $fields
        if ($complete) {
            $fields = $fields.Clone()
            $fields.duration_ms = ($call.end - $call.start) * 1000
            $fields.success = -not $call.Contains('success') -or $call.success
            $fields.lazy_load = $call.Contains('lazy') -and $call.lazy
            Add-Event $Process $call.end 'tool' $fields
        }
    }
    $samples = @($Start..$End | Where-Object { ($_ - $Start) % 60 -eq 0 })
    if ($samples[-1] -ne $End) { $samples += $End }
    foreach ($second in $samples) {
        $active = @($Calls | Where-Object {
            $_.start -le $second -and ($_.end -gt $second -or ($_.Contains('complete') -and -not $_.complete -and $_.end -eq $second))
        }).Count
        $lastActivity = [double]$Start
        foreach ($call in $Calls) {
            if ($call.start -le $second) { $lastActivity = [Math]::Max($lastActivity, $call.start) }
            if ($call.end -le $second -and (-not $call.Contains('complete') -or $call.complete)) {
                $lastActivity = [Math]::Max($lastActivity, $call.end)
            }
        }
        Add-Event $Process $second 'mem' @{
            workspace_key = $Key; memory_bytes = $GiB * 1GB; buildhost_bytes = $ChildGiB * 1GB
            buildhost_count = [int]($ChildGiB -gt 0); active_calls = $active
            idle_seconds = $(if ($active) { 0 } else { $second - $lastActivity })
            working_set_bytes = $GiB * 1GB; managed_heap_bytes = 0; committed_bytes = 0
        }
    }
    if (-not $NoExit) { Add-Event $Process $End 'exit' @{ reason = 'stdin_eof' } }
}
function Save-Tape([string]$Name, [int]$SplitAt = 0) {
    $directory = Join-Path $testRoot $Name
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $sequences = @{}
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($event in @($script:events | Sort-Object second, order)) {
        $proc = $event.row.proc
        if (-not $sequences.ContainsKey($proc)) { $sequences[$proc] = 0 }
        $sequences[$proc] += 1 + $event.skip
        $event.row.seq = $sequences[$proc]
        $lines.Add(($event.row | ConvertTo-Json -Compress -Depth 8))
    }
    if ($SplitAt -gt 0 -and $SplitAt -lt $lines.Count) {
        [IO.File]::WriteAllLines((Join-Path $directory 'usage-20261029-0.jsonl'), @($lines | Select-Object -First $SplitAt))
        [IO.File]::WriteAllLines((Join-Path $directory 'usage-20261030-1.jsonl'), @($lines | Select-Object -Skip $SplitAt))
    }
    else { [IO.File]::WriteAllLines((Join-Path $directory 'usage-20261029-0.jsonl'), $lines) }
    return $directory
}
function Invoke-Case([string]$Name, [int]$SplitAt = 0) {
    $script:cases++
    $directory = Save-Tape $Name $SplitAt
    return & $measurementScript -InputDirectory $directory -Machine desktop -OutputPath (Join-Path $directory 'report')
}
function Get-Timeout($Report, [int]$Minutes = 5) {
    return @($Report.machines[0].thresholds | Where-Object { $_.minutes -eq $Minutes })[0]
}

try {
    New-Tape
    Add-Process -GiB 1 -ChildGiB 0.5
    $report = Invoke-Case 'idle'
    Assert-Near (1.5 * 5 / 60) (Get-Timeout $report).idle_gib_hours 'M1 includes BuildHost and only the tail after T'
    Assert-Near 0.75 (Get-Timeout $report).idle_mean_gib 'M1 mean uses machine hours'
    Assert-Near 0 (Get-Timeout $report 15).idle_gib_hours 'M1 T15 has no tail'
    Assert-Near (10 / 60) $report.machines[0].observation.usable_hours 'Observation duration'
    Assert-Equal '2026-10-29T23:55:00.0000000+00:00' $report.machines[0].observation.start 'UTC is preserved across PowerShell date parsing'

    New-Tape
    Add-Process -Calls @(@{ start = 600; end = 601 }) -End 660
    $report = Invoke-Case 'return'
    Assert-Equal 1 (Get-Timeout $report).reload_episodes 'M2 one semantic return'
    Assert-Near 10 (Get-Timeout $report).estimated_reload_seconds 'M2 fresh-load cost'
    Assert-Equal 0 (Get-Timeout $report 15).reload_episodes 'M2 timeout-specific return'
    Assert-Equal 1 $report.machines[0].tools[0].calls 'M6 completed tool count'
    Assert-Near 1000 $report.machines[0].tools[0].median_duration_ms 'M6 tool duration'

    New-Tape
    Add-Process -Calls @(@{ start = 600; end = 601; required = $false; name = 'get_tool_help' }) -End 660
    $report = Invoke-Case 'help-only'
    Assert-Equal 0 (Get-Timeout $report).reload_episodes 'Workspace-free return does not reload'

    New-Tape
    Add-Process -Calls @(
        @{ start = 600; end = 601; required = $false; name = 'get_tool_help' },
        @{ start = 610; end = 620; lazy = $true }, @{ start = 610; end = 630 }
    ) -End 660
    $report = Invoke-Case 'help-then-concurrent'
    Assert-Equal 1 (Get-Timeout $report).reload_episodes 'Eviction survives help and coalesces concurrent semantic returns'
    Assert-Equal 1 @($report.machines[0].tools | Where-Object { $_.name -eq 'find_usages' })[0].lazy_loads 'M6 lazy count'

    New-Tape
    Add-Process -Cached -Calls @(@{ start = 600; end = 601 }) -End 660
    $report = Invoke-Case 'unknown-load-cost'
    Assert-Equal 1 (Get-Timeout $report).unknown_cost_episodes 'Cache hits cannot price a fresh load'
    Assert-Equal $null (Get-Timeout $report).estimated_reload_seconds 'Unknown cost is not zero'

    New-Tape
    Add-Process -Calls @(@{ start = 1000; end = 1001 }) -End 1080
    Add-Event 'a' 60 'load' @{ requested_key = 'PRIVATE:/src/App.sln'; workspace_key = 'PRIVATE:/src/App.sln'; success = $true; cache_hit = $false; duration_ms = 20000 }
    Add-Event 'a' 120 'load' @{ requested_key = 'PRIVATE:/src/App.sln'; workspace_key = 'PRIVATE:/src/App.sln'; success = $false; cache_hit = $false; duration_ms = 900000 }
    Add-Event 'a' 180 'load' @{ requested_key = 'PRIVATE:/src/App.sln'; workspace_key = 'PRIVATE:/src/App.sln'; success = $true; cache_hit = $true; duration_ms = 1 }
    $report = Invoke-Case 'median-load-cost'
    Assert-Near 15 (Get-Timeout $report).estimated_reload_seconds 'M2 median excludes failures and cache hits'
    Assert-Equal 3 $report.machines[0].workspace_operations[0].repeated_loads 'M6 repeated loads'
    Assert-Equal 1 $report.machines[0].workspace_operations[0].cache_hits 'M6 cache-hit count'

    New-Tape
    Add-Process -Process a -GiB 1 -ChildGiB 1
    Add-Process -Process b -GiB 1
    $report = Invoke-Case 'duplicates'
    Assert-Near (10 / 60) $report.machines[0].duplication.gib_hours 'M3 excludes the largest survivor'
    Assert-Near (5 / 60) (Get-Timeout $report).residual_duplicate_gib_hours 'M3r removes idle duplicates after T'
    Assert-Equal 2 $report.machines[0].composition.process_count 'M4 process count'
    Assert-Equal 1 $report.machines[0].subagent_heuristic.candidates 'M5 same-harness candidate'
    Assert-Equal $false $report.machines[0].subagent_heuristic.establishes_parentage 'M5 does not claim parentage'

    New-Tape
    Add-Process -Process a -GiB 3
    Add-Process -Process b -GiB 2 -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process c -GiB 1 -Calls @(@{ start = 0; end = 600 })
    $report = Invoke-Case 'largest-copy-evicted'
    Assert-Near 0.5 $report.machines[0].duplication.gib_hours 'M3 three unequal copies'
    Assert-Near (1 / 3) (Get-Timeout $report).residual_duplicate_gib_hours 'M3r recomputes the survivor after the largest copy is evicted'
    Assert-Near (10 / 60) $report.machines[0].duplication.active_overlap_hours 'M3a overlapping calls'

    New-Tape
    $firstCalls = @(0,120,240,360,480 | ForEach-Object { @{ start = $_; end = $_ + 1 } })
    $secondCalls = @(60,180,300,420,540 | ForEach-Object { @{ start = $_; end = $_ + 1 } })
    Add-Process -Process a -Calls $firstCalls
    Add-Process -Process b -Calls $secondCalls
    $report = Invoke-Case 'alternating'
    Assert-Near 0 (Get-Timeout $report).idle_gib_hours 'Short alternating pauses never reach T'
    Assert-Near $report.machines[0].duplication.gib_hours (Get-Timeout $report).residual_duplicate_gib_hours 'Short alternating pauses leave all duplicates'
    Assert-Near 0 $report.machines[0].duplication.active_overlap_hours 'Alternating calls do not overlap'

    New-Tape
    Add-Process -NoExit -Calls @(
        @{ start = 0; end = 600; complete = $false },
        @{ start = 5; end = 6; required = $false; name = 'get_tool_help' }
    )
    $report = Invoke-Case 'killed-long-call'
    Assert-Near 0 (Get-Timeout $report).idle_gib_hours 'An unfinished long call remains busy despite a short parallel call'
    Assert-Equal 1 $report.machines[0].composition.processes[0].unfinished_calls 'Unfinished call count'
    Assert-Equal 1 $report.machines[0].observation.right_censored_processes 'Missing exit is right-censored'

    New-Tape
    Add-Process -Calls @(@{ start = 300; end = 300 }) -End 300
    $report = Invoke-Case 'threshold-boundary'
    Assert-Near 0 (Get-Timeout $report).idle_gib_hours 'No idle tail at exactly T'
    Assert-Equal 0 (Get-Timeout $report).reload_episodes 'An arrival exactly at T wins the eviction boundary'

    New-Tape
    Add-Process -Process before -End 600
    Add-Process -Process after -Start 600 -End 1200
    $report = Invoke-Case 'pid-reuse-and-day-rotation' 12
    Assert-Equal 2 $report.machines[0].composition.process_count 'A reused PID does not merge process identities'
    Assert-Near 0 $report.machines[0].duplication.gib_hours 'Adjacent lifetimes do not overlap across files or midnight'
    Assert-Near (20 / 60) $report.machines[0].observation.usable_hours 'File rotation preserves the observation span'

    New-Tape
    Add-Process
    @($script:events | Where-Object { $_.row.ev -eq 'mem' -and $_.second -eq 420 })[0].skip = 1
    $report = Invoke-Case 'sequence-gap'
    Assert-Equal 1 $report.machines[0].observation.warnings.sequence_gaps 'Sequence gap detected'
    Assert-Near (9 / 60) $report.machines[0].observation.usable_hours 'Sequence gap excluded from denominator'
    Assert-Near (4 / 60) (Get-Timeout $report).idle_gib_hours 'Sequence gap excluded from idle memory'

    New-Tape
    Add-Process
    $script:events = [Collections.Generic.List[object]]@($script:events | Where-Object {
        -not ($_.row.ev -eq 'mem' -and $_.second -gt 120 -and $_.second -lt 420)
    })
    $report = Invoke-Case 'heartbeat-gap'
    Assert-Equal 1 $report.machines[0].observation.warnings.heartbeat_gaps 'Missing heartbeat gap detected'
    Assert-Near (5 / 60) $report.machines[0].observation.usable_hours 'Heartbeat gap excluded from denominator'
    Assert-Near (3 / 60) (Get-Timeout $report).idle_gib_hours 'Heartbeat gap does not extend the idle integral'

    New-Tape
    Add-Process
    @($script:events | Where-Object { $_.row.ev -eq 'mem' -and $_.second -eq 360 })[0].row.memory_bytes = $null
    $report = Invoke-Case 'missing-memory-field'
    Assert-Equal 1 $report.machines[0].observation.warnings.incomplete_snapshots 'Null memory is unavailable'
    Assert-Near (8 / 60) $report.machines[0].observation.usable_hours 'Incomplete snapshots excluded until recovery'

    New-Tape
    Add-Process
    @($script:events | Where-Object { $_.row.ev -eq 'mem' -and $_.second -eq 360 })[0].row.v = 2
    $directory = Save-Tape 'mixed-schema-and-torn-tail'
    [IO.File]::AppendAllText((Join-Path $directory 'usage-20261029-0.jsonl'), '{"v":')
    $script:cases++
    $report = & $measurementScript -InputDirectory $directory -Machine desktop -OutputPath (Join-Path $directory 'report')
    Assert-Equal 1 $report.machines[0].observation.warnings.unsupported_version_events 'Unknown schema is not reinterpreted'
    Assert-Equal 1 $report.machines[0].observation.warnings.torn_tails 'Torn final line ignored and reported'
    Assert-Near (8 / 60) $report.machines[0].observation.usable_hours 'Unknown schema interval excluded'

    New-Tape
    Add-Process -Process a -Key '10:/src/App|7:Release|-1:|7:net10.0'
    Add-Process -Process b -Key '10:/src/app|7:Release|-1:|7:net10.0'
    $report = Invoke-Case 'ordinal-key'
    Assert-Equal 2 $report.machines[0].workspaces.Count 'Distinct Linux-case encoded keys stay distinct'
    Assert-Near 0 $report.machines[0].duplication.gib_hours 'The analyzer does not recanonicalize encoded keys'
    $json = [IO.File]::ReadAllText((Join-Path $testRoot 'ordinal-key/report.json'))
    $markdown = [IO.File]::ReadAllText((Join-Path $testRoot 'ordinal-key/report.md'))
    Assert-Equal $false ($json.Contains('/src/App') -or $json.Contains('/src/app') -or $json.Contains('PRIVATE:')) 'JSON does not export keys or cwd'
    Assert-Equal $false ($markdown.Contains('/src/') -or $markdown.Contains('PRIVATE:')) 'Markdown does not export keys or cwd'

    New-Tape
    Add-Process -Process a
    Add-Process -Process b -Calls @(@{ start = 0; end = 600 })
    Add-Event 'a' 180 'load' @{ requested_key = 'PRIVATE:/other.sln'; workspace_key = 'PRIVATE:/src/App.sln'; success = $false; cache_hit = $false; duration_ms = 10 }
    Add-Event 'a' 240 'reset' @{ workspace_key = 'PRIVATE:/src/App.sln'; resident_key = 'PRIVATE:/src/App.sln'; success = $false; seconds_since_load = 240 }
    Add-Event 'a' 360 'reset' @{ workspace_key = 'PRIVATE:/src/App.sln'; resident_key = $null; success = $true; seconds_since_load = 360 }
    foreach ($event in $script:events) {
        if ($event.row.proc -eq 'a' -and $event.row.ev -eq 'mem' -and $event.second -ge 360) { $event.row.workspace_key = $null }
    }
    $report = Invoke-Case 'failure-and-reset'
    Assert-Near (6 / 60) $report.machines[0].duplication.gib_hours 'Failed load/reset preserve resident key; successful reset unloads'
    Assert-Equal 2 $report.machines[0].workspace_operations[0].resets 'M6 reset attempts'
    Assert-Equal 1 $report.machines[0].workspace_operations[0].failed_resets 'M6 failed reset count'

    New-Tape
    Add-Process
    $directory = Save-Tape 'duplicate-export'
    Copy-Item -LiteralPath (Join-Path $directory 'usage-20261029-0.jsonl') -Destination (Join-Path $directory 'usage-20261029-1.jsonl')
    $script:cases++
    $report = & $measurementScript -InputDirectory @($directory, $directory) -Machine @('desktop', 'desktop') -OutputPath (Join-Path $directory 'report')
    Assert-Equal 1 $report.machines[0].composition.process_count 'Repeated exports do not duplicate processes'
    Assert-Equal $script:events.Count $report.machines[0].observation.warnings.duplicate_records 'Duplicate sequence records counted once'
    Assert-Near (5 / 60) (Get-Timeout $report).idle_gib_hours 'Repeated exports do not double the integral'

    New-Tape
    Add-Process -GiB 1
    $directoryA = Save-Tape 'machine-a'
    New-Tape
    Add-Process -GiB 2
    $directoryB = Save-Tape 'machine-b'
    $script:cases++
    $report = & $measurementScript -InputDirectory @($directoryA, $directoryB) -Machine @('desktop', 'laptop') -OutputPath (Join-Path $testRoot 'multi-machine')
    Assert-Equal 2 $report.machines.Count 'Machines are not pooled'
    Assert-Near (5 / 60) @($report.machines[0].thresholds | Where-Object { $_.minutes -eq 5 })[0].idle_gib_hours 'First machine retains its own integral'
    Assert-Near (10 / 60) @($report.machines[1].thresholds | Where-Object { $_.minutes -eq 5 })[0].idle_gib_hours 'Second machine retains its own integral'

    New-Tape
    Add-Event 'fractional' 0 'start' @{ ts = '2026-10-30T03:00:00.125+03:00'; cwd = 'PRIVATE:/cwd' }
    Add-Event 'fractional' 0 'mem' @{
        ts = '2026-10-30T03:00:00.125+03:00'; workspace_key = 'PRIVATE:/key'
        memory_bytes = 1GB; buildhost_bytes = 0; active_calls = 0; idle_seconds = 0
    }
    Add-Event 'fractional' 60 'mem' @{
        ts = '2026-10-30T00:01:00.375+00:00'; workspace_key = 'PRIVATE:/key'
        memory_bytes = 1GB; buildhost_bytes = 0; active_calls = 0; idle_seconds = 60.25
    }
    $report = Invoke-Case 'fractional-offset'
    Assert-Equal '2026-10-30T00:00:00.1250000+00:00' $report.machines[0].observation.start 'Fractional offset timestamps preserve their instant'
    Assert-Near (60.25 / 3600) $report.machines[0].observation.usable_hours 'Fractional timestamp duration'

    New-Tape
    Add-Event 'unsupported' 0 'start' @{ v = 2 }
    Add-Event 'unsupported' 600 'exit' @{ v = 2 }
    $report = Invoke-Case 'no-supported-memory'
    Assert-Equal $false $report.machines[0].observation.memory_metrics_available 'Unsupported history cannot produce memory metrics'
    Assert-Equal $null (Get-Timeout $report).idle_gib_hours 'Unavailable integral is not zero'

    New-Tape
    $report = Invoke-Case 'empty'
    Assert-Equal 0 $report.machines[0].composition.process_count 'An empty export has no processes'
    Assert-Equal $null $report.machines[0].duplication.gib_hours 'An empty export has no invented memory metric'

    foreach ($scenario in @('switch', 'same-key-fresh', 'failed-fresh', 'cached-same-key', 'reset')) {
        New-Tape
        $toolName = if ($scenario -eq 'reset') { 'reset_workspace' } else { 'load_workspace' }
        Add-Process -Calls @(@{ start = 600; end = 610; name = $toolName }) -End 660
        $keyAfter = if ($scenario -eq 'switch') { 'PRIVATE:/src/Other.sln' } else { 'PRIVATE:/src/App.sln' }
        if ($scenario -eq 'reset') {
            Add-Event 'a' 610 'reset' @{ call = 1; workspace_key = 'PRIVATE:/src/App.sln'; resident_key = $null; success = $true }
        } else {
            Add-Event 'a' 610 'load' @{
                call = 1; requested_key = $keyAfter; workspace_key = $keyAfter
                success = $scenario -ne 'failed-fresh'; cache_hit = $scenario -eq 'cached-same-key'; duration_ms = 10000
            }
        }
        foreach ($event in $script:events) {
            if ($event.row.ev -eq 'mem' -and $event.second -gt 610) {
                $event.row.workspace_key = if ($scenario -eq 'reset') { $null } else { $keyAfter }
            }
        }
        $report = Invoke-Case ('baseline-' + $scenario)
        $expectedEpisodes = if ($scenario -eq 'cached-same-key') { 1 } else { 0 }
        Assert-Equal $expectedEpisodes (Get-Timeout $report).reload_episodes ('Only an avoidable cached opening adds a reload: ' + $scenario)
    }

    New-Tape
    Add-Process -Calls @(@{ start = 600; end = 601 }) -End 660
    foreach ($event in @($script:events | Where-Object { $_.row.ev -in @('tool_start', 'tool') })) {
        $event.row.Remove('requires_workspace')
    }
    $report = Invoke-Case 'unknown-requirement'
    Assert-Equal $null (Get-Timeout $report).reload_episodes 'Missing requirement does not become a zero reload estimate'
    Assert-Equal $false (Get-Timeout $report).reload_metrics_available 'Incomplete tool semantics are marked unavailable'

    New-Tape
    Add-Process
    @($script:events | Where-Object { $_.row.ev -eq 'mem' -and $_.second -eq 360 })[0].row.ts = $epoch.AddSeconds(200).ToString('o')
    $report = Invoke-Case 'clock-regression'
    Assert-Equal 1 $report.machines[0].observation.warnings.clock_regressions 'Wall-clock regression is detected from sequence order'
    Assert-Equal $null (Get-Timeout $report).idle_gib_hours 'A clock-regressed process cannot produce a misleading memory integral'

    New-Tape
    Add-Process
    $directory = Save-Tape 'conflicting-export'
    $sourceFile = Join-Path $directory 'usage-20261029-0.jsonl'
    $lines = [IO.File]::ReadAllLines($sourceFile)
    $conflict = $lines[0] | ConvertFrom-Json -AsHashtable
    $conflict.harness = 'claude'
    [IO.File]::WriteAllText((Join-Path $directory 'usage-20261029-1.jsonl'), ($conflict | ConvertTo-Json -Compress))
    $script:cases++
    $rejected = $false
    try { & $measurementScript -InputDirectory $directory -Machine desktop -OutputPath (Join-Path $directory 'report') | Out-Null }
    catch { $rejected = $_.Exception.Message -like 'Conflicting records*' }
    Assert-Equal $true $rejected 'Conflicting copies are rejected instead of silently selected'

    New-Tape
    Add-Process -Process a -End 1200 -Calls @(@{ start = 600; end = 601 })
    Add-Process -Process b -End 1200 -Calls @(@{ start = 0; end = 1200 })
    foreach ($event in @($script:events | Where-Object { $_.row.proc -eq 'a' -and $_.row.ev -in @('tool_start', 'tool') })) {
        $event.row.Remove('requires_workspace')
    }
    $report = Invoke-Case 'unknown-restoration-residual'
    Assert-Near (20 / 60) $report.machines[0].duplication.gib_hours 'Unknown restoration does not invalidate observed baseline duplicates'
    Assert-Equal $false (Get-Timeout $report).residual_metrics_available 'Residual policy is unavailable with unknown restoration semantics'
    Assert-Equal $null (Get-Timeout $report).residual_duplicate_gib_hours 'Unknown restoration cannot produce a precise residual integral'
    Assert-Equal $null (Get-Timeout $report).residual_duplicate_mean_gib 'Unknown restoration cannot produce a precise residual mean'
    Assert-Equal 4 @($report.machines[0].thresholds | Where-Object { $null -eq $_.residual_duplicate_gib_hours }).Count 'Unavailable restoration is explicit for every simulated policy'

    foreach ($unknownLazy in @('missing', 'null', 'wrong-type')) {
        New-Tape
        Add-Process -Calls @(
            @{ start = 10; end = 11; lazy = $true },
            @{ start = 20; end = 21 }
        )
        $unknownEnd = @($script:events | Where-Object { $_.row.ev -eq 'tool' -and $_.row.call -eq 2 })[0].row
        switch ($unknownLazy) {
            'missing' { $unknownEnd.Remove('lazy_load') }
            'null' { $unknownEnd.lazy_load = $null }
            'wrong-type' { $unknownEnd.lazy_load = 'false' }
        }
        $report = Invoke-Case ('unknown-lazy-' + $unknownLazy)
        $tool = $report.machines[0].tools[0]
        Assert-Equal 1 $tool.lazy_load_known_samples ('Known lazy sample count: ' + $unknownLazy)
        Assert-Equal 1 $tool.lazy_load_unknown_samples ('Unknown lazy sample count: ' + $unknownLazy)
        Assert-Equal 1 $tool.known_lazy_loads ('Observed positive lazy samples survive partial coverage: ' + $unknownLazy)
        Assert-Equal $null $tool.lazy_loads ('Incomplete lazy history does not invent a total: ' + $unknownLazy)
        Assert-Equal $null $tool.lazy_load_fraction ('Incomplete lazy history does not invent a fraction: ' + $unknownLazy)
    }

    New-Tape
    Add-Process -Calls @(@{ start = 10; end = 11; lazy = $true }, @{ start = 20; end = 21 })
    $report = Invoke-Case 'known-lazy-fraction'
    $tool = $report.machines[0].tools[0]
    Assert-Equal 2 $tool.lazy_load_known_samples 'Complete lazy history has two known samples'
    Assert-Equal 0 $tool.lazy_load_unknown_samples 'Complete lazy history has no unknown samples'
    Assert-Equal 1 $tool.lazy_loads 'Complete lazy history has a known total'
    Assert-Near 0.5 $tool.lazy_load_fraction 'Complete lazy history has a known fraction'

    foreach ($transitionCase in @('load-key', 'load-success', 'reset-key', 'reset-success')) {
        New-Tape
        Add-Process -Process a
        Add-Process -Process b -Calls @(@{ start = 0; end = 600 })
        $script:events = [Collections.Generic.List[object]]@($script:events | Where-Object {
            -not ($_.row.proc -eq 'a' -and $_.row.ev -eq 'mem' -and $_.second -eq 180)
        })
        $isLoad = $transitionCase.StartsWith('load')
        $keyAfter = if ($isLoad) { 'PRIVATE:/src/Other.sln' } else { $null }
        if ($isLoad) {
            $fields = @{ requested_key = 'PRIVATE:/src/Other.sln'; workspace_key = $keyAfter; success = $true; cache_hit = $false; duration_ms = 10 }
            $missingField = if ($transitionCase -eq 'load-key') { 'workspace_key' } else { 'success' }
            $fields.Remove($missingField)
            Add-Event 'a' 180 'load' $fields
        }
        else {
            $fields = @{ workspace_key = 'PRIVATE:/src/App.sln'; resident_key = $null; success = $true }
            $missingField = if ($transitionCase -eq 'reset-key') { 'resident_key' } else { 'success' }
            $fields.Remove($missingField)
            Add-Event 'a' 180 'reset' $fields
        }
        foreach ($event in $script:events) {
            if ($event.row.proc -eq 'a' -and $event.row.ev -eq 'mem' -and $event.second -ge 240) {
                $event.row.workspace_key = $keyAfter
            }
        }
        $report = Invoke-Case ('unknown-transition-' + $transitionCase)
        Assert-Equal 1 $report.machines[0].observation.warnings.incomplete_workspace_transitions ('Incomplete transition is reported: ' + $transitionCase)
        Assert-Near (1 / 60) $report.machines[0].observation.gap_hours ('Only transition-to-recovery interval is a gap: ' + $transitionCase)
        Assert-Near (10 / 60) $report.machines[0].observation.usable_hours ('Healthy peer preserves the observation denominator: ' + $transitionCase)
        Assert-Near (3 / 60) $report.machines[0].duplication.gib_hours ('Unknown residency does not extend old-key duplicates: ' + $transitionCase)
    }

    New-Tape
    Add-Process -Process healthy-idle -Key 'PRIVATE:/healthy.sln'
    Add-Process -Process healthy-busy -Key 'PRIVATE:/healthy.sln' -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process incomplete -Key 'PRIVATE:/other.sln' -Calls @(@{ start = 0; end = 600 })
    @($script:events | Where-Object { $_.row.proc -eq 'incomplete' -and $_.row.ev -eq 'mem' -and $_.second -eq 420 })[0].skip = 1
    $report = Invoke-Case 'process-local-sequence-gap'
    Assert-Near (5 / 60) (Get-Timeout $report).idle_gib_hours 'A peer sequence gap does not remove healthy idle memory'
    Assert-Near (10 / 60) $report.machines[0].observation.usable_hours 'Machine coverage is the union of usable processes'
    Assert-Near (1 / 60) $report.machines[0].observation.gap_hours 'A peer sequence gap remains visible separately'
    Assert-Near (10 / 60) $report.machines[0].duplication.gib_hours 'A peer gap on another key does not remove healthy duplicates'
    Assert-Near (5 / 60) (Get-Timeout $report).residual_duplicate_gib_hours 'A peer gap on another key does not remove healthy residual duplicates'
    Assert-Equal 1 $report.machines[0].observation.warnings.sequence_gaps 'Process-local sequence loss is still diagnosed'

    New-Tape
    Add-Process -Process healthy-idle -Key 'PRIVATE:/healthy.sln'
    Add-Process -Process healthy-busy -Key 'PRIVATE:/healthy.sln' -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process incomplete -Key 'PRIVATE:/other.sln' -Calls @(@{ start = 0; end = 600 })
    foreach ($event in $script:events) {
        if ($event.row.proc -eq 'incomplete' -and $event.row.ev -eq 'mem') { $event.row.buildhost_bytes = $null }
    }
    $report = Invoke-Case 'process-local-null-buildhost'
    Assert-Near (5 / 60) (Get-Timeout $report).idle_gib_hours 'Unknown peer BuildHost does not remove healthy idle memory'
    Assert-Near (10 / 60) $report.machines[0].observation.usable_hours 'A process without usable memory cannot darken healthy coverage'
    Assert-Near (10 / 60) $report.machines[0].observation.gap_hours 'A fully missing process remains in gap diagnostics'
    Assert-Near (10 / 60) $report.machines[0].duplication.gib_hours 'Unrelated healthy duplicate keys survive a fully missing process'
    Assert-Near (5 / 60) (Get-Timeout $report).residual_duplicate_gib_hours 'Unrelated healthy residual duplicates survive a fully missing process'
    Assert-Equal 1 $report.machines[0].observation.warnings.processes_without_memory 'The unusable process is diagnosed once'

    New-Tape
    Add-Process -Process healthy-idle
    Add-Process -Process incomplete-busy -Calls @(@{ start = 0; end = 600 })
    @($script:events | Where-Object { $_.row.proc -eq 'incomplete-busy' -and $_.row.ev -eq 'mem' -and $_.second -eq 420 })[0].skip = 1
    $report = Invoke-Case 'shared-key-sequence-gap'
    Assert-Near (5 / 60) (Get-Timeout $report).idle_gib_hours 'Healthy M1 continues during a same-key peer gap'
    Assert-Near (10 / 60) $report.machines[0].observation.usable_hours 'Same-key uncertainty does not remove healthy machine coverage'
    Assert-Near (9 / 60) $report.machines[0].duplication.gib_hours 'M3 omits a same-key group while one copy has a sequence gap'
    Assert-Near (9 / 60) (Get-Timeout $report 15).residual_duplicate_gib_hours 'M3r omits the same-key group during a peer gap'
    Assert-Near (5 / 60) (Get-Timeout $report).residual_duplicate_gib_hours 'The shorter policy retains only its known pre-eviction overlap'

    New-Tape
    Add-Process -Process healthy-busy -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process incomplete-busy -Calls @(@{ start = 0; end = 600 })
    @($script:events | Where-Object { $_.row.proc -eq 'incomplete-busy' -and $_.row.ev -eq 'mem' -and $_.second -eq 240 })[0].row.buildhost_bytes = $null
    $report = Invoke-Case 'shared-key-null-memory'
    Assert-Near (10 / 60) $report.machines[0].observation.usable_hours 'A healthy busy peer preserves the denominator'
    Assert-Near (2 / 60) $report.machines[0].observation.gap_hours 'An incomplete snapshot leaves its original recovery gap'
    Assert-Near (8 / 60) $report.machines[0].duplication.gib_hours 'M3 requires all live copies of its key to be observable'
    Assert-Near (8 / 60) $report.machines[0].duplication.active_overlap_hours 'M3a requires all live copies of its key to be observable'
    Assert-Near (8 / 60) (Get-Timeout $report).residual_duplicate_gib_hours 'M3r requires all live copies of its key to be observable'

    New-Tape
    Add-Process
    $script:events = [Collections.Generic.List[object]]@($script:events | Where-Object {
        -not ($_.row.ev -eq 'mem' -and $_.second -eq 0)
    })
    $report = Invoke-Case 'short-first-snapshot'
    Assert-Near (9 / 60) $report.machines[0].observation.usable_hours 'A process contributes only after its first memory snapshot'
    Assert-Near 0 $report.machines[0].observation.gap_hours 'A first snapshot within two intervals is not a gap'
    Assert-Equal 0 $report.machines[0].observation.gap_count 'Ordinary startup does not create a gap record'
    Assert-Near (5 / 60) (Get-Timeout $report).idle_gib_hours 'First snapshot timing does not fabricate an idle tail'

    foreach ($warmupSeconds in @(60, 120, 180)) {
        New-Tape
        Add-Process -Process healthy-idle -Key 'PRIVATE:/healthy.sln'
        Add-Process -Process healthy-busy -Key 'PRIVATE:/healthy.sln' -Calls @(@{ start = 0; end = 600 })
        Add-Process -Process starting-peer -Key 'PRIVATE:/other.sln' -Calls @(@{ start = 0; end = 600 })
        $script:events = [Collections.Generic.List[object]]@($script:events | Where-Object {
            -not ($_.row.proc -eq 'starting-peer' -and $_.row.ev -eq 'mem' -and $_.second -lt $warmupSeconds)
        })
        $report = Invoke-Case ('peer-first-snapshot-' + $warmupSeconds)
        $expectedGapHours = if ($warmupSeconds -gt 120) { $warmupSeconds / 3600.0 } else { 0 }
        Assert-Near $expectedGapHours $report.machines[0].observation.gap_hours ('Only startup longer than two intervals is a gap: ' + $warmupSeconds)
        Assert-Near (10 / 60) $report.machines[0].observation.usable_hours ('A starting peer does not delay healthy coverage: ' + $warmupSeconds)
        Assert-Near (5 / 60) (Get-Timeout $report).idle_gib_hours ('A starting peer does not remove healthy idle memory: ' + $warmupSeconds)
        Assert-Near (10 / 60) $report.machines[0].duplication.gib_hours ('A starting peer on another key does not remove healthy duplicates: ' + $warmupSeconds)
    }

    New-Tape
    Add-Process -Process transitioning -Key 'PRIVATE:/first.sln'
    Add-Process -Process first-copy-a -Key 'PRIVATE:/first.sln' -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process first-copy-b -Key 'PRIVATE:/first.sln' -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process target-copy-a -Key 'PRIVATE:/target.sln' -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process target-copy-b -Key 'PRIVATE:/target.sln' -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process unrelated-copy-a -Key 'PRIVATE:/unrelated.sln' -Calls @(@{ start = 0; end = 600 })
    Add-Process -Process unrelated-copy-b -Key 'PRIVATE:/unrelated.sln' -Calls @(@{ start = 0; end = 600 })
    $script:events = [Collections.Generic.List[object]]@($script:events | Where-Object {
        -not ($_.row.proc -eq 'transitioning' -and $_.row.ev -eq 'mem' -and $_.second -eq 180)
    })
    Add-Event 'transitioning' 180 'load' @{ requested_key = 'PRIVATE:/target.sln'; success = $true; cache_hit = $false; duration_ms = 1 }
    foreach ($event in $script:events) {
        if ($event.row.proc -eq 'transitioning' -and $event.row.ev -eq 'mem' -and $event.second -ge 240) {
            $event.row.workspace_key = 'PRIVATE:/target.sln'
        }
    }
    $report = Invoke-Case 'unknown-transition-key-candidates'
    Assert-Near (10 / 60) $report.machines[0].observation.usable_hours 'Unknown transition does not darken healthy peers'
    Assert-Near (1 / 60) $report.machines[0].observation.gap_hours 'Unknown transition retains its process-local recovery gap'
    Assert-Near (37 / 60) $report.machines[0].duplication.gib_hours 'Both possible transition keys are gated while an unrelated healthy key keeps contributing'
    Assert-Near (37 / 60) (Get-Timeout $report 15).residual_duplicate_gib_hours 'Residual duplicate gating preserves the independent key'
    Assert-Near (10 / 60) $report.machines[0].duplication.active_overlap_hours 'Independent healthy overlap remains observable during an ambiguous transition'
    $originalKey = @($report.machines[0].workspaces | Where-Object { $_.successful_loads -eq 3 })[0]
    Assert-Near (12 / 60) $originalKey.duplicate_gib_hours 'An unknown copy cannot disappear from the old-key duplicate gate'

    "PASS: $script:cases scenarios; $script:assertions assertions."
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unexpected temporary cleanup target.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
