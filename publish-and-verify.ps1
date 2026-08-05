param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "RoslynMcpServer.csproj"),
    [string]$PublishDir = (Join-Path $PSScriptRoot "bin\Release\net10.0\win-x64\publish"),
    [int]$ExpectedMinTools = 54
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

$dotnet = Resolve-DotNetX64
Write-Host "dotnet host  : $dotnet"
& $dotnet --info | Select-String -Pattern '^\s*(RID|Architecture|Base Path)\s*:' | ForEach-Object { Write-Host $_.Line.Trim() }

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
Write-Host "  1. Cursor -> MCP -> Reload RoslynMcpServer"
Write-Host "  2. Call get_mcp_server_info (expect >= $ExpectedMinTools tools, version $version)"
Write-Host "  3. Logs: $PublishDir\logs\mcp-*.log"

exit 0
