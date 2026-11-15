<#
.SYNOPSIS
  Verify an unpacked release over MCP stdio: initialize, tools/list, workspace load, definition, build.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HostPath,
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $HostPath)) {
    throw "Release host was not found: $HostPath"
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Smoke project was not found: $ProjectPath"
}

$script:stderrLines = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = (Resolve-Path -LiteralPath $HostPath).Path
$start.WorkingDirectory = Split-Path -Parent $start.FileName
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.CreateNoWindow = $true
$utf8 = [System.Text.UTF8Encoding]::new($false)
$start.StandardOutputEncoding = $utf8
$start.StandardErrorEncoding = $utf8
$start.StandardInputEncoding = $utf8

foreach ($name in @($start.Environment.Keys)) {
    if ($name.StartsWith('ROSLYN_MCP_', [System.StringComparison]::OrdinalIgnoreCase)) {
        [void]$start.Environment.Remove($name)
    }
}

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $start
$started = $false
$stderrPump = $null
$stderrHandle = $null

function Get-CapturedStderr {
    $text = ($script:stderrLines.ToArray() -join [Environment]::NewLine)
    if ($text.Length -le 4000) {
        return $text
    }

    return $text.Substring($text.Length - 4000)
}

if (-not $process.Start()) {
    throw "Could not start $HostPath"
}

$started = $true
# ErrorDataReceived is raised on a thread-pool thread. A script block there has no
# runspace and aborts pwsh as soon as the host writes a log line. Read the pipe
# from a runspace instead.
$stderrPump = [powershell]::Create()
$null = $stderrPump.AddScript({
    param($stream, $lines)
    while ($null -ne ($line = $stream.ReadLine())) {
        $lines.Enqueue($line)
    }
}).AddArgument($process.StandardError).AddArgument($script:stderrLines)
$stderrHandle = $stderrPump.BeginInvoke()

function Read-RpcResult {
    param(
        [int]$Id,
        [string]$What,
        [int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = $deadline - [DateTime]::UtcNow
        $pending = $process.StandardOutput.ReadLineAsync()
        if (-not $pending.Wait($remaining)) {
            throw "Timed out waiting for $What. stderr: $(Get-CapturedStderr)"
        }

        $line = $pending.Result
        if ($null -eq $line) {
            throw "stdout closed during $What. stderr: $(Get-CapturedStderr)"
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $message = $line | ConvertFrom-Json
        }
        catch {
            throw "Non-JSON stdout during $What`: $line"
        }

        if ($null -eq $message.id) {
            continue
        }

        if ([int]$message.id -ne $Id) {
            continue
        }

        if ($null -ne $message.error) {
            throw "Host refused $What`: $($message.error | ConvertTo-Json -Compress). stderr: $(Get-CapturedStderr)"
        }

        return $message.result
    }

    throw "Timed out waiting for $What. stderr: $(Get-CapturedStderr)"
}

function Send-Line {
    param([string]$Json)
    $process.StandardInput.WriteLine($Json)
    $process.StandardInput.Flush()
}

function Invoke-SmokeTool {
    param(
        [int]$Id,
        [string]$Name,
        [hashtable]$Arguments,
        [int]$TimeoutSeconds = 180
    )

    $call = @{
        jsonrpc = '2.0'
        id = $Id
        method = 'tools/call'
        params = @{ name = $Name; arguments = $Arguments }
    } | ConvertTo-Json -Compress -Depth 6
    Send-Line $call
    $result = Read-RpcResult -Id $Id -What $Name -TimeoutSeconds $TimeoutSeconds
    $text = (@($result.content | ForEach-Object { $_.text }) -join "`n")
    if ($result.isError -eq $true -or [string]::IsNullOrWhiteSpace($text) -or $text.StartsWith('Error:')) {
        throw "$Name failed: $text. stderr: $(Get-CapturedStderr)"
    }

    return $text
}

try {
    Send-Line '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"release-smoke","version":"1"}}}'
    $initialized = Read-RpcResult -Id 1 -What 'initialize' -TimeoutSeconds 60
    if ([string]$initialized.protocolVersion -ne '2025-11-25') {
        throw "initialize returned protocol $($initialized.protocolVersion)"
    }

    Send-Line '{"jsonrpc":"2.0","method":"notifications/initialized"}'
    Send-Line '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
    $listed = Read-RpcResult -Id 2 -What 'tools/list' -TimeoutSeconds 60
    $names = @($listed.tools | ForEach-Object { [string]$_.name })
    foreach ($name in @('load_workspace', 'find_symbol_definition', 'run_dotnet_build')) {
        if ($names -notcontains $name) {
            throw "tools/list did not include ${name}: $($names -join ', ')"
        }
    }

    $project = (Resolve-Path -LiteralPath $ProjectPath).Path
    $loaded = Invoke-SmokeTool -Id 3 -Name 'load_workspace' -Arguments @{
        workspacePath = $project
        configuration = 'Release'
    }
    if ($loaded.Contains('Workspace Load Failed')) {
        throw "load_workspace failed: $loaded. stderr: $(Get-CapturedStderr)"
    }

    $definition = Invoke-SmokeTool -Id 4 -Name 'find_symbol_definition' -Arguments @{ symbolName = 'Smoke' }
    $source = Join-Path (Split-Path -Parent $project) 'Smoke.cs'
    if ($definition -notmatch '(?m)^\s*Full name: Smoke\r?$' -or -not $definition.Contains($source)) {
        throw "find_symbol_definition did not resolve Smoke in ${source}: $definition"
    }

    $build = Invoke-SmokeTool -Id 5 -Name 'run_dotnet_build' -TimeoutSeconds 360 -Arguments @{
        workspacePath = $project
        configuration = 'Release'
    }
    if ($build -notmatch '(?m)^## Build succeeded\r?$') {
        throw "run_dotnet_build did not succeed: $build"
    }

    Write-Host "Smoke passed for $HostPath"
}
finally {
    if ($started -and -not $process.HasExited) {
        try {
            $process.Kill($true)
        }
        catch {
        }
    }

    if ($null -ne $stderrHandle) {
        try {
            if (-not $stderrHandle.AsyncWaitHandle.WaitOne(5000)) {
                $stderrPump.Stop()
            }

            $null = $stderrPump.EndInvoke($stderrHandle)
        }
        catch {
        }
    }

    if ($null -ne $stderrPump) {
        $stderrPump.Dispose()
    }

    $process.Dispose()
}
