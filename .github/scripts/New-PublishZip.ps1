<#
.SYNOPSIS
  Pack a publish folder as RoslynMCP/*.zip (README + LICENSE, no logs/).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [string]$RepoRoot,
    [ValidateSet('zip', 'tar.gz')]
    [string]$Format = 'zip',
    [string]$HostFileName = 'RoslynMcpServer.exe'
)

$ErrorActionPreference = 'Stop'

function Assert-ArchiveNames {
    param(
        [string[]]$Names,
        [string]$HostFileName
    )

    $unexpectedRoot = @($Names | Where-Object { $_ -and -not $_.StartsWith('RoslynMCP/') })
    if ($unexpectedRoot.Count -gt 0) {
        throw ('Artifact must have root folder RoslynMCP. Unexpected entries: {0}' -f (($unexpectedRoot | Select-Object -First 5) -join ', '))
    }

    $logs = @($Names | Where-Object { $_ -match '^RoslynMCP/logs(/|$)' })
    if ($logs.Count -gt 0) {
        throw ('Artifact must not include publish logs/: {0}' -f (($logs | Select-Object -First 5) -join ', '))
    }

    foreach ($required in @('RoslynMCP/README.md', 'RoslynMCP/LICENSE', ('RoslynMCP/' + $HostFileName))) {
        if ($Names -notcontains $required) {
            throw "Artifact is missing $required"
        }
    }
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$PublishDir = (Resolve-Path $PublishDir).Path
$readmePath = Join-Path $RepoRoot 'README.md'
$licensePath = Join-Path $RepoRoot 'LICENSE'
if (-not (Test-Path -LiteralPath $readmePath)) {
    throw "README.md not found: $readmePath"
}

if (-not (Test-Path -LiteralPath $licensePath)) {
    throw "LICENSE not found: $licensePath"
}

$zipParent = Split-Path -Parent $ZipPath
if ($zipParent -and -not (Test-Path -LiteralPath $zipParent)) {
    New-Item -ItemType Directory -Path $zipParent -Force | Out-Null
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('RoslynMcpRelease_' + [guid]::NewGuid().ToString('N'))
$payloadDir = Join-Path $stagingRoot 'RoslynMCP'
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

try {
    Get-ChildItem -LiteralPath $PublishDir -Force | Where-Object { $_.Name -ne 'logs' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payloadDir $_.Name) -Recurse -Force
    }

    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $payloadDir 'README.md') -Force
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $payloadDir 'LICENSE') -Force

    $hostInPayload = Join-Path $payloadDir $HostFileName
    if (-not (Test-Path -LiteralPath $hostInPayload)) {
        throw "Publish output is missing the host file $HostFileName"
    }

    if ($Format -eq 'tar.gz' -and -not $IsWindows) {
        & chmod a+x $hostInPayload
        if ($LASTEXITCODE -ne 0) {
            throw "chmod failed for $hostInPayload"
        }
    }

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    if ($Format -eq 'zip') {
        Compress-Archive -Path $payloadDir -DestinationPath $ZipPath -CompressionLevel Optimal
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
        try {
            $names = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
            Assert-ArchiveNames $names $HostFileName
        }
        finally {
            $zip.Dispose()
        }
    }
    else {
        & tar -czf $ZipPath -C $stagingRoot RoslynMCP
        if ($LASTEXITCODE -ne 0) {
            throw "tar failed for $ZipPath"
        }

        $extractRoot = Join-Path $stagingRoot 'extract'
        New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
        & tar -xzf $ZipPath -C $extractRoot
        if ($LASTEXITCODE -ne 0) {
            throw "tar extract failed for $ZipPath"
        }

        $names = @(Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Force | ForEach-Object {
            $_.FullName.Substring($extractRoot.Length).TrimStart('\', '/').Replace('\', '/')
        })
        Assert-ArchiveNames $names $HostFileName
        $extractedHost = Join-Path $extractRoot ('RoslynMCP/' + $HostFileName)
        if (-not $IsWindows) {
            & test -x $extractedHost
            if ($LASTEXITCODE -ne 0) {
                throw "Archive host is not executable after extract: $extractedHost"
            }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Write-Host "Packed $ZipPath"
