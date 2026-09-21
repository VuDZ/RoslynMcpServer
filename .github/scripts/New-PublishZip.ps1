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
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

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

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    Compress-Archive -Path $payloadDir -DestinationPath $ZipPath -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $names = @(
            $zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
        )
        $unexpectedRoot = @($names | Where-Object { $_ -and -not $_.StartsWith('RoslynMCP/') })
        if ($unexpectedRoot.Count -gt 0) {
            throw ('Artifact zip must have root folder RoslynMCP. Unexpected entries: {0}' -f (($unexpectedRoot | Select-Object -First 5) -join ', '))
        }

        $logs = @($names | Where-Object { $_ -match '^RoslynMCP/logs(/|$)' })
        if ($logs.Count -gt 0) {
            throw ('Artifact zip must not include publish logs/: {0}' -f (($logs | Select-Object -First 5) -join ', '))
        }

        foreach ($required in @('RoslynMCP/README.md', 'RoslynMCP/LICENSE', 'RoslynMCP/RoslynMcpServer.exe')) {
            if ($names -notcontains $required) {
                throw "Artifact zip is missing $required"
            }
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

Write-Host "Packed $ZipPath"
