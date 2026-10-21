<#
.SYNOPSIS
  Drive one unpacked release host over MCP stdio: initialize, tools/list, load_workspace.
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

$script:stderr = New-Object System.Text.StringBuilder
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = (Resolve-Path -LiteralPath $HostPath).Path
$start.WorkingDirectory = Split-Path -Parent $start.FileName
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.CreateNoWindow = $true
$start.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
$start.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false)

foreach ($name in @($start.Environment.Keys)) {
    if ($name.StartsWith('ROSLYN_MCP_', [System.StringComparison]::OrdinalIgnoreCase)) {
        [void]$start.Environment.Remove($name)
    }
}

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $start
$process.add_ErrorDataReceived({
    param($sender, $event)
    if (-not [string]::IsNullOrEmpty($event.Data)) {
        [void]$script:stderr.AppendLine($event.Data)
    }
})

if (-not $process.Start()) {
    throw "Could not start $HostPath"
}

$process.BeginErrorReadLine()

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
            throw "Timed out waiting for $What. stderr: $($script:stderr.ToString())"
        }

        $line = $pending.Result
        if ($null -eq $line) {
            throw "stdout closed during $What. stderr: $($script:stderr.ToString())"
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
            throw "Host refused $What`: $($message.error | ConvertTo-Json -Compress). stderr: $($script:stderr.ToString())"
        }

        return $message.result
    }

    throw "Timed out waiting for $What. stderr: $($script:stderr.ToString())"
}

function Send-Line {
    param([string]$Json)
    $process.StandardInput.WriteLine($Json)
    $process.StandardInput.Flush()
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
    if ($names -notcontains 'load_workspace') {
        throw "tools/list did not include load_workspace: $($names -join ', ')"
    }

    $project = (Resolve-Path -LiteralPath $ProjectPath).Path
    $call = @{
        jsonrpc = '2.0'
        id       = 3
        method   = 'tools/call'
        params   = @{
            name      = 'load_workspace'
            arguments = @{ workspacePath = $project }
        }
    } | ConvertTo-Json -Compress -Depth 6
    Send-Line $call
    $loaded = Read-RpcResult -Id 3 -What 'load_workspace' -TimeoutSeconds 180
    if ($loaded.isError -eq $true) {
        throw "load_workspace reported isError. stderr: $($script:stderr.ToString())"
    }

    $text = (@($loaded.content | ForEach-Object { $_.text }) -join "`n")
    if ([string]::IsNullOrWhiteSpace($text) -or $text.StartsWith('Error:') -or $text.Contains('Workspace Load Failed')) {
        throw "load_workspace failed: $text. stderr: $($script:stderr.ToString())"
    }

    Write-Host "Smoke passed for $HostPath"
}
finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
    }

    $process.Dispose()
}
