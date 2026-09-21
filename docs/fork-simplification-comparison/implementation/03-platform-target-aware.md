# 3 — Platform: raw для `.sln`, canonical для `.csproj`

Pri **P1** для `.sln` (MSB4126). SemVer: **patch**. Независим от парсера.
Разбор: [../port-candidates-1-2-3-4-7.md](../port-candidates-1-2-3-4-7.md#3-platform-verbatim-на-cli).

## Цель

`.sln`/`.slnx` CLI получает точное `Any CPU`; `.csproj` — `AnyCPU`; workspace
load по-прежнему `AnyCPU`.

## Файлы

- `Services/DotNetConfigurationArguments.cs`
- `Services/SolutionManager.cs` — `LoadedPlatform` + `LoadedPlatformRaw`
- `Tools/BuildTools.cs`, `Tools/TestTools.cs`, `Services/DotNetTestArguments.cs`
- `RoslynMcpServer.Tests/DotNetConfigurationArgumentsTests.cs`

## Правка

CLI formatter знает target path: `.sln`/`.slnx` → `Normalize` (trim), `.csproj`
→ `NormalizePlatform`. Наследование при опущенном `platform` — по **фактическому**
target вызова, не всегда `LoadedPlatformRaw`.

Не удалять `FormatConfigurationProperty` (`-p:Configuration=`).

Безусловный verbatim форка не переносить: нет их CLI-теста, что `Any CPU` ломает
SDK csproj, но риск `bin/Any CPU` vs `bin/AnyCPU` достаточный для ветки.

## Тесты

- `NormalizePlatform("Any CPU") == "AnyCPU"` остаётся
- solution → `-p:Platform="Any CPU"`; csproj → `"AnyCPU"`
- inherit: load `Any CPU` → sln build raw, csproj build canonical

## Acceptance

`.sln` `Debug|Any CPU` без MSB4126; прямой csproj без смены output platform
folder; workspace global property `AnyCPU`.
