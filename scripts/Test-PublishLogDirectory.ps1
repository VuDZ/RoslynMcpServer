# Verify the publish script's real path resolver without publishing or stopping MCP processes.
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$scriptPath = Join-Path $PSScriptRoot '../publish-and-verify.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
$resolver = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-RoslynMcpLogDirectory'
}, $false)
if ($null -eq $resolver) { throw 'Publish log resolver not found.' }
. ([scriptblock]::Create($resolver.Extent.Text))

$saved = @{}
foreach ($name in @('ROSLYN_MCP_LOG_DIR', 'ROSLYN_MCP_HOME', 'LOCALAPPDATA')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$root = Join-Path ([IO.Path]::GetTempPath()) 'roslyn-log-resolution'
$script:logChecks = 0
function Assert-LogPath([string]$Expected) {
    $script:logChecks++
    $actual = Get-RoslynMcpLogDirectory
    if ($actual -ne [IO.Path]::GetFullPath($Expected)) { throw "Expected '$Expected', received '$actual'." }
}
try {
    $env:LOCALAPPDATA = Join-Path $root 'local'
    $env:ROSLYN_MCP_HOME = $null
    $env:ROSLYN_MCP_LOG_DIR = $null
    Assert-LogPath (Join-Path $env:LOCALAPPDATA 'RoslynMcpServer/logs')
    $env:ROSLYN_MCP_HOME = Join-Path $root 'home'
    Assert-LogPath (Join-Path $env:ROSLYN_MCP_HOME 'logs')
    $env:ROSLYN_MCP_LOG_DIR = Join-Path $root 'custom-logs'
    Assert-LogPath $env:ROSLYN_MCP_LOG_DIR
    $env:ROSLYN_MCP_LOG_DIR = 'relative-logs'
    Assert-LogPath (Join-Path $env:ROSLYN_MCP_HOME 'logs')
    $env:ROSLYN_MCP_HOME = 'relative-home'
    Assert-LogPath (Join-Path $env:LOCALAPPDATA 'RoslynMcpServer/logs')
    $env:ROSLYN_MCP_LOG_DIR = "bad$([char]0)path"
    Assert-LogPath (Join-Path $env:LOCALAPPDATA 'RoslynMcpServer/logs')
    if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        $env:ROSLYN_MCP_LOG_DIR = 'C:relative-logs'
        Assert-LogPath (Join-Path $env:LOCALAPPDATA 'RoslynMcpServer/logs')
        $env:ROSLYN_MCP_LOG_DIR = '\rooted-without-drive'
        Assert-LogPath (Join-Path $env:LOCALAPPDATA 'RoslynMcpServer/logs')
        $env:ROSLYN_MCP_LOG_DIR = '\\example\share\logs'
        Assert-LogPath $env:ROSLYN_MCP_LOG_DIR
    }
    Write-Output ("Publish log directory: {0} checks passed." -f $script:logChecks)
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
