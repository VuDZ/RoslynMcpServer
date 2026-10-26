<#
.SYNOPSIS
    Checks the full ProjectFileInfo RPC contract with a condition changed by design-time build, linked documents,
    additional/editorconfig/content items, capabilities and include/exclude/remove patterns. No office inputs.

.DESCRIPTION
    A second fixture checks SDK resolution for candidate C of BuildHost/task-04, which shares one SDK resolution
    cache between the evaluations of a project: an SDK from a local NuGet feed in versions 1.0.0 and 2.0.0 adds
    v1/**/*.cs or v2/**/*.cs to Compile. One project asks for 1.0.0 under one target framework and for 2.0.0 under
    the other; two more pin different versions in their own global.json. Every side must get the versions upstream
    gets.
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
# Each side: the spike copy whose BuildHost-netcore it runs, and for a fork side A + B and whether C is on.
$sides = [ordered]@{
    original = @{ bin = $copies.originalBin; fork = $false; sharedSdkCache = $false }
    'original-repeat' = @{ bin = $copies.originalBin; fork = $false; sharedSdkCache = $false }
    ab = @{ bin = $copies.forkBin; fork = $true; sharedSdkCache = $false }
    gc = @{ bin = $gcBin; fork = $true; sharedSdkCache = $false }
    abc = @{ bin = $copies.forkBin; fork = $true; sharedSdkCache = $true }
}

function Invoke-Capture([string]$Side, [string]$Solution, [string]$Output) {
    $definition = $sides[$Side]
    Set-ForkOptimizations $true 4 'all' $definition.sharedSdkCache
    $dll = Join-Path $definition.bin 'BuildHostFork.dll'
    $hostDll = Join-Path $definition.bin 'BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll'
    $exitCode = Invoke-Spike $dll @('rpc-capture', $hostDll, $Solution, $Output) ([IO.Path]::ChangeExtension($Output, '.out.txt')) $definition.fork
    Assert-Rpc ($exitCode -eq 0) "$Side capture completed and host exited"
    return Get-Content -LiteralPath $Output -Raw | ConvertFrom-Json
}

function Assert-SameAsOriginal([string]$Side, [string]$OriginalOutput, [string]$Output) {
    $dll = Join-Path $sides[$Side].bin 'BuildHostFork.dll'
    & dotnet $dll rpc-compare $OriginalOutput $Output > ([IO.Path]::ChangeExtension($Output, '.raw.txt'))
    Assert-Rpc ($LASTEXITCODE -in @(0, 1)) "$Side raw comparison completed"
    Invoke-Native 'dotnet' @($dll, 'rpc-compare', $OriginalOutput, $Output, 'glob-sets') "$Side RPC fields changed beyond glob set order"
}

foreach ($side in $sides.Keys) {
    Remove-Item -LiteralPath (Join-Path $directory 'marker.txt') -Force -ErrorAction SilentlyContinue
    $output = Join-Path $directory "$side.json"
    $capture = Invoke-Capture $side $solution $output
    $info = $capture.Projects[0].Infos[0]
    Assert-Rpc (-not $info.isEmpty -and @($info.documents | Where-Object { $_.filePath -like '*After.cs' }).Count -eq 0) "$side build snapshot excludes the late document"
    Assert-Rpc (@($info.fileGlobs.includes | Where-Object { $_ -like '*after*' }).Count -gt 0) "$side late globs observe the build-created marker"
    $linked = @($info.documents | Where-Object { $_.logicalPath -eq 'Logical/Linked.cs' })
    Assert-Rpc ($linked.Count -eq 1 -and $linked[0].folders[0] -eq 'Logical') "$side preserves logical path and folders"
    Assert-Rpc ($info.additionalDocuments.Count -eq 1 -and $info.analyzerConfigDocuments.Count -gt 0 -and $info.contentFilePaths.Count -eq 1 -and $info.projectCapabilities -contains 'RpcFixture') "$side captures additional, editorconfig, content and capability fields"
    if ($side -eq 'original') { continue }
    Assert-SameAsOriginal $side (Join-Path $directory 'original.json') $output
}

