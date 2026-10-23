<#
.SYNOPSIS
    Measures load_workspace and the first semantic query of a published RoslynMcpServer
    on one solution, without any server change (D/E0/task-00 protocol).

.DESCRIPTION
    Each run starts a fresh server process over stdio, calls load_workspace and then
    find_symbol_definition, and stops the process. The last run also repeats
    load_workspace in the same process (RAM hit). Run 1 is warmup.

    Client-side time comes from a Stopwatch around every request. Stage estimates come
    from lines the server already writes to logs/mcp-*.log next to the exe:
    the inbound tools/call, "Analyzer provenance capture finished" (ReplayMs), the first
    "Disk watcher started", "Loaded Roslyn workspace", and "request handler completed".
    These are estimates from log timestamps, not measured spans.
    Server 1.5.8+ also logs design-time build and declaration-search project timings.
    The report includes their full data in runs.json and top projects in summary.md.
    Server 1.5.11+ also logs first-chance exceptions raised during the declaration search
    (type, searched project, file or I/O message, count); summary.md lists the largest groups.

    Output folder: summary.md, runs.json, and per-run redacted log excerpts and responses.
    The Git repository root of the workspace (or the workspace folder outside Git) is replaced
    with <repo>, this server checkout with <server-repo>, the user profile, user name path
    segments and DOMAIN\user with <user>, a custom NUGET_PACKAGES folder with <nuget>, and the
    machine name with <machine>. Environment variables and file contents are not written.
    Afterwards the text files of the report are scanned for absolute paths that are still left
    (outside Program Files and Windows) and the count is printed with examples. The .nettrace
    file of a trace run cannot be redacted.

.PARAMETER Workspace
    .sln, .slnx or .csproj to load.

.PARAMETER ServerExe
    Published RoslynMcpServer.exe. Default: bin\Release\net10.0\win-x64\publish next to this repo.

.PARAMETER SymbolName
    Type name from the solution for the first find_symbol_definition. Omit to skip the query.

.PARAMETER Runs
    Fresh-process runs, including the warmup run. Default 4.

.PARAMETER AffinityCount
    Restrict the server to the first N logical CPUs (BuildHost inherits the mask). 0 = no limit.

.PARAMETER TracerExe
    dotnet-trace executable. Adds one run after the measured runs that records CPU samples and
    runtime exceptions of the first find_symbol_definition into run-N-find.nettrace. The trace
    ends when the server process exits, so the file also covers shutdown. Its times are reported
    separately and are not part of the medians. Requires -SymbolName.

.PARAMETER Note
    Free text for the report: cold/warm cache, antivirus state, power plan.

.EXAMPLE
    .\scripts\Measure-WorkspaceLoad.ps1 -Workspace D:\src\App\App.slnx -SymbolName MainViewModel -Platform x64

.EXAMPLE
    .\scripts\Measure-WorkspaceLoad.ps1 -Workspace D:\src\App\App.slnx -SymbolName MainViewModel -AffinityCount 4 -Note "4 cores, warm"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Workspace,

    [string]$ServerExe,

    [string]$SymbolName,

    [string]$Configuration,

    [string]$Platform,

    [string]$TargetFramework,

    [ValidateRange(1, 50)]
    [int]$Runs = 4,

    [switch]$SkipRamHit,

    [switch]$FullOutput,

    [ValidateRange(0, 1024)]
    [int]$AffinityCount = 0,

    [ValidateRange(10, 86400)]
    [int]$CallTimeoutSeconds = 3600,

    [ValidateRange(0, 600)]
    [int]$PauseSeconds = 3,

    [string]$Note = '',

    [string]$TracerExe,

    [string]$OutDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$Workspace = [IO.Path]::GetFullPath($Workspace)
