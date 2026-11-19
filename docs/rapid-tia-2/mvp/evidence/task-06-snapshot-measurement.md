# Task-06 — память двух снимков и число тестовых классов

2026-11-19, Windows x64, .NET SDK 10.0.402, Release, сервер **1.8.1** на `9bb3c8c`,
`DOTNET_PROCESSOR_COUNT=4`. Сборку продукта исполнитель использовал как зависимость
[временной программы](SnapshotMeasurement.cs.txt).

- `inventory` загружает workspace через `SolutionManager` и вызывает тот же
  `VstestTestInventory.BuildAsync`, что и оценка. В каждой строке TSV — имя проекта, число классов в
  инвентаре, число классов без известного имени VSTest, `OutputFilePath`. Число классов включает записи
  без имени, как и размеры ступеней. Тест-кейсы и строки параметров отдельными классами не считаются.
- `memory` копирует выходы загруженных проектов, прогревает процесс и для каждой пробы создаёт чистую
  `AssemblySnapshotSession`. После первого чтения меняет timestamp каждой копии и вызывает тот же
  `GetPairAsync`. Программа проверяет, что запись каждой сборки действительно перечитана, затем отпускает
  пару с графами. Измеряет разницу `GC.GetTotalMemory(forceFullCollection: true)` относительно кучи без
  сессии. Это удерживаемая управляемая память, без workspace, графов запроса и пика чтения. У всех DLL
  отдельные временные каталоги, поэтому ширина ступени 3 берётся из ответа MCP с настоящими выходными
  каталогами по M-06.

## Команды

Выполняются из корня репозитория после Release-сборки сервера. Создайте временный проект под
`artifacts/task-06/memory` (он исключён из glob сервера). Имя временной сборки даёт ей существующий доступ
к внутренним классам сервера.

```powershell
$measurementRoot = Join-Path (Get-Location) 'artifacts/task-06/memory'
$serverRelease = Join-Path (Get-Location) 'bin/Release/net10.0'
New-Item -ItemType Directory -Path $measurementRoot -Force | Out-Null
Copy-Item 'docs/rapid-tia-2/mvp/evidence/SnapshotMeasurement.cs.txt' (Join-Path $measurementRoot 'SnapshotMeasurement.cs')
$measurementProject = Join-Path $measurementRoot 'SnapshotMeasurement.csproj'
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <AssemblyName>RoslynMcpServer.Tests</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="RoslynMcpServer" HintPath="$(ServerRelease)/RoslynMcpServer.dll" />
    <Reference Include="Microsoft.CodeAnalysis" HintPath="$(ServerRelease)/Microsoft.CodeAnalysis.dll" />
    <Reference Include="Microsoft.CodeAnalysis.Workspaces" HintPath="$(ServerRelease)/Microsoft.CodeAnalysis.Workspaces.dll" />
    <Reference Include="Microsoft.Extensions.Logging.Abstractions" HintPath="$(ServerRelease)/Microsoft.Extensions.Logging.Abstractions.dll" />
    <Reference Include="Microsoft.Extensions.Options" HintPath="$(ServerRelease)/Microsoft.Extensions.Options.dll" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath $measurementProject
dotnet build $measurementProject -c Release "-p:ServerRelease=$serverRelease" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Snapshot measurement build failed.' }
$measurementOut = Join-Path $measurementRoot 'bin/Release/net10.0'
Copy-Item -Path (Join-Path $serverRelease '*') -Destination $measurementOut -Recurse -Force
$snapshotHost = Join-Path $measurementOut 'RoslynMcpServer.Tests.dll'
$snapshotDeps = Join-Path $measurementOut 'RoslynMcpServer.deps.json'
```

Укажите абсолютный путь к уже собранному solution, его конфигурацию и платформу. Для workspace с
явно выбранной TFM добавьте её последним аргументом `inventory`. Загрузке на Windows нужен доступ к
именованному каналу MSBuild BuildHost.

```powershell
$solution = (Resolve-Path 'RoslynMcpServer.sln').Path
$env:ROSLYN_MCP_HOME = Join-Path $measurementRoot 'home'
$env:DOTNET_PROCESSOR_COUNT = '4'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:NuGetAudit = 'false'
$inventory = Join-Path $measurementRoot 'inventory.log'
dotnet exec --depsfile $snapshotDeps $snapshotHost inventory $solution Release 'Any CPU' |
    Set-Content -LiteralPath $inventory
if ($LASTEXITCODE -ne 0) { throw 'Test class inventory failed.' }
$assemblies = @(Get-Content -LiteralPath $inventory |
    Where-Object { $_ -match "`t" } |
    ForEach-Object { ($_ -split "`t", 4)[3] })
dotnet exec --depsfile $snapshotDeps $snapshotHost memory @assemblies
if ($LASTEXITCODE -ne 0) { throw 'Snapshot measurement failed.' }
```

Храните TSV и stderr закрытого solution локально. В отчёте оставляйте только числа и причины.

## Результаты

Самозамер — 5 сборок, все прочитаны, 833 отпечатка типов. В единственном тестовом проекте 203 класса,
у всех есть имя VSTest. После прогрева получены следующие числа.

```text
sample=1 assemblies=5 unreadable=0 types=833 first_read_ms=817 one_snapshot_bytes=2012208 two_snapshots_bytes=4039528
sample=2 assemblies=5 unreadable=0 types=833 first_read_ms=684 one_snapshot_bytes=2014160 two_snapshots_bytes=4025336
sample=3 assemblies=5 unreadable=0 types=833 first_read_ms=678 one_snapshot_bytes=2024496 two_snapshots_bytes=4035432
```

Медианы — **684 мс**, **1,92 МиБ** для одного снимка, **3,85 МиБ** для двух. Время первого чтения в
живом MCP берётся из его собственного лога.

Замер владельца — 41 выход, из них 39 прочитаны, 2497 отпечатков типов. В 17 тестовых проектах 300
классов, у всех есть имя VSTest. Две присутствующие сборки получили `Unreadable` в обеих версиях снимка
по одной причине: `NotAnAssembly: BadImageFormatException: The complete custom attribute value could not be decoded.`
Обе записи учтены в памяти сессии.

```text
sample=1 assemblies=41 unreadable=2 types=2497 first_read_ms=1391 one_snapshot_bytes=5279552 two_snapshots_bytes=10649184
sample=2 assemblies=41 unreadable=2 types=2497 first_read_ms=1117 one_snapshot_bytes=5111880 two_snapshots_bytes=10300280
sample=3 assemblies=41 unreadable=2 types=2497 first_read_ms=1189 one_snapshot_bytes=5111448 two_snapshots_bytes=10265408
```

Медианы — **1189 мс**, **4,88 МиБ** для одного снимка, **9,82 МиБ** для двух. Результаты выбора тестов
через MCP и ограничения приёмки — в [основном отчёте](../task-06-implementation-report.md).
