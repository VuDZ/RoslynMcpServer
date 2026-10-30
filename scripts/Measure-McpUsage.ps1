#requires -Version 7.0
<#
.SYNOPSIS
    Aggregates local MCP usage files without exporting paths or workspace keys.
.DESCRIPTION
    Supply one machine label per input directory. Repeated labels combine exports from the same
    machine; duplicate process/sequence records are counted once. Writes OutputPath.json and
    OutputPath.md and returns the report object. Memory uses the last valid snapshot until the
    next one, excluding sequence gaps and heartbeat intervals longer than two minutes.
.EXAMPLE
    ./scripts/Measure-McpUsage.ps1 -InputDirectory ./usage-copy -Machine desktop -OutputPath ./report
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$InputDirectory,
    [Parameter(Mandatory)]
    [string[]]$Machine,
    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($InputDirectory.Count -ne $Machine.Count) {
    throw 'Provide one machine label per input directory.'
}

function Get-Time($Value) {
    if ($Value -is [DateTimeOffset]) { return $Value.ToUnixTimeMilliseconds() / 1000.0 }
    if ($Value -is [DateTime]) { return ([DateTimeOffset]$Value).ToUnixTimeMilliseconds() / 1000.0 }
    $stamp = [DateTimeOffset]::MinValue
    if ($null -eq $Value -or -not [DateTimeOffset]::TryParse(
            [string]$Value, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$stamp)) {
        return $null
    }
    return $stamp.ToUnixTimeMilliseconds() / 1000.0
}

function Format-Time([double]$Time) {
    return [DateTimeOffset]::FromUnixTimeMilliseconds([long][Math]::Round($Time * 1000)).ToString('o')
}

