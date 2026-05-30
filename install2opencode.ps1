#Requires -Version 5.1
<#
.SYNOPSIS
  Registers RoslynMcpServer in the current project's OpenCode configuration.

.DESCRIPTION
  Creates or updates opencode.json in the project directory (current location by default)
  and merges Roslyn MCP agent rules into AGENTS.md when they are not already present.

.PARAMETER BinaryPath
  Absolute path to the published RoslynMcpServer executable.
  When omitted, the script looks for RoslynMcpServer.exe or RoslynMcpServer next to itself.

.PARAMETER ProjectPath
  Target project root. Defaults to the current working directory.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string] $BinaryPath,

    [Parameter()]
    [string] $ProjectPath = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$McpServerName = 'RoslynMcpServer'
$AgentsMarker = 'Roslyn MCP Server'
$SampleFileName = 'AGENTS.md.sample'
$AgentsFileName = 'AGENTS.md'
$OpenCodeConfigFileName = 'opencode.json'

function Resolve-RoslynBinaryPath {
    param([string] $ExplicitPath)

    if ($ExplicitPath) {
        $resolved = Resolve-Path -LiteralPath $ExplicitPath
        return $resolved.ProviderPath
    }

    $candidates = @(
        (Join-Path $PSScriptRoot 'RoslynMcpServer.exe'),
        (Join-Path $PSScriptRoot 'RoslynMcpServer')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }

    throw "RoslynMcpServer binary was not found. Pass -BinaryPath or run this script from the publish folder."
}

function Convert-ToForwardSlashPath {
    param([string] $Path)
    return ($Path -replace '\\', '/')
}

function Get-AgentsSampleContent {
    $samplePath = Join-Path $PSScriptRoot $SampleFileName
    if (-not (Test-Path -LiteralPath $samplePath)) {
        throw "Missing $SampleFileName next to install2opencode.ps1. Rebuild/publish RoslynMcpServer first."
    }

    $content = Get-Content -LiteralPath $samplePath -Raw -Encoding UTF8
    return ($content -replace '(?s)\A<!--.*?-->\s*', '').TrimStart()
}

function Test-RoslynAgentsInstructionsPresent {
    param([string] $Content)

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return $false
    }

    return ($Content -match [regex]::Escape($AgentsMarker)) -and
           ($Content -match 'load_workspace') -and
           ($Content -match 'run_dotnet_build')
}

function Update-AgentsFile {
    param(
        [string] $TargetPath,
        [string] $SampleContent
    )

    $existingContent = $null
    if (Test-Path -LiteralPath $TargetPath) {
        $existingContent = Get-Content -LiteralPath $TargetPath -Raw -Encoding UTF8
    }

    if (Test-RoslynAgentsInstructionsPresent -Content $existingContent) {
        Write-Host "AGENTS.md already contains Roslyn MCP instructions. Skipping."
        return
    }

    if ([string]::IsNullOrWhiteSpace($existingContent)) {
        Set-Content -LiteralPath $TargetPath -Value $SampleContent -Encoding UTF8 -NoNewline
        Write-Host "Created $AgentsFileName with Roslyn MCP instructions."
        return
    }

    $separator = "`r`n`r`n---`r`n`r`n<!-- Added by RoslynMcpServer install2opencode.ps1 -->`r`n`r`n"
    $merged = $existingContent.TrimEnd() + $separator + $SampleContent
    Set-Content -LiteralPath $TargetPath -Value $merged -Encoding UTF8 -NoNewline
    Write-Host "Appended Roslyn MCP instructions to existing $AgentsFileName."
}

function Read-OpenCodeConfig {
    param([string] $ConfigPath)

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        return [ordered]@{
            '$schema' = 'https://opencode.ai/config.json'
            mcp       = [ordered]@{}
        }
    }

    $raw = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [ordered]@{
            '$schema' = 'https://opencode.ai/config.json'
            mcp       = [ordered]@{}
        }
    }

    $parsed = $raw | ConvertFrom-Json
    $config = [ordered]@{}

    foreach ($property in $parsed.PSObject.Properties) {
        $config[$property.Name] = $property.Value
    }

    if (-not $config.Contains('$schema') -or [string]::IsNullOrWhiteSpace([string]$config['$schema'])) {
        $config['$schema'] = 'https://opencode.ai/config.json'
    }

    if (-not $config.Contains('mcp') -or $null -eq $config.mcp) {
        $config.mcp = [ordered]@{}
    }
    elseif ($config.mcp -isnot [System.Collections.IDictionary]) {
        $mcp = [ordered]@{}
        foreach ($property in $config.mcp.PSObject.Properties) {
            $mcp[$property.Name] = $property.Value
        }
        $config.mcp = $mcp
    }

    return $config
}

function Write-OpenCodeConfig {
    param(
        [string] $ConfigPath,
        [hashtable] $Config
    )

    $json = $Config | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $ConfigPath -Value $json -Encoding UTF8
}

$projectRoot = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath
$binary = Resolve-RoslynBinaryPath -ExplicitPath $BinaryPath
$binaryForConfig = Convert-ToForwardSlashPath -Path $binary

Write-Host "Project root : $projectRoot"
Write-Host "MCP binary   : $binary"

$openCodeConfigPath = Join-Path $projectRoot $OpenCodeConfigFileName
$config = Read-OpenCodeConfig -ConfigPath $openCodeConfigPath

$config.mcp[$McpServerName] = [ordered]@{
    type    = 'local'
    command = @($binaryForConfig)
    enabled = $true
}

Write-OpenCodeConfig -ConfigPath $openCodeConfigPath -Config $config
Write-Host "Updated $OpenCodeConfigFileName."

$agentsPath = Join-Path $projectRoot $AgentsFileName
$sampleContent = Get-AgentsSampleContent
Update-AgentsFile -TargetPath $agentsPath -SampleContent $sampleContent

Write-Host 'Done. Restart OpenCode or reload MCP servers to apply changes.'