# The SDK fixture: RoslynMcp.FixtureSdk 1.0.0 and 2.0.0 in a local feed, restored into a packages folder of its own.
$sdkDirectory = Join-Path $directory 'sdk'
$feed = Join-Path $sdkDirectory 'feed'
New-Item -ItemType Directory -Force -Path $feed | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($version in @('1.0.0', '2.0.0')) {
    $major = $version.Split('.')[0]
    $package = Join-Path $sdkDirectory "package-$version"
    New-Item -ItemType Directory -Force -Path (Join-Path $package 'Sdk') | Out-Null
    Write-Utf8 (Join-Path $package 'RoslynMcp.FixtureSdk.nuspec') @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>RoslynMcp.FixtureSdk</id><version>$version</version><authors>fixture</authors><description>fixture</description>
    <packageTypes><packageType name="MSBuildSdk" /></packageTypes>
  </metadata>
</package>
"@
    Write-Utf8 (Join-Path $package 'Sdk/Sdk.props') "<Project><ItemGroup><Compile Include=`"v$major/**/*.cs`" /></ItemGroup></Project>"
    [IO.Compression.ZipFile]::CreateFromDirectory($package, (Join-Path $feed "RoslynMcp.FixtureSdk.$version.nupkg"))
}
Write-Utf8 (Join-Path $sdkDirectory 'NuGet.Config') @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="fixture" value="feed" /></packageSources>
  <config><add key="globalPackagesFolder" value="packages" /></config>
</configuration>
'@

# Not SDK-style: only the fixture SDK is resolved, and the targets the host requires do nothing.
function Write-SdkProject([string]$Path, [string]$Body) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Path), (Join-Path (Split-Path $Path) 'v1'), (Join-Path (Split-Path $Path) 'v2') | Out-Null
    Write-Utf8 (Join-Path (Split-Path $Path) 'v1/V1.cs') 'class V1 { }'
    Write-Utf8 (Join-Path (Split-Path $Path) 'v2/V2.cs') 'class V2 { }'
    Write-Utf8 $Path "<Project>$Body<Target Name=`"CoreCompile`" /><Target Name=`"Compile`" DependsOnTargets=`"CoreCompile`" /></Project>"
}
Write-SdkProject (Join-Path $sdkDirectory 'Versions/Versions.csproj') @'
<PropertyGroup><TargetFrameworks>one;two</TargetFrameworks></PropertyGroup>
<Import Project="Sdk.props" Sdk="RoslynMcp.FixtureSdk" Version="1.0.0" Condition="'$(TargetFramework)' != 'two'" />
<Import Project="Sdk.props" Sdk="RoslynMcp.FixtureSdk" Version="2.0.0" Condition="'$(TargetFramework)' == 'two'" />
'@
foreach ($pin in @(@{ name = 'Left'; version = '1.0.0' }, @{ name = 'Right'; version = '2.0.0' })) {
    Write-SdkProject (Join-Path $sdkDirectory "$($pin.name)/$($pin.name).csproj") '<PropertyGroup><TargetFramework>one</TargetFramework></PropertyGroup><Import Project="Sdk.props" Sdk="RoslynMcp.FixtureSdk" />'
    Write-Utf8 (Join-Path $sdkDirectory "$($pin.name)/global.json") "{ `"msbuild-sdks`": { `"RoslynMcp.FixtureSdk`": `"$($pin.version)`" } }"
}
$sdkSolution = Join-Path $sdkDirectory 'Sdk.slnx'
Write-Utf8 $sdkSolution '<Solution><Project Path="Versions/Versions.csproj" /><Project Path="Left/Left.csproj" /><Project Path="Right/Right.csproj" /></Solution>'

function Get-FixtureVersions($Capture, [string]$ProjectName) {
    $project = @($Capture.Projects | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_.Path) -eq $ProjectName })
    Assert-Rpc ($project.Count -eq 1) "the SDK fixture capture has $ProjectName"
    return @($project[0].Infos | ForEach-Object {
        $versions = @($_.documents | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.filePath) } | Sort-Object -Unique) -join '+'
        "$($_.targetFramework)=$versions"
    })
}

foreach ($side in $sides.Keys) {
    $output = Join-Path $sdkDirectory "$side.json"
    $capture = Invoke-Capture $side $sdkSolution $output
    $versions = (Get-FixtureVersions $capture 'Versions') -join ', '
    Assert-Rpc ($versions -eq 'one=V1, two=V2') "$side resolves the SDK version each target framework asks for ($versions)"
    $left = Get-FixtureVersions $capture 'Left'
    $right = Get-FixtureVersions $capture 'Right'
    Assert-Rpc ($left -join ', ' -eq 'one=V1' -and $right -join ', ' -eq 'one=V2') "$side resolves the version each global.json pins ($left; $right)"
    if ($side -eq 'original') { continue }
    Assert-SameAsOriginal $side (Join-Path $sdkDirectory 'original.json') $output
}
Write-Host "ProjectFileInfo RPC checks passed: $directory"