function Get-Number($Row, [string]$Name) {
    $number = 0.0
    if (-not $Row.Contains($Name) -or $null -eq $Row[$Name] -or
        -not [double]::TryParse([string]$Row[$Name], [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or
        -not [double]::IsFinite($number) -or $number -lt 0) {
        return $null
    }
    return $number
}

function Get-SafeName($Value) {
    $name = [string]$Value
    if ($name -match '^[A-Za-z0-9_.-]{1,160}$') { return $name }
    return 'unknown'
}

function Test-WorkspaceValue($Value) {
    return $null -eq $Value -or ($Value -is [string] -and $Value.Length -gt 0)
}

function Add-Point($Timeline, [double]$Time, [int]$Order, [string]$Kind, $Model, $Data) {
    if (-not $Timeline.ContainsKey($Time)) {
        $Timeline[$Time] = [Collections.Generic.List[object]]::new()
    }
    $Timeline[$Time].Add(@{ order = $Order; kind = $Kind; model = $Model; data = $Data })
}

function Add-Range($Ranges, [double]$Start, [double]$End, [bool]$UnknownResidency = $false) {
    if ($End -gt $Start) { $Ranges.Add(@{ start = $Start; end = $End; unknown_residency = $UnknownResidency }) }
}

function Merge-Ranges($Ranges) {
    $current = $null
    foreach ($range in @($Ranges | Sort-Object { $_.start }, { $_.end })) {
        if ($null -eq $current -or $range.start -gt $current.end) {
            if ($null -ne $current) { $current }
            $current = @{ start = [double]$range.start; end = [double]$range.end }
        }
        elseif ($range.end -gt $current.end) { $current.end = [double]$range.end }
    }
    if ($null -ne $current) { $current }
}

function Get-GapKeys($Model, $Range) {
    $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $previousKey = $null
    foreach ($row in $Model.rows) {
        if ($row._time -gt $Range.end) { break }
        if ($row.v -ne 1) { continue }
        if ($row._time -lt $Range.start) {
            if ($row.ev -eq 'mem' -and $row.Contains('workspace_key') -and (Test-WorkspaceValue $row.workspace_key)) {
                $previousKey = $row.workspace_key
            }
            elseif ($row.ev -eq 'load' -and $row._transition_valid) { $previousKey = $row.workspace_key }
            elseif ($row.ev -eq 'reset' -and $row._transition_valid) { $previousKey = $row.resident_key }
            continue
        }
        foreach ($field in @('workspace_key', 'resident_key', 'requested_key')) {
            if ($row.Contains($field) -and $null -ne $row[$field] -and (Test-WorkspaceValue $row[$field])) {
                [void]$keys.Add([string]$row[$field])
            }
        }
    }
    if ($null -ne $previousKey) { [void]$keys.Add([string]$previousKey) }
    return ,$keys
}

function Get-Median($Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}

function Set-Resident($State, $Key, [bool]$Restore, [double]$Time) {
    $keyText = if ($null -eq $Key -or [string]::IsNullOrEmpty([string]$Key)) { $null } else { [string]$Key }
    $changed = -not [string]::Equals($State.key, $keyText, [StringComparison]::Ordinal)
    if ($changed -or $Restore) {
        foreach ($timeout in @($State.retained.Keys)) { $State.retained[$timeout] = $null -ne $keyText }
    }
    $State.key = $keyText
    if ($changed) { $State.loaded_at = $Time }
}

function Add-ActivityGap($Model, [double]$Start, [double]$End) {
    $before = @($Model.snapshots | Where-Object { $_._time -le $Start } | Select-Object -Last 1)
    $after = @($Model.snapshots | Where-Object { $_._time -ge $End } | Select-Object -First 1)
    $gapStart = if ($before.Count) { $before[0]._time } else { $Model.start }
    $gapEnd = if ($after.Count) { $after[0]._time } else { $Model.end }
    Add-Range $Model.gaps $gapStart $gapEnd
}

function Get-Calls($Model, $Warnings) {
    $calls = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($row in $Model.rows) {
        if ($row.v -ne 1 -or $row.ev -notin @('tool_start', 'tool')) { continue }
        $callId = if ($row.Contains('call')) { [string]$row.call } else { 'end-' + $row.seq }
        if (-not $calls.ContainsKey($callId)) {
            $calls[$callId] = @{
                id = $callId; start = $null; end = $null; name = 'unknown'; required = $null
                key = $null; completed = $false; row = $null; baseline_fresh_opening = $false
            }
        }
        $call = $calls[$callId]
        $start = if ($row.Contains('start_ts')) { Get-Time $row.start_ts } else { $null }
        if ($null -eq $start -and $row.ev -eq 'tool_start') { $start = $row._time }
        $duration = Get-Number $row 'duration_ms'
        if ($null -eq $start -and $row.ev -eq 'tool' -and $null -ne $duration) {
            $start = $row._time - $duration / 1000
        }
        if ($null -ne $start) { $call.start = [double]$start }
        if ($row.Contains('name')) { $call.name = Get-SafeName $row.name }
        if ($row.Contains('requires_workspace') -and $row.requires_workspace -is [bool]) {
            $call.required = $row.requires_workspace
        }
        if ($row.Contains('workspace_key')) { $call.key = $row.workspace_key }
        if ($row.ev -eq 'tool') {
            $call.end = [double]$row._time
            $call.completed = $true
            $call.row = $row
        }
    }
    foreach ($row in $Model.rows) {
        if ($row.v -eq 1 -and $row.ev -eq 'load' -and $row.Contains('call') -and
            $row.Contains('cache_hit') -and $row.cache_hit -is [bool] -and $row.cache_hit -eq $false -and
            $calls.ContainsKey([string]$row.call)) {
            $calls[[string]$row.call].baseline_fresh_opening = $true
        }
    }
    foreach ($call in $calls.Values) {
        if ($null -eq $call.start) {
            $Warnings.missing_call_fields++
            $end = if ($null -ne $call.end) { $call.end } else { $Model.end }
            Add-ActivityGap $Model $end $end
            continue
        }
        if ($null -eq $call.required) { $Warnings.unknown_workspace_requirement++ }
        if ($null -eq $call.end) {
            $call.end = $Model.end
            # A zero-busy snapshot after lost completion bounds the unknown end.
            $emptySnapshot = @($Model.snapshots | Where-Object {
                $_._time -gt $call.start -and $_.active_calls -eq 0
            } | Select-Object -First 1)
            if ($emptySnapshot.Count -gt 0) { $call.end = $emptySnapshot[0]._time }
        }
        if ($call.end -lt $call.start) {
            $Warnings.invalid_call_intervals++
            Add-ActivityGap $Model $call.end $call.start
            continue
        }
        $call.start = [Math]::Max($call.start, $Model.start)
        $call.end = [Math]::Min($call.end, $Model.end)
        $call
    }
}

function Get-ToolSummary($Rows) {
    $groups = @{}
    foreach ($row in $Rows) {
        if ($row.v -ne 1 -or $row.ev -ne 'tool') { continue }
        $name = if ($row.Contains('name')) { Get-SafeName $row.name } else { 'unknown' }
        $harness = Get-SafeName $row.harness
        $id = $harness + ':' + $name
        if (-not $groups.ContainsKey($id)) {
            $groups[$id] = @{
                harness = $harness; name = $name; count = 0; failures = 0
                unknown_outcomes = 0; lazy_loads = 0; lazy_known = 0; lazy_unknown = 0; durations = [Collections.Generic.List[double]]::new()
            }
        }
        $group = $groups[$id]
        $group.count++
        if ($row.Contains('success') -and $row.success -is [bool]) {
            if (-not $row.success) { $group.failures++ }
        }
        else { $group.unknown_outcomes++ }
        if ($row.Contains('lazy_load') -and $row.lazy_load -is [bool]) {
            $group.lazy_known++
            if ($row.lazy_load) { $group.lazy_loads++ }
        }
        else { $group.lazy_unknown++ }
        $duration = Get-Number $row 'duration_ms'
        if ($null -ne $duration) { $group.durations.Add($duration) }
    }
    foreach ($group in @($groups.Values | Sort-Object harness, name)) {
        $durations = @($group.durations | Sort-Object)
        @{
            harness = $group.harness; name = $group.name; calls = $group.count
            failures = $group.failures; unknown_outcomes = $group.unknown_outcomes
            lazy_loads = if ($group.lazy_unknown -eq 0) { $group.lazy_loads } else { $null }
            known_lazy_loads = $group.lazy_loads
            lazy_load_known_samples = $group.lazy_known; lazy_load_unknown_samples = $group.lazy_unknown
            lazy_load_fraction = if ($group.lazy_unknown -eq 0) { $group.lazy_loads / [double]$group.count } else { $null }
            duration_samples = $durations.Count
            total_duration_ms = if ($durations.Count) { ($durations | Measure-Object -Sum).Sum } else { $null }
            median_duration_ms = Get-Median $durations
            p95_duration_ms = if ($durations.Count) { $durations[[Math]::Ceiling($durations.Count * 0.95) - 1] } else { $null }
        }
    }
}

function Measure-Machine([string]$Label, $Rows, $Warnings) {
    $timeouts = @(5, 15, 30, 60)
    $timeline = [Collections.Generic.SortedDictionary[double, object]]::new()
    $models = [Collections.Generic.List[object]]::new()
    $keyLabels = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $loadCosts = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $keyStats = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $procGroups = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($row in $Rows) {
        if (-not $procGroups.ContainsKey($row.proc)) {
            $procGroups[$row.proc] = [Collections.Generic.List[object]]::new()
        }
        $procGroups[$row.proc].Add($row)
        foreach ($keyName in @('workspace_key', 'requested_key', 'resident_key')) {
            if ($row.v -eq 1 -and $row.Contains($keyName) -and $null -ne $row[$keyName] -and
                -not [string]::IsNullOrEmpty([string]$row[$keyName]) -and
                -not $keyLabels.ContainsKey([string]$row[$keyName])) {
                $key = [string]$row[$keyName]
                $keyLabels[$key] = 'K' + ($keyLabels.Count + 1)
                $keyStats[$key] = @{ loads = 0; successful_loads = 0; fresh_loads = 0; duplicate_gib_seconds = 0.0 }
            }
        }
        if ($row.v -eq 1 -and $row.ev -eq 'load' -and
            $row.Contains('success') -and $row.success -is [bool] -and $row.success -eq $true -and
            $row.Contains('cache_hit') -and $row.cache_hit -is [bool] -and $row.cache_hit -eq $false -and
            $row.Contains('workspace_key') -and $null -ne $row.workspace_key) {
            $duration = Get-Number $row 'duration_ms'
            if ($null -ne $duration) {
                $key = [string]$row.workspace_key
                if (-not $loadCosts.ContainsKey($key)) { $loadCosts[$key] = [Collections.Generic.List[double]]::new() }
                $loadCosts[$key].Add($duration / 1000)
            }
        }
    }
    $allGaps = [Collections.Generic.List[object]]::new()
    foreach ($proc in $procGroups.Keys) {
        $ordered = @($procGroups[$proc] | Sort-Object { $_._time }, { $_.seq })
        $model = @{
            label = 'P' + ($models.Count + 1); rows = $ordered; start = [double]$ordered[0]._time
            end = [double]$ordered[-1]._time; harness = Get-SafeName $ordered[0].harness
            snapshots = @(); calls = @(); gaps = [Collections.Generic.List[object]]::new()
            started = @($ordered | Where-Object { $_.v -eq 1 -and $_.ev -eq 'start' }).Count -gt 0
            exited = @($ordered | Where-Object { $_.v -eq 1 -and $_.ev -eq 'exit' }).Count -gt 0
        }
        $model.snapshots = @($ordered | Where-Object {
            $_.v -eq 1 -and $_.ev -eq 'mem' -and $_.Contains('workspace_key') -and (Test-WorkspaceValue $_.workspace_key) -and
            $null -ne (Get-Number $_ 'memory_bytes') -and $null -ne (Get-Number $_ 'buildhost_bytes') -and
            $null -ne (Get-Number $_ 'idle_seconds') -and $null -ne (Get-Number $_ 'active_calls')
        })
        foreach ($snapshot in $model.snapshots) { $snapshot['_snapshot_valid'] = $true }
        if ($model.snapshots.Count -eq 0) {
            Add-Range $model.gaps $model.start $model.end
            $Warnings.processes_without_memory++
        }
        else {
            if ($model.snapshots[0]._time - $model.start -gt 120) {
                Add-Range $model.gaps $model.start $model.snapshots[0]._time
                $Warnings.heartbeat_gaps++
            }
            for ($i = 1; $i -lt $model.snapshots.Count; $i++) {
                if ($model.snapshots[$i]._time - $model.snapshots[$i - 1]._time -gt 120) {
                    Add-Range $model.gaps $model.snapshots[$i - 1]._time $model.snapshots[$i]._time $true
                    $Warnings.heartbeat_gaps++
                }
            }
            if ($model.end - $model.snapshots[-1]._time -gt 120) {
                Add-Range $model.gaps $model.snapshots[-1]._time $model.end $true
                $Warnings.heartbeat_gaps++
            }
        }
        $previous = $null
        $snapshotIndex = 0
        $previousSequenceTime = $null
        foreach ($sequenceRow in @($ordered | Sort-Object seq)) {
            if ($null -ne $previousSequenceTime -and $sequenceRow._time -lt $previousSequenceTime) {
                $Warnings.clock_regressions++
                Add-Range $model.gaps $model.start $model.end $true
                break
            }
            $previousSequenceTime = $sequenceRow._time
        }
        foreach ($row in $ordered) {
            while ($snapshotIndex -lt $model.snapshots.Count -and $model.snapshots[$snapshotIndex]._time -lt $row._time) { $snapshotIndex++ }
            $gap = $row.v -ne 1
            $unknownResidency = $row.v -ne 1
            $gapStart = if ($null -ne $previous) { $previous._time } else { $model.start }
            if ($null -ne $previous -and $row.seq -ne $previous.seq + 1) {
                $gap = $true
                $Warnings.sequence_gaps++
                $unknownResidency = $true
            }
            if ($row.ev -eq 'mem' -and -not $row.Contains('_snapshot_valid')) {
                $gap = $true
                $Warnings.incomplete_snapshots++
                if (-not $row.Contains('workspace_key') -or -not (Test-WorkspaceValue $row.workspace_key)) { $unknownResidency = $true }
            }
            if ($row.v -eq 1 -and $row.ev -in @('load', 'reset')) {
                $residentField = if ($row.ev -eq 'load') { 'workspace_key' } else { 'resident_key' }
                $row['_transition_valid'] = $row.Contains($residentField) -and
                    (Test-WorkspaceValue $row[$residentField]) -and
                    $row.Contains('success') -and $row.success -is [bool]
                if (-not $row._transition_valid) {
                    if (-not $gap) { $gapStart = $row._time }
                    $gap = $true
                    $Warnings.incomplete_workspace_transitions++
                    $unknownResidency = $true
                }
            }
            if ($gap) {
                $gapEnd = if ($snapshotIndex -lt $model.snapshots.Count) { $model.snapshots[$snapshotIndex]._time } else { $model.end }
                Add-Range $model.gaps $gapStart $gapEnd $unknownResidency
            }
            $previous = $row
        }
        $model.calls = @(Get-Calls $model $Warnings)
        $retained = @{}
        foreach ($timeout in $timeouts) { $retained[$timeout] = $false }
        $model.state = @{
            alive = $false; key = $null; loaded_at = $model.start; memory = $null
            known_calls = 0; sampled_calls = 0; idle_start = $model.start; activity_known = $false
            retained = $retained; policy_known = $false; has_loaded = $false; in_gap = $false
            uncertain_keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        }
        $models.Add($model)
        Add-Point $timeline $model.start 0 'life_start' $model $null
        Add-Point $timeline $model.end 100 'life_end' $model $null
        foreach ($range in @(Merge-Ranges $model.gaps)) {
            $allGaps.Add($range)
            Add-Point $timeline $range.start 95 'gap_start' $model $null
            Add-Point $timeline $range.end -1 'gap_end' $model $null
        }
        foreach ($range in @(Merge-Ranges @($model.gaps | Where-Object { $_.unknown_residency }))) {
            # Observed boundary/request keys remain possible copies until a trustworthy recovery snapshot.
            Add-Point $timeline $range.start 94 'unknown_keys_start' $model (Get-GapKeys $model $range)
            Add-Point $timeline $range.end -2 'unknown_keys_end' $model $null
        }
        foreach ($row in $ordered) {
            if ($row.v -ne 1 -or $row.ev -in @('tool', 'tool_start')) { continue }
            Add-Point $timeline $row._time 20 'row' $model $row
            if ($row.ev -in @('load', 'start', 'mem')) {
                $idleAnchor = if ($row.ev -eq 'mem' -and $null -ne (Get-Number $row 'idle_seconds')) {
                    $row._time - [double]$row.idle_seconds
                } else { $row._time }
                foreach ($timeout in $timeouts) {
                    $deadline = $idleAnchor + $timeout * 60
                    if ($deadline -gt $model.start -and $deadline -lt $model.end) {
                        Add-Point $timeline $deadline 90 'deadline' $model $null
                    }
                }
            }
        }
        foreach ($call in $model.calls) {
            Add-Point $timeline $call.start 10 'call_start' $model $call
            Add-Point $timeline $call.end 30 'call_end' $model $call
            foreach ($timeout in $timeouts) {
                $deadline = $call.end + $timeout * 60
                if ($deadline -gt $model.start -and $deadline -lt $model.end) {
                    Add-Point $timeline $deadline 90 'deadline' $model $null
                }
            }
        }
    }
    $gaps = @(Merge-Ranges $allGaps)
    $totals = @{}
    foreach ($timeout in $timeouts) {
        $totals[$timeout] = @{ idle = 0.0; residual = 0.0; episodes = 0; cost = 0.0; unknown_cost = 0 }
    }
    $observationSeconds = 0.0
    $duplicateGibSeconds = 0.0
    $activeOverlapSeconds = 0.0
    $copyDistribution = @{}
    $subagentCandidates = 0
    $subagentByKey = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    $previousTime = $null
    foreach ($entry in $timeline.GetEnumerator()) {
        $time = [double]$entry.Key
        if ($null -ne $previousTime -and $time -gt $previousTime) {
            $alive = @($models | Where-Object { $_.state.alive })
            $usable = @($alive | Where-Object { -not $_.state.in_gap -and $null -ne $_.state.memory })
            if ($usable.Count -gt 0) {
                $seconds = $time - $previousTime
                $middle = ($time + $previousTime) / 2
                $observationSeconds += $seconds
                $byKey = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
                $incompleteKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($model in $alive) {
                    $state = $model.state
                    if ($state.in_gap -or $null -eq $state.memory) {
                        if ($null -ne $state.key) { [void]$incompleteKeys.Add($state.key) }
                        foreach ($candidate in $state.uncertain_keys) { [void]$incompleteKeys.Add($candidate) }
                    }
                }
                foreach ($model in $usable) {
                    $state = $model.state
                    if ($null -eq $state.key) { continue }
                    if (-not $byKey.ContainsKey($state.key)) { $byKey[$state.key] = [Collections.Generic.List[object]]::new() }
                    $byKey[$state.key].Add($model)
                    if ($state.known_calls + $state.sampled_calls -eq 0) {
                        foreach ($timeout in $timeouts) {
                            if ($middle - $state.idle_start -gt $timeout * 60) {
                                $totals[$timeout].idle += $state.memory * $seconds
                            }
                        }
                    }
                }
                $overlap = $false
                foreach ($key in $byKey.Keys) {
                    if ($incompleteKeys.Contains($key)) { continue }
                    $copies = $byKey[$key]
                    $countKey = [string]$copies.Count
                    if (-not $copyDistribution.ContainsKey($countKey)) { $copyDistribution[$countKey] = 0.0 }
                    $copyDistribution[$countKey] += $seconds
                    if ($copies.Count -lt 2) { continue }
                    $sizes = @($copies | ForEach-Object { $_.state.memory })
                    $redundant = ($sizes | Measure-Object -Sum).Sum - ($sizes | Measure-Object -Maximum).Maximum
                    $duplicateGibSeconds += $redundant * $seconds
                    $keyStats[$key].duplicate_gib_seconds += $redundant * $seconds
                    if (@($copies | Where-Object { $_.state.known_calls + $_.state.sampled_calls -gt 0 }).Count -ge 2) { $overlap = $true }
                    foreach ($timeout in $timeouts) {
                        $remaining = @($copies | Where-Object { $_.state.retained[$timeout] } | ForEach-Object { $_.state.memory })
                        if ($remaining.Count -ge 2) {
                            $residual = ($remaining | Measure-Object -Sum).Sum - ($remaining | Measure-Object -Maximum).Maximum
                            $totals[$timeout].residual += $residual * $seconds
                        }
                    }
                }
                if ($overlap) { $activeOverlapSeconds += $seconds }
            }
        }

        foreach ($point in @($entry.Value | Sort-Object order)) {
            $model = $point.model
            $state = $model.state
            switch ($point.kind) {
                'life_start' { $state.alive = $true }
                'life_end' { $state.alive = $false }
                'gap_start' { $state.policy_known = $false; $state.in_gap = $true }
                'gap_end' { $state.in_gap = $false }
                'unknown_keys_start' { $state.uncertain_keys = $point.data }
                'unknown_keys_end' { $state.uncertain_keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
                'call_start' {
                    $call = $point.data
                    # An opening already required by the baseline does not restore the old key first.
                    if ($call.required -eq $true -and -not $call.baseline_fresh_opening -and
                        $call.name -ne 'reset_workspace' -and $state.policy_known -and $null -ne $state.key) {
                        foreach ($timeout in $timeouts) {
                            if (-not $state.retained[$timeout]) {
                                $totals[$timeout].episodes++
                                if ($loadCosts.ContainsKey($state.key)) {
                                    $totals[$timeout].cost += Get-Median $loadCosts[$state.key]
                                } else { $totals[$timeout].unknown_cost++ }
                                $state.retained[$timeout] = $true
                            }
                        }
                    }
                    $state.known_calls++
                    $state.idle_start = $time
                    $state.activity_known = $true
                }
                'call_end' {
                    $state.known_calls = [Math]::Max(0, $state.known_calls - 1)
                    $state.idle_start = $time
                    $state.activity_known = $true
                }
                'row' {
                    $row = $point.data
                    switch ($row.ev) {
                        'load' {
                            if ($row._transition_valid) {
                                $key = $row.workspace_key
                                if ($row.success -eq $true -and $null -ne $key -and -not $state.has_loaded) {
                                    $otherIdle = @($models | Where-Object {
                                        $_ -ne $model -and $_.state.alive -and
                                        [string]::Equals($_.state.key, [string]$key, [StringComparison]::Ordinal) -and
                                        $_.harness -eq $model.harness -and $_.state.known_calls + $_.state.sampled_calls -eq 0
                                    })
                                    if ($otherIdle.Count) {
                                        $subagentCandidates++
                                        if (-not $subagentByKey.ContainsKey([string]$key)) { $subagentByKey[[string]$key] = 0 }
                                        $subagentByKey[[string]$key]++
                                    }
                                }
                                Set-Resident $state $key ($row.success -eq $true) $time
                                if ($row.success -eq $true -and $null -ne $key) { $state.has_loaded = $true }
                            }
                            else {
                                Set-Resident $state $null $false $time
                                $state.policy_known = $false
                            }
                            $state.idle_start = $time
                            $state.activity_known = $true
                            $key = if ($row.Contains('requested_key') -and $null -ne $row.requested_key) {
                                [string]$row.requested_key
                            } elseif ($row.Contains('workspace_key')) { [string]$row.workspace_key } else { $null }
                            if ($null -ne $key -and $keyStats.ContainsKey($key)) {
                                $keyStats[$key].loads++
                                if ($row.Contains('success') -and $row.success -eq $true) {
                                    $keyStats[$key].successful_loads++
                                    if ($row.Contains('cache_hit') -and $row.cache_hit -eq $false) { $keyStats[$key].fresh_loads++ }
                                }
                            }
                        }
                        'reset' {
                            if ($row._transition_valid) {
                                Set-Resident $state $row.resident_key $false $time
                            }
                            else {
                                Set-Resident $state $null $false $time
                                $state.policy_known = $false
                                $Warnings.unknown_reset_outcomes++
                            }
                        }
                        'mem' {
                            if ($row.Contains('_snapshot_valid')) {
                                $state.memory = ([double]$row.memory_bytes + [double]$row.buildhost_bytes) / 1GB
                                $restore = -not $state.policy_known
                                Set-Resident $state $row.workspace_key $restore $time
                                $state.policy_known = -not $state.in_gap
                                $state.sampled_calls = [Math]::Max(0, [double]$row.active_calls - $state.known_calls)
                                if ($row.active_calls -eq 0) {
                                    $anchor = $time - [double]$row.idle_seconds
                                    $state.idle_start = if ($state.activity_known) { [Math]::Max($state.idle_start, $anchor) } else { $anchor }
                                }
                            } else {
                                if ($row.Contains('workspace_key') -and (Test-WorkspaceValue $row.workspace_key)) {
                                    Set-Resident $state $row.workspace_key $false $time
                                }
                                $state.memory = $null
                                $state.policy_known = $false
                            }
                        }
                    }
                }
            }
        }
        foreach ($model in $models) {
            $state = $model.state
            if ($state.alive -and $null -ne $state.key -and $state.known_calls + $state.sampled_calls -eq 0) {
                foreach ($timeout in $timeouts) {
                    if ($time - $state.idle_start -ge $timeout * 60) { $state.retained[$timeout] = $false }
                }
            }
        }
        $previousTime = $time
    }
    $hours = $observationSeconds / 3600
    $totalRows = @($Rows)
    $processResults = @($models | ForEach-Object {
        @{
            process = $_.label; harness = $_.harness; start = Format-Time $_.start; end = Format-Time $_.end
            observed_lifetime_seconds = $_.end - $_.start; left_censored = -not $_.started
            right_censored = -not $_.exited; unfinished_calls = @($_.calls | Where-Object { -not $_.completed }).Count
        }
    })
    $policyMetricsAvailable = $hours -gt 0 -and $Warnings.unknown_workspace_requirement -eq 0 -and $Warnings.missing_call_fields -eq 0 -and $Warnings.invalid_call_intervals -eq 0
    $thresholdResults = @($timeouts | ForEach-Object {
        $total = $totals[$_]
        @{
            minutes = $_
            idle_gib_hours = if ($hours -gt 0) { $total.idle / 3600 } else { $null }
            idle_mean_gib = if ($hours -gt 0) { $total.idle / $observationSeconds } else { $null }
            reload_metrics_available = $policyMetricsAvailable
            reload_episodes = if ($policyMetricsAvailable) { $total.episodes } else { $null }
            known_reload_episodes = $total.episodes
            estimated_reload_seconds = if ($policyMetricsAvailable -and $total.unknown_cost -eq 0) { $total.cost } else { $null }
            known_reload_seconds = $total.cost; unknown_cost_episodes = $total.unknown_cost
            residual_metrics_available = $policyMetricsAvailable
            residual_duplicate_gib_hours = if ($policyMetricsAvailable) { $total.residual / 3600 } else { $null }
            residual_duplicate_mean_gib = if ($policyMetricsAvailable) { $total.residual / $observationSeconds } else { $null }
        }
    })
    $workspaceResults = @($keyLabels.Keys | Sort-Object { [int]$keyLabels[$_].Substring(1) } | ForEach-Object {
        @{
            key = $keyLabels[$_]; load_attempts = $keyStats[$_].loads
            successful_loads = $keyStats[$_].successful_loads; fresh_loads = $keyStats[$_].fresh_loads
            median_fresh_load_seconds = if ($loadCosts.ContainsKey($_)) { Get-Median $loadCosts[$_] } else { $null }
            duplicate_gib_hours = if ($hours -gt 0) { $keyStats[$_].duplicate_gib_seconds / 3600 } else { $null }
        }
    })
    $loadSummary = @($models | ForEach-Object {
        $harness = $_.harness
        $loads = @($_.rows | Where-Object { $_.v -eq 1 -and $_.ev -eq 'load' })
        $resets = @($_.rows | Where-Object { $_.v -eq 1 -and $_.ev -eq 'reset' })
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $repeated = 0
        foreach ($load in $loads) {
            $key = if ($load.Contains('requested_key')) { $load.requested_key } else { $null }
            if ($null -ne $key -and -not $seen.Add([string]$key)) { $repeated++ }
        }
        @{
            harness = $harness; loads = $loads.Count
            cache_hits = @($loads | Where-Object { $_.Contains('cache_hit') -and $_.cache_hit -eq $true }).Count
            repeated_loads = $repeated; resets = $resets.Count
            failed_resets = @($resets | Where-Object { $_.Contains('success') -and $_.success -eq $false }).Count
        }
    } | Group-Object harness | ForEach-Object {
        @{
            harness = $_.Name
            loads = ($_.Group.loads | Measure-Object -Sum).Sum
            cache_hits = ($_.Group.cache_hits | Measure-Object -Sum).Sum
            repeated_loads = ($_.Group.repeated_loads | Measure-Object -Sum).Sum
            resets = ($_.Group.resets | Measure-Object -Sum).Sum
            failed_resets = ($_.Group.failed_resets | Measure-Object -Sum).Sum
        }
    })
    return @{
        machine = $Label
        observation = @{
            start = if ($models.Count) { Format-Time (($models.start | Measure-Object -Minimum).Minimum) } else { $null }
            end = if ($models.Count) { Format-Time (($models.end | Measure-Object -Maximum).Maximum) } else { $null }
            usable_hours = $hours; gap_hours = (($gaps | ForEach-Object { $_.end - $_.start } | Measure-Object -Sum).Sum) / 3600
            gap_count = $gaps.Count; memory_metrics_available = $hours -gt 0
            right_censored_processes = @($models | Where-Object { -not $_.exited }).Count
            left_censored_processes = @($models | Where-Object { -not $_.started }).Count
            warnings = $Warnings
        }
        thresholds = $thresholdResults
        duplication = @{
            gib_hours = if ($hours -gt 0) { $duplicateGibSeconds / 3600 } else { $null }
            mean_gib = if ($hours -gt 0) { $duplicateGibSeconds / $observationSeconds } else { $null }
            active_overlap_hours = if ($hours -gt 0) { $activeOverlapSeconds / 3600 } else { $null }
        }
        composition = @{
            process_count = $models.Count; processes = $processResults
            harnesses = @($processResults | Group-Object harness | ForEach-Object {
                @{ harness = $_.Name; processes = $_.Count; observed_process_hours = ($_.Group.observed_lifetime_seconds | Measure-Object -Sum).Sum / 3600 }
            })
            copy_distribution = @($copyDistribution.Keys | Sort-Object { [int]$_ } | ForEach-Object {
                @{ copies = [int]$_; workspace_hours = $copyDistribution[$_] / 3600 }
            })
        }
        subagent_heuristic = @{
            candidates = $subagentCandidates; establishes_parentage = $false
            workspaces = @($subagentByKey.Keys | ForEach-Object { @{ key = $keyLabels[$_]; candidates = $subagentByKey[$_] } })
        }
        tools = @(Get-ToolSummary $totalRows)
        workspace_operations = $loadSummary
        workspaces = $workspaceResults
    }
}

$machines = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
for ($directoryIndex = 0; $directoryIndex -lt $InputDirectory.Count; $directoryIndex++) {
    $label = $Machine[$directoryIndex]
    if ([string]::IsNullOrWhiteSpace($label) -or $label -match '[\r\n]') { throw 'Machine labels must be nonempty single-line values.' }
    if (-not $machines.ContainsKey($label)) {
        $machines[$label] = @{
            rows = [Collections.Generic.List[object]]::new()
            records = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
            files = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            warnings = @{
                duplicate_records = 0; unsupported_version_events = 0; malformed_records = 0; torn_tails = 0
                sequence_gaps = 0; heartbeat_gaps = 0; incomplete_snapshots = 0; processes_without_memory = 0
                missing_call_fields = 0; unknown_workspace_requirement = 0; invalid_call_intervals = 0; unknown_reset_outcomes = 0; clock_regressions = 0; incomplete_workspace_transitions = 0
            }
        }
    }
    $machineData = $machines[$label]
    if (-not (Test-Path -LiteralPath $InputDirectory[$directoryIndex] -PathType Container)) { throw 'An input directory does not exist.' }
    foreach ($file in @(Get-ChildItem -LiteralPath $InputDirectory[$directoryIndex] -File -Filter 'usage-*.jsonl' | Sort-Object Name)) {
        if (-not $machineData.files.Add($file.FullName)) { continue }
        $reader = [IO.StreamReader]::new($file.FullName, [Text.Encoding]::UTF8, $true)
        try {
            while ($null -ne ($line = $reader.ReadLine())) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try {
                    try { $row = ConvertFrom-Json -InputObject $line -AsHashtable }
                    catch {
                        if ($reader.EndOfStream) { $machineData.warnings.torn_tails++ } else { $machineData.warnings.malformed_records++ }
                        continue
                    }
                    $time = if ($row.Contains('ts')) { Get-Time $row.ts } else { $null }
                    $sequence = if ($row.Contains('seq')) { Get-Number $row 'seq' } else { $null }
                    if ($null -eq $time -or $null -eq $sequence -or $sequence -lt 1 -or $sequence % 1 -ne 0 -or
                        -not $row.Contains('proc') -or [string]::IsNullOrEmpty([string]$row.proc) -or
                        -not $row.Contains('ev') -or -not $row.Contains('v') -or -not $row.Contains('harness')) {
                        throw 'Incomplete event envelope.'
                    }
                }
                catch {
                    $machineData.warnings.malformed_records++
                    continue
                }
                $identity = [string]$row.proc + ':' + [long]$sequence
                $signature = ConvertTo-Json -InputObject $row -Compress -Depth 32
                if ($machineData.records.ContainsKey($identity)) {
                    if ($machineData.records[$identity] -cne $signature) { throw 'Conflicting records have the same process and sequence; use consistent exports.' }
                    $machineData.warnings.duplicate_records++
                    continue
                }
                $machineData.records[$identity] = $signature
                $row['_time'] = [double]$time
                $row['seq'] = [long]$sequence
                $row['proc'] = [string]$row.proc
                if ($row.v -ne 1) { $machineData.warnings.unsupported_version_events++ }
                $machineData.rows.Add($row)
            }
        }
        finally { $reader.Dispose() }
    }
}
$results = @($machines.Keys | Sort-Object | ForEach-Object {
    $data = $machines[$_]
    Measure-Machine $_ @($data.rows | Sort-Object { $_._time }, { $_.seq }) $data.warnings
})
$report = [ordered]@{
    schema_version = 1
    method = 'Per-process memory coverage; machine hours are its union; incomplete duplicate groups omitted per key; piecewise-constant snapshots; half-open calls; estimated reload costs.'
    strategy = 'Owner decision from measured results; no fixed benefit or latency thresholds.'
    machines = $results
}
$absoluteOutput = [IO.Path]::GetFullPath($OutputPath)
$parentDirectory = [IO.Path]::GetDirectoryName($absoluteOutput)
[IO.Directory]::CreateDirectory($parentDirectory) | Out-Null
$json = ConvertTo-Json -InputObject $report -Depth 32
[IO.File]::WriteAllText($absoluteOutput + '.json', $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add('# MCP usage statistics')
$markdown.Add('')
$markdown.Add('Each machine is evaluated separately. Workspace labels are local to its report. Raw paths and keys are not exported.')
$markdown.Add('Memory values use the last valid snapshot. Gaps exclude their process; duplicate groups with an incomplete copy are omitted. A missing exit gives a lower lifetime bound.')
$markdown.Add('Usable hours are the union of usable process observations. Reported gap hours can overlap those hours when another process remains observable.')
$markdown.Add('Reload costs are estimates from median successful fresh loads. The report does not select a strategy.')
foreach ($result in $results) {
    $markdown.Add('')
    $markdown.Add('## ' + ($result.machine -replace '[#|<>]', '_'))
    $markdown.Add('')
    $markdown.Add(('Usable observation: {0:N3} h; gaps: {1:N3} h; processes: {2}; right-censored: {3}.' -f
        $result.observation.usable_hours, $result.observation.gap_hours, $result.composition.process_count, $result.observation.right_censored_processes))
    $markdown.Add('')
    $markdown.Add('| Timeout (min) | Idle (GiB·h) | Idle mean (GiB) | Reload episodes | Estimated reload (s) | Unknown cost episodes | Residual duplicates (GiB·h) |')
    $markdown.Add('|---|---|---|---|---|---|---|')
    foreach ($threshold in $result.thresholds) {
        $values = @($threshold.minutes, $threshold.idle_gib_hours, $threshold.idle_mean_gib, $threshold.reload_episodes,
            $threshold.estimated_reload_seconds, $threshold.unknown_cost_episodes, $threshold.residual_duplicate_gib_hours)
        $rendered = @($values | ForEach-Object { if ($null -eq $_) { 'unknown' } else { ([double]$_).ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) } })
        $markdown.Add('| ' + ($rendered -join ' | ') + ' |')
    }
    $duplicateText = if ($null -eq $result.duplication.gib_hours) { 'unknown' } else { ([double]$result.duplication.gib_hours).ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }
    $markdown.Add('')
    $markdown.Add('Duplicate memory: ' + $duplicateText + ' GiB·h. Subagent candidates are a same-harness heuristic, not verified parentage.')
    $markdown.Add('Detailed workspace, process, harness, tool and data-quality aggregates are in the accompanying JSON.')
}
[IO.File]::WriteAllLines($absoluteOutput + '.md', $markdown, [Text.UTF8Encoding]::new($false))
$report
