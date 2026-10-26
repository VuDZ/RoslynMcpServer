<#
.SYNOPSIS
    Checks the full ProjectFileInfo RPC contract with a condition changed by design-time build, linked documents,
    additional/editorconfig/content items, capabilities and include/exclude/remove patterns. No office inputs.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$spikes = Split-Path $PSScriptRoot -Parent
$repo = Split-Path $spikes -Parent
. (Join-Path $spikes 'ParallelSearch/MatrixCommon.ps1')
. (Join-Path $PSScriptRoot 'BuildHostForkCommon.ps1')
$copies = Initialize-ForkCopy $repo $PSScriptRoot
$gcBin = Initialize-GcForkCopy $copies.forkBin
$directory = Join-Path $PSScriptRoot ('results/rpc-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $directory 'before'), (Join-Path $directory 'after') | Out-Null

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

function Assert-Rpc([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "RPC check failed: $Message" }
    Write-Host "ok: $Message"
}

Write-Utf8 (Join-Path $directory 'Directory.Build.props') '<Project />'
Write-Utf8 (Join-Path $directory 'Directory.Build.targets') '<Project />'
Write-Utf8 (Join-Path $directory 'Probe.csproj') @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
  <ItemGroup>
    <Compile Include="before/**/*.cs" Exclude="before/excluded/**;before/also-excluded/**" />
    <Compile Include="after/**/*.cs" Condition="Exists('marker.txt')" />
    <Compile Remove="before/removed/**;before/also-removed/**" />
    <Compile Include="Linked.cs" Link="Logical/Linked.cs" />
    <AdditionalFiles Include="extra.txt" /><EditorConfigFiles Include="settings.editorconfig" />
    <Content Include="content.txt" /><ProjectCapability Include="RpcFixture" />
  </ItemGroup>
  <Target Name="ChangeGlobInput" AfterTargets="CoreCompile">
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/marker.txt" Lines="ready" Overwrite="true" />
  </Target>
</Project>
'@
Write-Utf8 (Join-Path $directory 'before/Before.cs') 'class Before { }'
Write-Utf8 (Join-Path $directory 'after/After.cs') 'class After { }'
Write-Utf8 (Join-Path $directory 'Linked.cs') 'class Linked { }'
foreach ($file in @('extra.txt', 'settings.editorconfig', 'content.txt')) { Write-Utf8 (Join-Path $directory $file) '' }
$solution = Join-Path $directory 'Probe.slnx'
Write-Utf8 $solution '<Solution><Project Path="Probe.csproj" /></Solution>'
Invoke-Native 'dotnet' @('restore', $solution, '-nologo', '-v', 'q') 'RPC fixture restore failed'
Set-ForkOptimizations $true 4 'all'
$bins = [ordered]@{ original = $copies.originalBin; 'original-repeat' = $copies.originalBin; ab = $copies.forkBin; gc = $gcBin }
foreach ($side in $bins.Keys) {
    Remove-Item -LiteralPath (Join-Path $directory 'marker.txt') -Force -ErrorAction SilentlyContinue
    $dll = Join-Path $bins[$side] 'BuildHostFork.dll'
    $hostDll = Join-Path $bins[$side] 'BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll'
    $output = Join-Path $directory "$side.json"
    $exitCode = Invoke-Spike $dll @('rpc-capture', $hostDll, $solution, $output) (Join-Path $directory "$side.out.txt") ($side -notlike 'original*')
    Assert-Rpc ($exitCode -eq 0) "$side capture completed and host exited"
    $capture = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $info = $capture.Projects[0].Infos[0]
    Assert-Rpc (-not $info.isEmpty -and @($info.documents | Where-Object { $_.filePath -like '*After.cs' }).Count -eq 0) "$side build snapshot excludes the late document"
    Assert-Rpc (@($info.fileGlobs.includes | Where-Object { $_ -like '*after*' }).Count -gt 0) "$side late globs observe the build-created marker"
    $linked = @($info.documents | Where-Object { $_.logicalPath -eq 'Logical/Linked.cs' })
    Assert-Rpc ($linked.Count -eq 1 -and $linked[0].folders[0] -eq 'Logical') "$side preserves logical path and folders"
    Assert-Rpc ($info.additionalDocuments.Count -eq 1 -and $info.analyzerConfigDocuments.Count -gt 0 -and $info.contentFilePaths.Count -eq 1 -and $info.projectCapabilities -contains 'RpcFixture') "$side captures additional, editorconfig, content and capability fields"
    if ($side -eq 'original') { continue }
    & dotnet $dll rpc-compare (Join-Path $directory 'original.json') $output > (Join-Path $directory "$side-raw.txt")
    Assert-Rpc ($LASTEXITCODE -in @(0, 1)) "$side raw comparison completed"
    Invoke-Native 'dotnet' @($dll, 'rpc-compare', (Join-Path $directory 'original.json'), $output, 'glob-sets') 'RPC fields changed beyond glob set order'
}
Write-Host "ProjectFileInfo RPC checks passed: $directory"