if (-not (Test-Path -LiteralPath $Workspace -PathType Leaf)) {
    throw "Workspace file not found: $Workspace"
}
if (-not $ServerExe) {
    $ServerExe = Join-Path $PSScriptRoot '..\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe'
}
$ServerExe = [IO.Path]::GetFullPath($ServerExe)
if (-not (Test-Path -LiteralPath $ServerExe -PathType Leaf)) {
    throw "Server exe not found: $ServerExe"
}
if ($TracerExe) {
    if (-not $SymbolName) { throw '-TracerExe needs -SymbolName: the trace covers the first find_symbol_definition.' }
    $tracerCommand = Get-Command -Name $TracerExe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $tracerCommand) { throw "dotnet-trace not found: $TracerExe" }
    $TracerExe = $tracerCommand.Source
}
if (-not $OutDirectory) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutDirectory = Join-Path (Get-Location) ("workspace-load-measure\" + [IO.Path]::GetFileNameWithoutExtension($Workspace) + "-$stamp")
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null

$logDirectory = Join-Path (Split-Path $ServerExe) 'logs'
$workspaceRoot = Split-Path $Workspace
$utf8NoBom = New-Object Text.UTF8Encoding($false)
$redactionPairs = @()

# The solution may sit deep inside a monorepo whose projects live above its folder, so the Git
# repository root is redacted, not only the solution folder. Longer values go first: a repository
# under the user profile must not be cut by the shorter profile replacement.
function Get-RedactionPairs([string]$WorkspaceDirectory, [string]$ServerRepository) {
    $repositoryRoot = $WorkspaceDirectory
    try {
        $topLevel = & git -C $WorkspaceDirectory rev-parse --show-toplevel 2>$null
        if ($LASTEXITCODE -eq 0 -and $topLevel) { $repositoryRoot = [IO.Path]::GetFullPath(([string]$topLevel).Trim()) }
    }
    catch { }
    $pairs = @(
        @($repositoryRoot, '<repo>'),
        @($WorkspaceDirectory, '<repo>'),
        @($ServerRepository, '<server-repo>'),
        @($env:NUGET_PACKAGES, '<nuget>'),
        @($env:USERPROFILE, '<user>'),
        @("$env:USERDOMAIN\$env:USERNAME", '<user>'),
        @($env:COMPUTERNAME, '<machine>')
    )
    return @($pairs | Where-Object { -not [string]::IsNullOrEmpty($_[0]) -and $_[0] -ne '\' } |
        Sort-Object { $_[0].Length } -Descending)
}

function Get-RedactedText([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    foreach ($pair in $redactionPairs) {
        $value = $pair[0].TrimEnd('\', '/')
        foreach ($variant in @($value, $value.Replace('\', '\\'), $value.Replace('\', '/'))) {
            $Text = [regex]::Replace($Text, [regex]::Escape($variant), $pair[1], 'IgnoreCase')
        }
    }
    # Only as a whole path segment: a short user name also occurs inside unrelated words.
    if ($env:USERNAME -and $env:USERNAME.Length -ge 2) {
        $Text = [regex]::Replace($Text, '(?<=[\\/])' + [regex]::Escape($env:USERNAME) + '(?=[\\/])', '<user>', 'IgnoreCase')
    }
    return $Text
}

# Absolute paths that survived redaction, outside Program Files and Windows, which name no user data.
function Find-UnredactedPaths([string]$Directory) {
    $allowedRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:windir) | Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\') + '\' }
    # Logs embed JSON, so a path may appear with doubled backslashes in any file.
    $allowed = @($allowedRoots | ForEach-Object { $_; $_.Replace('\', '\\') })
    $found = New-Object Collections.Generic.List[string]
    foreach ($file in @(Get-ChildItem -LiteralPath $Directory -File | Where-Object { $_.Extension -in '.md', '.txt', '.json' })) {
        $text = [IO.File]::ReadAllText($file.FullName)
        # Drive paths, and UNC paths in plain or JSON-escaped form (\\server or \\\\server). A UNC path
        # starts a token; inside one (<repo>\\src, a==\\sqlite3) the backslashes are path separators.
        $pattern = '(?<![A-Za-z])[A-Za-z]:[\\/][^\s"''<>|,;]*|(?<=^|[\s"''(\[])\\\\(?:\\\\)?[A-Za-z0-9._-]+\\[^\s"''<>|,;]*'
        foreach ($match in [regex]::Matches($text, $pattern, 'Multiline')) {
            # Match from the original text: the pattern stops at the space in "Program Files".
            $rest = $text.Substring($match.Index)
            if (@($allowed | Where-Object { $rest.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) { continue }
            $found.Add("$($file.Name): $($match.Value)")
        }
    }
    return $found
}

function Get-LogOffsets {
    $offsets = @{}
    if (Test-Path -LiteralPath $logDirectory) {
        foreach ($file in Get-ChildItem -LiteralPath $logDirectory -Filter 'mcp-*.log') {
            $offsets[$file.FullName] = $file.Length
        }
    }
    return $offsets
}

function Read-NewLogLines($Offsets) {
    $lines = New-Object Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $logDirectory)) { return $lines }
    foreach ($file in Get-ChildItem -LiteralPath $logDirectory -Filter 'mcp-*.log' | Sort-Object Name) {
        $start = 0L
        if ($Offsets.ContainsKey($file.FullName)) { $start = [long]$Offsets[$file.FullName] }
        if ($file.Length -le $start) { continue }
        $stream = [IO.File]::Open($file.FullName, 'Open', 'Read', 'ReadWrite')
        try {
            $null = $stream.Seek($start, 'Begin')
            $reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)
            foreach ($line in ($reader.ReadToEnd() -split "`r?`n")) {
                if ($line.Length -gt 0) { $lines.Add($line) }
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    return $lines
}

$script:proc = $null
$script:pendingRead = $null
$script:nextId = 1
$script:cpuSamples = $null
$script:lastSampleUtc = [DateTime]::MinValue

function Update-CpuSamples {
    if ($null -eq $script:cpuSamples) { return }
    if (([DateTime]::UtcNow - $script:lastSampleUtc).TotalMilliseconds -lt 2000) { return }
    $script:lastSampleUtc = [DateTime]::UtcNow
    $snapshot = Get-ProcessTreeCpu $script:proc.Id
    foreach ($id in $snapshot.Children.Keys) {
        $script:cpuSamples[$id] = $snapshot.Children[$id]
    }
}

function Send-Message($Message) {
    $json = $Message | ConvertTo-Json -Compress -Depth 20
    $bytes = $utf8NoBom.GetBytes($json + "`n")
    $script:proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $script:proc.StandardInput.BaseStream.Flush()
}

function Read-Message([DateTime]$Deadline) {
    while ($true) {
        if ($null -eq $script:pendingRead) {
            $script:pendingRead = $script:proc.StandardOutput.ReadLineAsync()
        }
        while (-not $script:pendingRead.IsCompleted) {
            if ([DateTime]::UtcNow -gt $Deadline) { return $null }
            if ($script:proc.HasExited -and -not $script:pendingRead.IsCompleted) {
                Start-Sleep -Milliseconds 200
                if (-not $script:pendingRead.IsCompleted) { return $null }
            }
            $null = $script:pendingRead.AsyncWaitHandle.WaitOne(500)
            Update-CpuSamples
        }
        $line = $script:pendingRead.Result
        $script:pendingRead = $null
        if ($null -eq $line) { return $null }
        if ($line.Trim().Length -eq 0) { continue }
        try {
            return ($line | ConvertFrom-Json)
        }
        catch {
            continue
        }
    }
}

function Invoke-Request([string]$Method, $Params) {
    $id = $script:nextId
    $script:nextId++
    $request = [ordered]@{ jsonrpc = '2.0'; id = $id; method = $Method }
    if ($null -ne $Params) { $request.params = $Params }
    $deadline = [DateTime]::UtcNow.AddSeconds($CallTimeoutSeconds)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Send-Message $request
    while ($true) {
        $message = Read-Message $deadline
        if ($null -eq $message) {
            $watch.Stop()
            $state = 'no-response'
            if ([DateTime]::UtcNow -gt $deadline) { $state = 'timeout' }
            elseif ($script:proc.HasExited) { $state = 'server-exited' }
            return [pscustomobject]@{ Id = $id; Ms = $watch.Elapsed.TotalMilliseconds; State = $state; Result = $null; Error = $null }
        }
        $names = @($message.PSObject.Properties | ForEach-Object Name)
        if ($names -contains 'method') {
            if ($names -contains 'id') {
                $reply = [ordered]@{ jsonrpc = '2.0'; id = $message.id }
                if ($message.method -eq 'ping') { $reply.result = @{} }
                else { $reply.error = @{ code = -32601; message = 'Method not supported by the measurement client' } }
                Send-Message $reply
            }
            continue
        }
        if (($names -contains 'id') -and $message.id -eq $id) {
            $watch.Stop()
            $state = 'ok'
            $errorText = $null
            if ($names -contains 'error') {
                $state = 'rpc-error'
                $errorText = [string]$message.error.message
            }
            return [pscustomobject]@{ Id = $id; Ms = $watch.Elapsed.TotalMilliseconds; State = $state; Result = $message.result; Error = $errorText }
        }
    }
}

function Get-ToolText($Response) {
    if ($null -eq $Response.Result) { return '' }
    $content = $Response.Result.PSObject.Properties['content']
    if ($null -eq $content) { return '' }
    $parts = @()
    foreach ($item in @($content.Value)) {
        if ($null -ne $item -and $item.PSObject.Properties['text']) { $parts += [string]$item.text }
    }
    return ($parts -join "`n")
}

function Get-ToolIsError($Response) {
    if ($null -eq $Response.Result) { return $true }
    $flag = $Response.Result.PSObject.Properties['isError']
    return ($null -ne $flag -and [bool]$flag.Value)
}

function Invoke-Tool([string]$Name, $Arguments) {
    $response = Invoke-Request 'tools/call' ([ordered]@{ name = $Name; arguments = $Arguments })
    $text = Get-ToolText $response
    $state = $response.State
    if ($state -eq 'ok' -and (Get-ToolIsError $response)) { $state = 'tool-error' }
    return [pscustomobject]@{ Id = $response.Id; Ms = $response.Ms; State = $state; Text = $text; Error = $response.Error }
}

$linePattern = '^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<lvl>\w{3})\] (?<sid>\[[^\]]+\]) (?<msg>.*)$'

function ConvertTo-LogEntries($Lines, [string]$Marker) {
    $parsed = New-Object Collections.Generic.List[object]
    foreach ($line in $Lines) {
        $m = [regex]::Match($line, $linePattern)
        if (-not $m.Success) { continue }
        $parsed.Add([pscustomobject]@{
            Time = [DateTimeOffset]::ParseExact($m.Groups['ts'].Value, 'yyyy-MM-dd HH:mm:ss.fff zzz', [Globalization.CultureInfo]::InvariantCulture)
            Level = $m.Groups['lvl'].Value
            Session = $m.Groups['sid'].Value
            Message = $m.Groups['msg'].Value
            Raw = $line
        })
    }
    $session = $null
    foreach ($entry in $parsed) {
        if ($entry.Message.Contains($Marker)) { $session = $entry.Session; break }
    }
    if ($null -eq $session) { return @() }
    return @($parsed | Where-Object { $_.Session -eq $session })
}

function Get-StageEstimate($Entries, [int]$RequestId) {
    $result = [ordered]@{
        handlerMs = $null; openMs = $null; replayMs = $null; postOpenMs = $null; watchersMs = $null
        responseMs = $null; projects = $null; diagLines = $null; workingSetMb = $null
        captureStatus = $null; msbuildFailures = 0; msbuildWarnings = 0; watcherLines = 0
        designTime = $null; searchProjects = @(); searchSummaries = @(); searchOutsideLoopMs = $null
        searchExceptions = @(); missingSourceFiles = $null
    }
    $start = -1
    for ($i = 0; $i -lt $Entries.Count; $i++) {
        if ($Entries[$i].Message -match ('^MCP \u2190 request id=' + $RequestId + ' method=tools/call ')) { $start = $i; break }
    }
    if ($start -lt 0) { return [pscustomobject]$result }
    $t0 = $Entries[$start].Time
    $capture = $null; $firstWatcher = $null; $loaded = $null; $done = $null
    for ($i = $start + 1; $i -lt $Entries.Count; $i++) {
        $e = $Entries[$i]
        if ($e.Message -match '^MCP \u2190 request id=') { break }
        if ($e.Message -match '^workspace_(?<kind>load_designtime|missing_source_files|search_project|search_summary|search_exceptions) (?<json>\{.*\})$') {
            $timingKind = $Matches['kind']
            try {
                $timing = (Get-RedactedText $Matches['json']) | ConvertFrom-Json
                switch ($timingKind) {
                    'load_designtime' { $result.designTime = $timing }
                    'missing_source_files' { $result.missingSourceFiles = $timing }
                    'search_project' { $result.searchProjects += $timing }
                    'search_summary' { $result.searchSummaries += $timing }
                    'search_exceptions' { $result.searchExceptions += $timing }
                }
            }
            catch { Write-Warning "Cannot parse $timingKind timing for request ${RequestId}: $($_.Exception.Message)" }
            continue
        }
        if ($null -eq $capture -and $e.Message -match 'Analyzer provenance capture finished: Status="?(?<st>[^",]+)"?.*ReplayMs=(?<r>\d+(?:[.,]\d+)?)') {
            $capture = $e
            $result.captureStatus = $Matches['st']
            $result.replayMs = [double]::Parse($Matches['r'].Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture)
        }
        elseif ($e.Message.StartsWith('Disk watcher started')) {
            $result.watcherLines++
            if ($null -eq $firstWatcher) { $firstWatcher = $e }
        }
        elseif ($null -eq $loaded -and $e.Message.StartsWith('Loaded Roslyn workspace from')) { $loaded = $e }
        elseif ($e.Message -match '^MSBuildWorkspace "?Failure') { $result.msbuildFailures++ }
        elseif ($e.Message -match '^MSBuildWorkspace "?Warning') { $result.msbuildWarnings++ }
        elseif ($e.Message -match 'working set: (?<ws>\d+(?:[.,]\d+)?) MB \(context: workspace_load\)') {
            $result.workingSetMb = [double]::Parse($Matches['ws'].Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture)
        }
        elseif ($e.Message -match '^MCP tool LoadWorkspace \| status=.*?projects=(?<p>\d+).*?workspace_diag_lines=(?<d>\d+)') {
            $result.projects = [int]$Matches['p']
            $result.diagLines = [int]$Matches['d']
        }
        elseif ($e.Message -match "method 'tools/call' request handler completed in (?<ms>\d+(?:[.,]\d+)?)ms") {
            $done = $e
            $result.handlerMs = [double]::Parse($Matches['ms'].Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture)
            break
        }
    }
    if ($null -ne $capture -and $null -ne $result.replayMs) {
        $result.openMs = ($capture.Time - $t0).TotalMilliseconds - $result.replayMs
    }
    if ($null -ne $capture -and $null -ne $firstWatcher) {
        $result.postOpenMs = ($firstWatcher.Time - $capture.Time).TotalMilliseconds
    }
    if ($null -ne $firstWatcher -and $null -ne $loaded) {
        $result.watchersMs = ($loaded.Time - $firstWatcher.Time).TotalMilliseconds
    }
    if ($null -ne $loaded -and $null -ne $done) {
        $result.responseMs = ($done.Time - $loaded.Time).TotalMilliseconds
    }
    if ($null -ne $result.handlerMs -and $result.searchSummaries.Count -gt 0) {
        $loopMs = ($result.searchSummaries | Measure-Object -Property elapsedMs -Sum).Sum
        if ($loopMs -le $result.handlerMs) { $result.searchOutsideLoopMs = $result.handlerMs - $loopMs }
    }
    return [pscustomobject]$result
}

function Get-ProcessTreeCpu([int]$RootId) {
    $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
    $byParent = @{}
    foreach ($p in $all) {
        $key = [int]$p.ParentProcessId
        if (-not $byParent.ContainsKey($key)) { $byParent[$key] = New-Object Collections.Generic.List[object] }
        $byParent[$key].Add($p)
    }
    $tree = @{}
    $queue = New-Object Collections.Generic.Queue[int]
    $queue.Enqueue($RootId)
    while ($queue.Count -gt 0) {
        $parent = $queue.Dequeue()
        if (-not $byParent.ContainsKey($parent)) { continue }
        foreach ($child in $byParent[$parent]) {
            $childId = [int]$child.ProcessId
            if ($tree.ContainsKey($childId) -or $childId -eq $RootId) { continue }
            $tree[$childId] = $child
            $queue.Enqueue($childId)
        }
    }
    $root = $all | Where-Object { [int]$_.ProcessId -eq $RootId } | Select-Object -First 1
    $rootCpu = 0.0
    if ($null -ne $root) { $rootCpu = ([double]$root.KernelModeTime + [double]$root.UserModeTime) / 10000 }
    $children = @{}
    foreach ($id in $tree.Keys) {
        $c = $tree[$id]
        $children[$id] = [pscustomobject]@{
            Name = [string]$c.Name
            CpuMs = ([double]$c.KernelModeTime + [double]$c.UserModeTime) / 10000
        }
    }
    return [pscustomobject]@{ RootCpuMs = $rootCpu; Children = $children }
}

function Measure-ToolCall([string]$Name, $Arguments) {
    $before = Get-ProcessTreeCpu $script:proc.Id
    $script:cpuSamples = @{}
    $script:lastSampleUtc = [DateTime]::MinValue
    try {
        $call = Invoke-Tool $Name $Arguments
    }
    finally {
        $after = Get-ProcessTreeCpu $script:proc.Id
        foreach ($id in $after.Children.Keys) { $script:cpuSamples[$id] = $after.Children[$id] }
        $merged = [pscustomobject]@{ RootCpuMs = $after.RootCpuMs; Children = $script:cpuSamples }
        $script:cpuSamples = $null
    }
    return [pscustomobject]@{ Call = $call; Cpu = (Get-CpuDelta $before $merged $call.Ms) }
}

function Get-CpuDelta($Before, $After, [double]$WallMs) {
    $childCpu = 0.0
    $names = @()
    foreach ($id in $After.Children.Keys) {
        $now = $After.Children[$id]
        $was = 0.0
        if ($Before.Children.ContainsKey($id)) { $was = $Before.Children[$id].CpuMs }
        $childCpu += ($now.CpuMs - $was)
        $names += $now.Name
    }
    $serverCpu = $After.RootCpuMs - $Before.RootCpuMs
    $cores = $null
    if ($WallMs -gt 0) { $cores = [Math]::Round(($serverCpu + $childCpu) / $WallMs, 2) }
    return [pscustomobject]@{
        serverCpuMs = [Math]::Round($serverCpu)
        childCpuMs = [Math]::Round($childCpu)
        busyCores = $cores
        children = (($names | Sort-Object -Unique) -join ', ')
    }
}

function Stop-ServerTree([int]$ProcessId) {
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue | ForEach-Object { [int]$_.ProcessId })
    try { $script:proc.StandardInput.Close() } catch { }
    if (-not $script:proc.WaitForExit(20000)) {
        & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null
        $null = $script:proc.WaitForExit(5000)
    }
    $leftover = 0
    foreach ($child in $children) {
        $alive = Get-Process -Id $child -ErrorAction SilentlyContinue
        if ($null -ne $alive) {
            $leftover++
            & taskkill.exe /PID $child /T /F 2>&1 | Out-Null
        }
    }
    return $leftover
}

function Get-Median($Values) {
    $sorted = @($Values | Where-Object { $null -ne $_ } | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $mid = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$mid] }
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
}

function Format-Ms($Value) {
    if ($null -eq $Value) { return 'n/a' }
    return ([Math]::Round([double]$Value)).ToString([Globalization.CultureInfo]::InvariantCulture)
}

function Format-SearchSummaryLine($Summary) {
    $line = "Search $($Summary.searchId): $($Summary.outcome), $(Format-Ms $Summary.elapsedMs) ms, $($Summary.completedProjects)/$($Summary.projects) projects completed"
    # GC fields exist from server 1.5.12; the counters are process-wide, so concurrent work adds to them.
    if ($Summary.PSObject.Properties['serverGc']) {
        $gcMode = 'workstation'
        if ($Summary.serverGc) { $gcMode = 'server' }
        $line += "; $gcMode GC, pauses $(Format-Ms $Summary.gcPauseMs) ms, collections gen0/1/2 $(@($Summary.gcCollections) -join '/')"
    }
    return $line + '.'
}

# Roslyn's index cache of this solution from any RoslynMcpServer install (server 1.5.14+), before the runs:
# folders <exe>-<path checksum>\<solution file, first 20 chars>-<path checksum>. With a cache the warmup run
# is already warm, so the report says whether one existed.
function Get-IndexCacheState([string]$SolutionPath) {
    $root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) (Join-Path 'Microsoft' (Join-Path 'VisualStudio' (Join-Path 'Roslyn' 'Cache')))
    $leaf = Split-Path $SolutionPath -Leaf
    $folders = @()
    if (Test-Path -LiteralPath $root) {
        $folders = @(Get-ChildItem -LiteralPath $root -Directory | Where-Object { $_.Name -like 'RoslynMcpServer*' } | ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -Directory | Where-Object {
                $cut = $_.Name.LastIndexOf('-')
                $cut -gt 0 -and $leaf.StartsWith($_.Name.Substring(0, $cut), [StringComparison]::OrdinalIgnoreCase)
            }
        })
    }
    $bytes = 0L
    foreach ($folder in $folders) { $bytes += [long](Get-ChildItem -LiteralPath $folder.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum }
    return "$($folders.Count) folders, $([Math]::Round($bytes / 1MB, 1)) MB"
}

# Server 1.5.13+ gives documents missing on disk a loader without Roslyn's one-second retries.
function Format-MissingSourceFilesLines($Record) {
    if ($null -eq $Record) {
        return @('Missing source files at load: unavailable (server before 1.5.13 or no workspace_missing_source_files record).')
    }
    $lines = @("Missing source files at load: $($Record.documents) documents in $($Record.files) files; fast-fail loaders installed: $($Record.installed); scan $(Format-Ms $Record.elapsedMs) ms.")
    $paths = @($Record.paths)
    if ($paths.Count -gt 0) { $lines += 'Missing files (first 10): ' + (($paths | ForEach-Object { '`' + $_ + '`' }) -join ', ') }
    return $lines
}

function Format-SearchExceptionLines($Summaries) {
    $lines = New-Object Collections.Generic.List[string]
    if (@($Summaries).Count -eq 0) {
        $lines.Add('Search exception summary unavailable: server before 1.5.11 or no workspace_search_exceptions record.')
        return $lines
    }
    foreach ($x in @($Summaries)) {
        $lines.Add("Search $($x.searchId) first-chance exceptions: $($x.total) total, $($x.attributed) in searched projects, $($x.unattributed) elsewhere in the process; $($x.distinctGroups) groups, $($x.droppedGroups) dropped.")
        $groups = @($x.groups)
        if ($groups.Count -eq 0) { continue }
        $lines.Add('')
        $lines.Add('| exception (top 20) | searched project | file or I/O message | count | first ms |')
        $lines.Add('|---|---|---|---|---|')
        foreach ($g in $groups) {
            $project = '(none)'
            if ($g.project) { $project = '`' + ([string]$g.project).Replace('|', '\|') + '`' }
            $detail = ''
            if ($g.detail) { $detail = ([string]$g.detail).Replace('|', '\|') -replace '\r?\n', ' ' }
            $lines.Add(('| `{0}` | {1} | {2} | {3} | {4} |' -f $g.type, $project, $detail, $g.count, (Format-Ms $g.firstMs)))
        }
    }
    return $lines
}

# dotnet-trace ignores Enter on a redirected console, and a console Ctrl+C cannot be sent to one child
# process reliably on Windows. The trace therefore ends when the server exits; the runtime still writes
# method rundown at shutdown, so managed frames keep their names.
function Start-FirstSearchTrace([int]$ProcessId, [string]$TracePath) {
    $logPath = "$TracePath.log"
    $arguments = "collect -p $ProcessId -o `"$TracePath`" --profile dotnet-common,dotnet-sampled-thread-time"
    $tracer = Start-Process -FilePath $TracerExe -ArgumentList $arguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Test-Path -LiteralPath $logPath) -and (Get-Content -LiteralPath $logPath -Raw) -match 'Output File') {
            # The session is attached; give the sampler a moment before the measured call.
            Start-Sleep -Milliseconds 1000
            return [pscustomobject]@{ Process = $tracer; TracePath = $TracePath; LogPath = $logPath }
        }
        if ($tracer.HasExited) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $tracer.HasExited) { Stop-Process -Id $tracer.Id -Force -ErrorAction SilentlyContinue }
    $output = ''
    foreach ($path in @($logPath, "$logPath.err")) {
        if (Test-Path -LiteralPath $path) { $output += (Get-Content -LiteralPath $path -Raw) }
    }
    throw "dotnet-trace did not attach to the server: $(Get-RedactedText $output)"
}

function Complete-FirstSearchTrace($Trace) {
    $state = 'completed'
    if (-not $Trace.Process.WaitForExit(300000)) {
        Stop-Process -Id $Trace.Process.Id -Force -ErrorAction SilentlyContinue
        $state = 'timeout'
    }
    $bytes = $null
    if (Test-Path -LiteralPath $Trace.TracePath) { $bytes = (Get-Item -LiteralPath $Trace.TracePath).Length }
    if ($state -eq 'completed' -and -not $bytes) { $state = 'no-file' }
    $output = ''
    if (Test-Path -LiteralPath $Trace.LogPath) { $output = Get-Content -LiteralPath $Trace.LogPath -Raw }
    return [pscustomobject]@{
        file = Split-Path $Trace.TracePath -Leaf
        bytes = $bytes
        state = $state
        tracerOutputTail = (Get-RedactedText ([string]$output).Trim()) -replace '(?s)^.*(.{500})$', '$1'
    }
}

$loadArguments = [ordered]@{ workspacePath = $Workspace; briefOutput = (-not $FullOutput) }
if ($Configuration) { $loadArguments.configuration = $Configuration }
if ($Platform) { $loadArguments.platform = $Platform }
if ($TargetFramework) { $loadArguments.targetFramework = $TargetFramework }

$affinityMask = 0L
if ($AffinityCount -gt 0) {
    $logical = [Environment]::ProcessorCount
    if ($AffinityCount -gt $logical) { throw "AffinityCount $AffinityCount exceeds $logical logical CPUs" }
    if ($AffinityCount -gt 63) { throw 'AffinityCount above 63 is not supported by this script' }
    $affinityMask = ([long]1 -shl $AffinityCount) - 1
}

$redactionPairs = Get-RedactionPairs $workspaceRoot (Split-Path $PSScriptRoot -Parent)
$indexCacheBefore = Get-IndexCacheState $Workspace
$records = New-Object Collections.Generic.List[object]
$totalRuns = $Runs
if ($TracerExe) { $totalRuns = $Runs + 1 }

for ($run = 1; $run -le $totalRuns; $run++) {
    $marker = 'measure-' + [Guid]::NewGuid().ToString('N')
    $offsets = Get-LogOffsets
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $ServerExe
    $psi.WorkingDirectory = $workspaceRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
    $psi.CreateNoWindow = $true
    if ($psi.EnvironmentVariables.ContainsKey('MCP_LOG_INCOMING_RPC')) { $psi.EnvironmentVariables.Remove('MCP_LOG_INCOMING_RPC') }

    Write-Host "Run $run/$totalRuns : starting server"
    $startWatch = [Diagnostics.Stopwatch]::StartNew()
    $script:proc = [Diagnostics.Process]::Start($psi)
    $script:pendingRead = $null
    $script:nextId = 1
    if ($affinityMask -ne 0) { $script:proc.ProcessorAffinity = [IntPtr]$affinityMask }
    $stderrTask = $script:proc.StandardError.ReadToEndAsync()

    $record = [ordered]@{
        run = $run; kind = 'measured'; startupMs = $null; load = $null; find = $null; ramHit = $null
        loadCpu = $null; findCpu = $null; leftoverChildren = 0; stderrTail = ''; trace = $null
    }
    if ($run -eq 1) { $record.kind = 'warmup' }
    elseif ($run -gt $Runs) { $record.kind = 'trace' }
    $trace = $null

    $calls = @()
    try {
        $init = Invoke-Request 'initialize' ([ordered]@{
            protocolVersion = '2025-11-25'
            capabilities = @{}
            clientInfo = [ordered]@{ name = 'workspace-load-measure'; version = $marker }
        })
        $startWatch.Stop()
        $record.startupMs = $startWatch.Elapsed.TotalMilliseconds
        if ($init.State -ne 'ok') { throw "initialize failed: $($init.State) $($init.Error)" }
        Send-Message ([ordered]@{ jsonrpc = '2.0'; method = 'notifications/initialized' })

        Write-Host "Run $run/$totalRuns : load_workspace"
        $measuredLoad = Measure-ToolCall 'load_workspace' $loadArguments
        $load = $measuredLoad.Call
        $record.loadCpu = $measuredLoad.Cpu
        $calls += [pscustomobject]@{ Name = 'load'; Call = $load }
        Write-Host ("Run {0}/{1} : load_workspace {2} in {3} ms" -f $run, $totalRuns, $load.State, (Format-Ms $load.Ms))

        if ($SymbolName -and $load.State -eq 'ok') {
            if ($record.kind -eq 'trace') {
                Write-Host "Run $run/$totalRuns : attaching dotnet-trace"
                $trace = Start-FirstSearchTrace $script:proc.Id (Join-Path $OutDirectory "run-$run-find.nettrace")
            }
            $measuredFind = Measure-ToolCall 'find_symbol_definition' ([ordered]@{ symbolName = $SymbolName })
            $find = $measuredFind.Call
            $record.findCpu = $measuredFind.Cpu
            $calls += [pscustomobject]@{ Name = 'find'; Call = $find }
            Write-Host ("Run {0}/{1} : find_symbol_definition {2} in {3} ms" -f $run, $totalRuns, $find.State, (Format-Ms $find.Ms))
        }

        if ($run -eq $Runs -and -not $SkipRamHit -and $load.State -eq 'ok') {
            $ram = Invoke-Tool 'load_workspace' $loadArguments
            $calls += [pscustomobject]@{ Name = 'ramHit'; Call = $ram }
            Write-Host ("Run {0}/{1} : RAM hit load_workspace {2} in {3} ms" -f $run, $totalRuns, $ram.State, (Format-Ms $ram.Ms))
        }
    }
    catch {
        $record.error = Get-RedactedText $_.Exception.Message
        Write-Warning "Run $run failed: $($record.error)"
    }
    finally {
        $record.leftoverChildren = Stop-ServerTree $script:proc.Id
        if ($null -ne $trace) {
            Write-Host "Run $run/$totalRuns : waiting for dotnet-trace to finish the file"
            $record.trace = Complete-FirstSearchTrace $trace
        }
    }

    if ($stderrTask.AsyncWaitHandle.WaitOne(5000)) {
        $stderr = $stderrTask.Result
        if ($stderr.Length -gt 2000) { $stderr = $stderr.Substring($stderr.Length - 2000) }
        $record.stderrTail = Get-RedactedText $stderr
    }

    Start-Sleep -Milliseconds 500
    $entries = @(ConvertTo-LogEntries @(Read-NewLogLines $offsets) $marker)
    $record.logSession = $null
    if ($entries.Count -gt 0) { $record.logSession = $entries[0].Session }

    foreach ($item in $calls) {
        $call = $item.Call
        $info = [ordered]@{ clientMs = $call.Ms; state = $call.State; requestId = $call.Id }
        $stage = Get-StageEstimate $entries $call.Id
        foreach ($p in $stage.PSObject.Properties) { $info[$p.Name] = $p.Value }
        $record[$item.Name] = [pscustomobject]$info
        $responsePath = Join-Path $OutDirectory ("run-{0}-{1}-response.md" -f $run, $item.Name)
        [IO.File]::WriteAllText($responsePath, (Get-RedactedText $call.Text), $utf8NoBom)
    }

    $excerpt = New-Object Collections.Generic.List[string]
    $watcherCount = 0
    foreach ($entry in $entries) {
        if ($entry.Message.StartsWith('Disk watcher started')) { $watcherCount++; continue }
        if ($watcherCount -gt 0) {
            $excerpt.Add("... $watcherCount 'Disk watcher started' lines omitted ...")
            $watcherCount = 0
        }
        $excerpt.Add((Get-RedactedText $entry.Raw))
    }
    [IO.File]::WriteAllLines((Join-Path $OutDirectory "run-$run-log.txt"), $excerpt, $utf8NoBom)

    $records.Add([pscustomobject]$record)
    if ($run -lt $totalRuns -and $PauseSeconds -gt 0) { Start-Sleep -Seconds $PauseSeconds }
}

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$os = Get-CimInstance Win32_OperatingSystem
$antivirus = @()
try {
    $antivirus = @(Get-CimInstance -Namespace 'root/SecurityCenter2' -ClassName AntiVirusProduct -ErrorAction Stop | ForEach-Object displayName)
}
catch { }
$sdks = @()
try { $sdks = @(& dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split ' ')[0] }) } catch { }

$environment = [ordered]@{
    serverVersion = (Get-Item -LiteralPath $ServerExe).VersionInfo.FileVersion
    os = "$($os.Caption) $($os.Version)"
    cpu = "$($cpu.Name.Trim()) ($($cpu.NumberOfCores) cores, $($cpu.NumberOfLogicalProcessors) logical)"
    ramGb = [Math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    dotnetSdks = ($sdks -join ', ')
    antivirus = ($antivirus -join ', ')
    # Overrides the server's runtimeconfig; the search lines show the mode the runtime applied.
    dotnetGcServer = $(if ($null -eq $env:DOTNET_gcServer) { '(unset)' } else { $env:DOTNET_gcServer })
    indexCacheBefore = $indexCacheBefore
    indexCacheAfter = Get-IndexCacheState $Workspace
    affinityCount = $AffinityCount
    workspace = Get-RedactedText $Workspace
    configuration = $Configuration
    platform = $Platform
    targetFramework = $TargetFramework
    symbolName = $SymbolName
    runs = $Runs
    note = $Note
    startedBy = 'scripts/Measure-WorkspaceLoad.ps1'
}

$document = [ordered]@{ environment = $environment; runs = $records }
[IO.File]::WriteAllText((Join-Path $OutDirectory 'runs.json'), ($document | ConvertTo-Json -Depth 8), $utf8NoBom)

$md = New-Object Collections.Generic.List[string]
$md.Add('# Workspace load measurement')
$md.Add('')
foreach ($key in $environment.Keys) { $md.Add("- ${key}: $($environment[$key])") }
$md.Add('')
$md.Add('Stage columns are estimates from log timestamps. open = capture line - tools/call - ReplayMs')
$md.Add('(lock wait, BuildHost start, evaluation, design-time build, IPC, snapshot). post-open = first')
$md.Add('watcher line - capture line (execution gate, publication, diagnostics, input map).')
$md.Add('watchers = Loaded line - first watcher line. response = handler completed - Loaded line.')
$md.Add('')
$md.Add('| run | kind | call | state | client ms | handler ms | open | replay | post-open | watchers | response | projects | diag lines | MSBuild failures | WS MB |')
$md.Add('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
foreach ($r in $records) {
    foreach ($name in @('load', 'find', 'ramHit')) {
        $c = $r.$name
        if ($null -eq $c) { continue }
        $md.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} | {14} |' -f
            $r.run, $r.kind, $name, $c.state, (Format-Ms $c.clientMs), (Format-Ms $c.handlerMs), (Format-Ms $c.openMs),
            (Format-Ms $c.replayMs), (Format-Ms $c.postOpenMs), (Format-Ms $c.watchersMs), (Format-Ms $c.responseMs),
            $c.projects, $c.diagLines, $c.msbuildFailures, $c.workingSetMb))
    }
}
$measured = @($records | Where-Object { $_.kind -eq 'measured' -and $null -ne $_.load -and $_.load.state -eq 'ok' })
$md.Add('')
$md.Add("Median over measured fresh-process runs (n=$($measured.Count), warmup excluded):")
$md.Add('')
foreach ($field in @('clientMs', 'handlerMs', 'openMs', 'replayMs', 'postOpenMs', 'watchersMs', 'responseMs')) {
    $md.Add("- load $field : " + (Format-Ms (Get-Median @($measured | ForEach-Object { $_.load.$field }))))
}
$finds = @($measured | Where-Object { $null -ne $_.find })
if ($finds.Count -gt 0) {
    $md.Add('- first find_symbol_definition clientMs : ' + (Format-Ms (Get-Median @($finds | ForEach-Object { $_.find.clientMs }))))
    $gcSearches = @($finds | ForEach-Object { @($_.find.searchSummaries) | Select-Object -First 1 } | Where-Object { $_.PSObject.Properties['gcPauseMs'] })
    if ($gcSearches.Count -gt 0) {
        $md.Add('- first search GC pause ms : ' + (Format-Ms (Get-Median @($gcSearches | ForEach-Object { $_.gcPauseMs }))))
    }
}
$md.Add('- server startup to initialize response ms : ' + (Format-Ms (Get-Median @($measured | ForEach-Object { $_.startupMs }))))
$md.Add('')
$md.Add('Project breakdowns (server 1.5.8+; full rows are in runs.json):')
$md.Add('Design-time project/target/task durations are inclusive and overlap; do not add them as wall time.')
$md.Add('Binlog coverage may be incomplete. Root evaluations, BuildHost startup and IPC can be outside the logged span.')
foreach ($r in $records) {
    $md.Add('')
    $md.Add("## Run $($r.run) ($($r.kind)) project timings")
    $md.Add('')
    if ($null -ne $r.load -and $null -ne $r.load.designTime) {
        $dt = $r.load.designTime
        $md.Add("Design-time coverage: $($dt.status); replay failures: $(@($dt.replayFailures) -join ', ').")
        $md.Add("Measured MSBuild open: $(Format-Ms $dt.openMs) ms; binlog span: $(Format-Ms $dt.binlogSpanMs) ms; unattributed remainder: $(Format-Ms $dt.unattributedOpenMs) ms; reason: $($dt.remainderReason).")
        $md.Add('Measured MSBuild open excludes manager/workspace setup, replay and snapshot processing; the earlier open column is a broader timestamp estimate.')
        $md.Add("Logged evaluations: $($dt.loggedEvaluations); inclusive sum: $(Format-Ms $dt.loggedEvaluationMs) ms; maximum: $(Format-Ms $dt.maxLoggedEvaluationMs) ms.")
        $md.Add("Unmatched starts/finishes: $($dt.unmatchedStarts)/$($dt.unmatchedFinishes); invalid intervals: $($dt.invalidIntervals).")
        if ($dt.PSObject.Properties['binlogs']) {
            foreach ($file in @($dt.binlogs)) {
                $md.Add("Binlog #$($file.ordinal): format $($file.formatVersion), project starts $($file.projectStarts), failures: $(@($file.failures) -join ', ').")
            }
        }
        $md.Add('')
        $md.Add('| design-time project (top 10, all configurations/TFMs combined) | builds | failed | inclusive sum ms | maximum ms |')
        $md.Add('|---|---|---|---|---|')
        foreach ($p in @($dt.projects | Sort-Object totalMs -Descending | Select-Object -First 10)) {
            $md.Add(('| `{0}` | {1} | {2} | {3} | {4} |' -f $p.project.Replace('|', '\|'), $p.builds, $p.failedBuilds, (Format-Ms $p.totalMs), (Format-Ms $p.maxMs)))
        }
        foreach ($kind in @('targets', 'tasks')) {
            $md.Add('')
            $md.Add("| $kind (top 10) | calls | inclusive sum ms | maximum ms |")
            $md.Add('|---|---|---|---|')
            foreach ($p in @($dt.$kind)) {
                $md.Add(('| `{0}` | {1} | {2} | {3} |' -f $p.name.Replace('|', '\|'), $p.calls, (Format-Ms $p.totalMs), (Format-Ms $p.maxMs)))
            }
        }
    }
    else { $md.Add('Design-time breakdown unavailable: no workspace_load_designtime record.') }
    if ($null -ne $r.load) {
        $md.Add('')
        Format-MissingSourceFilesLines $r.load.missingSourceFiles | ForEach-Object { $md.Add($_) }
    }
    $md.Add('')
    if ($null -ne $r.find -and $r.find.searchProjects.Count -gt 0) {
        foreach ($s in $r.find.searchSummaries) {
            $md.Add((Format-SearchSummaryLine $s))
        }
        $md.Add("Handler time outside measured search loops: $(Format-Ms $r.find.searchOutsideLoopMs) ms (preparation, result formatting and other work).")
        $md.Add('')
        $md.Add('| search project (top 10; each attempt separate) | documents | outcome | elapsed ms | declaration candidates |')
        $md.Add('|---|---|---|---|---|')
        foreach ($p in @($r.find.searchProjects | Sort-Object elapsedMs -Descending | Select-Object -First 10)) {
            $path = $p.project
            if (-not $path) { $path = $p.name }
            $md.Add(('| `{0}` | {1} | {2} | {3} | {4} |' -f $path.Replace('|', '\|'), $p.documents, $p.outcome, (Format-Ms $p.elapsedMs), $p.matches))
        }
    }
    else { $md.Add('Search project breakdown unavailable: query skipped or no workspace_search_project records.') }
    if ($null -ne $r.find) {
        $md.Add('')
        foreach ($line in (Format-SearchExceptionLines $r.find.searchExceptions)) { $md.Add($line) }
    }
}
$md.Add('')
$md.Add('CPU time during the call (Win32_Process kernel+user; child = BuildHost and other descendants;')
$md.Add('busy cores = total CPU / wall time):')
$md.Add('')
$md.Add('| run | call | server CPU ms | child CPU ms | busy cores | children |')
$md.Add('|---|---|---|---|---|---|')
foreach ($r in $records) {
    foreach ($pair in @(@('load', $r.loadCpu), @('find', $r.findCpu))) {
        $c = $pair[1]
        if ($null -eq $c) { continue }
        $md.Add(('| {0} | {1} | {2} | {3} | {4} | {5} |' -f $r.run, $pair[0], $c.serverCpuMs, $c.childCpuMs, $c.busyCores, $c.children))
    }
}
$traced = @($records | Where-Object { $_.kind -eq 'trace' })
if ($traced.Count -gt 0) {
    $md.Add('')
    $md.Add('Trace run (CPU samples and runtime exceptions of the first search; not part of the medians):')
    foreach ($r in $traced) {
        $findMs = 'n/a'
        if ($null -ne $r.find) { $findMs = Format-Ms $r.find.clientMs }
        if ($null -eq $r.trace) {
            $md.Add("- run $($r.run): no trace file; first search $findMs ms")
            continue
        }
        $md.Add("- run $($r.run): $($r.trace.state), $($r.trace.file), $($r.trace.bytes) bytes; first search with tracing $findMs ms")
    }
    $md.Add('- The .nettrace file contains exception messages with file paths; send it only where that is allowed.')
}
$failed = @($records | Where-Object { $_.PSObject.Properties['error'] -or ($null -ne $_.load -and $_.load.state -ne 'ok') })
$md.Add("- failed runs: $($failed.Count); leftover child processes killed: $(($records | Measure-Object -Property leftoverChildren -Sum).Sum)")
[IO.File]::WriteAllLines((Join-Path $OutDirectory 'summary.md'), $md, $utf8NoBom)

Write-Host ''
Write-Host "Report: $OutDirectory"
$leftovers = @(Find-UnredactedPaths $OutDirectory)
if ($leftovers.Count -gt 0) {
    Write-Warning "$($leftovers.Count) absolute paths were not redacted; review them before sending the report. Examples:"
    $leftovers | Select-Object -Unique -First 10 | ForEach-Object { Write-Warning "  $_" }
}
else { Write-Host 'Redaction check: no absolute paths left in the text files (Program Files and Windows excepted).' }
$md | Select-Object -Skip 1 | ForEach-Object { Write-Host $_ }
