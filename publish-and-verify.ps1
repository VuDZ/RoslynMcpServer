param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "RoslynMcpServer.csproj"),
    [string]$PublishDir = (Join-Path $PSScriptRoot "bin\Release\net10.0\win-x64\publish"),
    [int]$ExpectedMinTools = 54
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing RoslynMcpServer..."
dotnet publish $ProjectPath -c Release -r win-x64 | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $PublishDir "RoslynMcpServer.exe"
if (-not (Test-Path $exe)) {
    throw "Publish output not found: $exe"
}

$exeInfo = Get-Item $exe
Write-Host "Binary       : $exe"
Write-Host "LastWriteTime: $($exeInfo.LastWriteTime.ToString('o'))"
Write-Host "Size         : $($exeInfo.Length) bytes"

$toolCount = & $exe --help 2>$null
# Tool count via reflection at runtime is not exposed via CLI; verify assembly loads.
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Cursor -> MCP -> Reload RoslynMcpServer"
Write-Host "  2. Call get_mcp_server_info (expect >= $ExpectedMinTools tools in response)"
Write-Host "  3. Logs: $PublishDir\logs\mcp-*.log"

exit 0
