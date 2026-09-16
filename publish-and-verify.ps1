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

function Get-ArtifactVersion {
    param([string]$ExePath)

    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath)
    foreach ($raw in @($info.ProductVersion, $info.FileVersion)) {
        if ([string]::IsNullOrWhiteSpace($raw)) { continue }
        $candidate = $raw.Split('+', 2)[0].Trim()
        if ($candidate -match '^(\d+\.\d+\.\d+)') {
            return $Matches[1]
        }
    }

    throw "Cannot determine three-part version from $ExePath (ProductVersion='$($info.ProductVersion)', FileVersion='$($info.FileVersion)')."
}

function New-PublishArtifactZip {
    param(
        [string]$PublishDir,
        [string]$RepoRoot,
        [string]$Version
    )

    $readmePath = Join-Path $RepoRoot 'README.md'
    if (-not (Test-Path -LiteralPath $readmePath)) {
        throw "README.md not found: $readmePath"
    }

    $artifactsDir = Join-Path $RepoRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

    $zipPath = Join-Path $artifactsDir ("RoslynMCP-{0}.zip" -f $Version)
    $stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("RoslynMcpPublish_" + [guid]::NewGuid().ToString('N'))
    $payloadDir = Join-Path $stagingRoot 'RoslynMCP'
    New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

    try {
        Get-ChildItem -LiteralPath $PublishDir -Force | Where-Object { $_.Name -ne 'logs' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payloadDir $_.Name) -Recurse -Force
        }

        Copy-Item -LiteralPath $readmePath -Destination (Join-Path $payloadDir 'README.md') -Force

        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        Compress-Archive -Path $payloadDir -DestinationPath $zipPath -CompressionLevel Optimal

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $names = @(
                $zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
            )
            $unexpectedRoot = @($names | Where-Object { $_ -and -not $_.StartsWith('RoslynMCP/') })
            if ($unexpectedRoot.Count -gt 0) {
                throw ("Artifact zip must have root folder RoslynMCP. Unexpected entries: {0}" -f (($unexpectedRoot | Select-Object -First 5) -join ', '))
            }

            $logs = @($names | Where-Object { $_ -match '^RoslynMCP/logs(/|$)' })
            if ($logs.Count -gt 0) {
                throw ("Artifact zip must not include publish logs/: {0}" -f (($logs | Select-Object -First 5) -join ', '))
            }

            if ($names -notcontains 'RoslynMCP/README.md') {
                throw "Artifact zip is missing RoslynMCP/README.md"
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }

    return $zipPath
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
$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
$artifactVersion = Get-ArtifactVersion -ExePath $exe
Write-Host "Binary       : $exe"
Write-Host "FileVersion  : $fileVersion"
Write-Host "LastWriteTime: $($exeInfo.LastWriteTime.ToString('o'))"
Write-Host "Size         : $($exeInfo.Length) bytes"

Write-Host "Packaging artifact zip (root RoslynMCP, no logs/, README.md included)..."
$zipPath = New-PublishArtifactZip -PublishDir $PublishDir -RepoRoot $PSScriptRoot -Version $artifactVersion
$zipInfo = Get-Item $zipPath
Write-Host "Artifact     : $zipPath"
Write-Host "Zip size     : $($zipInfo.Length) bytes"

# Tool count via reflection at runtime is not exposed via CLI; verify assembly loads.
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Cursor -> MCP -> Reload RoslynMcpServer (old PIDs were stopped before publish)"
Write-Host "  2. Call get_mcp_server_info (expect >= $ExpectedMinTools tools, version $fileVersion)"
Write-Host "  3. Logs: $PublishDir\logs\mcp-*.log"
Write-Host "  4. Artifact zip: $zipPath"

exit 0
