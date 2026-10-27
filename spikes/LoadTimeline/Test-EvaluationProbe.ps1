# Regression check: projects outside the solution folder import props through SolutionDir, including each TFM evaluation.
[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'ParallelSearch/MatrixCommon.ps1')

$project = Join-Path $PSScriptRoot 'LoadTimeline.csproj'
$dll = Join-Path $PSScriptRoot 'bin/Release/net10.0/LoadTimeline.dll'
Invoke-Native 'dotnet' @('build', $project, '-c', 'Release', '-nologo', '-v', 'q') 'Spike build failed'

$fixture = Join-Path $PSScriptRoot ('results/evaluation-check-' + [Guid]::NewGuid().ToString('N'))
$solutionDirectory = Join-Path $fixture 'solution home'
$projectDirectory = Join-Path $fixture 'project'
New-Item -ItemType Directory -Force -Path $solutionDirectory, $projectDirectory | Out-Null
@'
<Project>
  <Import Project="$(SolutionDir)common.cs.netcore.props" />
  <Import Project="$(SolutionDir)$(TargetFramework).props" Condition="'$(TargetFramework)' != ''" />
</Project>
'@ | Set-Content -LiteralPath (Join-Path $projectDirectory 'Probe.csproj') -Encoding UTF8
'<Project><PropertyGroup><TargetFrameworks>first;second</TargetFrameworks></PropertyGroup></Project>' |
    Set-Content -LiteralPath (Join-Path $solutionDirectory 'common.cs.netcore.props') -Encoding UTF8
foreach ($framework in @('first', 'second')) {
    '<Project />' | Set-Content -LiteralPath (Join-Path $solutionDirectory "$framework.props") -Encoding UTF8
}
@'
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Probe", "../project/Probe.csproj", "{1096D02E-8BF4-4E91-B3DC-FA922AFB31B2}"
EndProject
Global
EndGlobal
'@ | Set-Content -LiteralPath (Join-Path $solutionDirectory 'Probe.sln') -Encoding UTF8
'<Solution><Project Path="../project/Probe.csproj" /></Solution>' |
    Set-Content -LiteralPath (Join-Path $solutionDirectory 'Probe.slnx') -Encoding UTF8

# Run from another directory and pass a relative solution path; its own folder must still supply the imports.
Push-Location $fixture
try {
    foreach ($extension in @('sln', 'slnx')) {
        $output = Join-Path $fixture $extension
        Invoke-Native 'dotnet' @($dll, 'evaluate', "solution home/Probe.$extension", $output) 'Evaluation failed'
        $rows = @(Import-Csv -LiteralPath (Join-Path $output 'evaluation.csv'))
        if ($rows.Count -ne 1 -or $rows[0].project -ne 'Probe.csproj' -or $rows[0].evaluations -ne '3') {
            throw "Expected one outer and two TFM evaluations for Probe.$extension."
        }
    }
}
finally {
    Pop-Location
}
Write-Host 'Evaluation checks passed: SolutionDir imports for .sln/.slnx, outer and TFM evaluations, relative paths and spaces.'
