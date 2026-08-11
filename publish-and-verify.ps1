param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "RoslynMcpServer.csproj"),
    [string]$PublishDir = (Join-Path $PSScriptRoot "bin\Release\net10.0\win-x64\publish"),
    [int]$ExpectedMinTools = 54,
    # Cursor may keep MCP stdio processes alive after "disconnect"; they lock publish DLLs.
    [switch]$SkipKill
)

$ErrorActionPreference = "Stop"

# ReadyToRun for win-x64 requires a 64-bit SDK host. PATH often prefers
# "Program Files (x86)\dotnet" on Windows → CrossGen fails with
# Unable to load DLL 'clrjit_win_x64_x86'. Prefer Program Files\dotnet.
function Resolve-DotNetX64 {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        'C:\Program Files\dotnet\dotnet.exe'
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    $fromPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    throw "64-bit dotnet.exe not found under Program Files\dotnet. Install the .NET 10 x64 SDK."
}

function Test-PathUnderRoot {
    param(
        [string]$Path,
        [string]$Root
    )
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($Root)) {
        return $false
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    return $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) `
        -or $fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)
}

function Get-RoslynMcpLockHolders {
    param(
        [string]$PublishDir,
        [string]$RepoRoot
    )

    $holders = @()
    $cim = Get-CimInstance Win32_Process -Filter "Name = 'RoslynMcpServer.exe'" -ErrorAction SilentlyContinue
    foreach ($proc in @($cim)) {
        if ($null -eq $proc) { continue }

        $exePath = $proc.ExecutablePath
        # Unknown path (ACL / race): treat as a lock risk for this dogfood publish.
        $matchesRepo = [string]::IsNullOrWhiteSpace($exePath) `
            -or (Test-PathUnderRoot -Path $exePath -Root $PublishDir) `
            -or (Test-PathUnderRoot -Path $exePath -Root $RepoRoot)

        if (-not $matchesRepo) {
            continue
        }

        $holders += [pscustomobject]@{
            ProcessId      = [int]$proc.ProcessId
            ExecutablePath = if ($exePath) { $exePath } else { '(path unavailable)' }
            CommandLine    = $proc.CommandLine
        }
    }

    return $holders
}

function Stop-RoslynMcpLockHolders {
    param(
        [string]$PublishDir,
        [string]$RepoRoot,
        [int]$WaitSeconds = 8
    )

    $holders = @(Get-RoslynMcpLockHolders -PublishDir $PublishDir -RepoRoot $RepoRoot)
    if ($holders.Count -eq 0) {
        Write-Host "MCP lock check: no RoslynMcpServer.exe holding repo/publish paths."
        return
    }

    Write-Host "MCP lock check: stopping $($holders.Count) RoslynMcpServer.exe process(es) that may lock publish output:"
    foreach ($h in $holders) {
        Write-Host ("  PID {0}  {1}" -f $h.ProcessId, $h.ExecutablePath)
    }

    foreach ($h in $holders) {
        try {
            Stop-Process -Id $h.ProcessId -Force -ErrorAction Stop
        }
        catch {
            Write-Warning ("Failed to stop PID {0}: {1}" -f $h.ProcessId, $_.Exception.Message)
        }
    }

    $deadline = [datetime]::UtcNow.AddSeconds($WaitSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $remaining = @(Get-RoslynMcpLockHolders -PublishDir $PublishDir -RepoRoot $RepoRoot)
        if ($remaining.Count -eq 0) {
            Write-Host "MCP lock check: processes exited; publish folder should be unlocked."
            return
        }
    } while ([datetime]::UtcNow -lt $deadline)

    $still = @(Get-RoslynMcpLockHolders -PublishDir $PublishDir -RepoRoot $RepoRoot)
    if ($still.Count -gt 0) {
        $ids = ($still | ForEach-Object { $_.ProcessId }) -join ', '
        throw "RoslynMcpServer.exe still running after kill (PIDs: $ids). Close Cursor MCP hosts or pass -SkipKill only if you know the lock is elsewhere."
    }
}

$dotnet = Resolve-DotNetX64
Write-Host "dotnet host  : $dotnet"
& $dotnet --info | Select-String -Pattern '^\s*(RID|Architecture|Base Path)\s*:' | ForEach-Object { Write-Host $_.Line.Trim() }

if (-not $SkipKill) {
    Stop-RoslynMcpLockHolders -PublishDir $PublishDir -RepoRoot $PSScriptRoot
}
else {
    Write-Host "MCP lock check: skipped (-SkipKill)."
}

Write-Host "Publishing RoslynMcpServer..."
& $dotnet publish $ProjectPath -c Release -r win-x64 | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $PublishDir "RoslynMcpServer.exe"
if (-not (Test-Path $exe)) {
    throw "Publish output not found: $exe"
}

$exeInfo = Get-Item $exe
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
Write-Host "Binary       : $exe"
Write-Host "FileVersion  : $version"
Write-Host "LastWriteTime: $($exeInfo.LastWriteTime.ToString('o'))"
Write-Host "Size         : $($exeInfo.Length) bytes"

# Tool count via reflection at runtime is not exposed via CLI; verify assembly loads.
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Cursor -> MCP -> Reload RoslynMcpServer (old PIDs were stopped before publish)"
Write-Host "  2. Call get_mcp_server_info (expect >= $ExpectedMinTools tools, version $version)"
Write-Host "  3. Logs: $PublishDir\logs\mcp-*.log"

exit 0
