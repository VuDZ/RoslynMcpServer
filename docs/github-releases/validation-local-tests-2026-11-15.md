# Завершение локальной валидации, 2026-11-15

Статус: **local-validation-complete**. Шесть сбоев из
[первого отчёта](validation-macos-arm64-2026-11-15.md) устранены и реально выполнены.
Согласованный объём: [task-01](task-01-macos-arm64-med.md), продолжение по поручению
пользователя «разберись с шестью падающими тестами». GitHub CI отложен пользователем;
нативная поддержка macOS этим локальным прогоном не подтверждается.

## Причины и исправления

1. Три `DependencyPartialProofTests` и ordinary-session тест `IndependentWorkspaceOracleTests`
   используют строго SDK 10.0.300, включая проверку конкретных framework packs.
   На машине его не было. Подготовлен изолированный SDK с исходным pin и исходными assertions:
   проверки не переведены на другой SDK, фильтр и категории не менялись.
2. `RawImportEvidenceCapture` читал binlog формата 28 новым SDK через reader до 26.
   Включено чтение совместимых новых форматов, как в production provenance reader.
   `RecoverableReadError` прерывает capture: неполные события не принимаются за доказательство.
   В исходном окружении с установленным SDK 11 все `RawImportEvidenceTests` прошли.
3. Multitarget-фикстура жила вне `global.json` репозитория и выбирала SDK 11,
   хотя тестовый процесс загрузил MSBuild из SDK 10. `RoundtripFixture` теперь записывает
   свой `global.json` с точной версией SDK загруженного MSBuild. Повтор в исходном окружении
   с доступным SDK 11 прошёл: **4/4**, без трёхминутного зависания и ошибки удаления каталога.
4. Полный прогон в изолированном .NET выявил дополнительное неверное предположение
   `DotNetHostResolverTests`: тест всегда требовал `Program Files`, игнорируя приоритет
   существующего `DOTNET_ROOT`. Assertion теперь проверяет явно заданный host, а при его
   отсутствии — прежний fallback. Production resolver не менялся. Тест прошёл и в обычном,
   и в изолированном окружении.
5. Диагностические `.cs` из `artifacts/` попадали в compile glob сервера и давали CS0579.
   Каталог исключён из `Compile`/`None`; production scope в SourceStructure синхронизирован
   с этим исключением. Диагностическая папка остаётся на месте, файлы больше не перемещаются.

## Финальные проверки

Все проверки — **Release**, против окончательного кода 1.5.18.

- Solution build: **passed**. Сохранились прежние CS8603, CS8601 и xUnit1031 вне изменённых строк.
- Шесть исходных случаев с соседними theory-вариантами: **9/9 passed**.
- Основной CI-набор: **1492 total, 1492 passed, 0 failed, 0 skipped**.
  Фильтр ровно `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`.
  Все шесть исходных случаев проверены по именам в финальном `main.trx`; они не исключены.
- SourceStructure: **123/123 passed**, инвентарь структурных ограничений не менялся.
- `AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`: **13/13 passed**, 0 skipped.
- `Assert-NoSkippedTests.ps1`: **1637/1637** для четырёх TRX (main, lifecycle, source structure,
  focused). Это сумма прогонов, не количество уникальных тестов.
- Diff check с учётом существующего CRLF: `git -c core.whitespace=cr-at-eol diff --check` — passed.

## Окружение и повторение

В `Program Files` запись недоступна; системные SDK не изменены. Изолированный .NET сохранён в
`artifacts/test-validation-20261115/sdk-setup/extracted/`:

- CLI и загруженный MSBuild — SDK 10.0.402; доступен точный SDK 10.0.300.
- SDK 10.0.300 скачан из официального
  [архива Microsoft](https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.300/dotnet-sdk-10.0.300-win-x64.zip),
  SHA512 сверена с [release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json):
  `32446eddffc5a485f58f9d79cdab3a1a9adab4adc2ef0e4c787cfbb2465020d50beaadc54d40f0850e2e0089edd09864d12d6c19c526319819d57a4c00d38518`.
- SDK 10.0.402 и reference pack 9.0.16 скопированы обычными файлами; остальные установленные
  runtime/pack версии переиспользованы через junction. Это локальный каталог валидации,
  его устройство не является требованием к поставке сервера.
- Прогоны выполнены вне sandbox, поскольку внутри недоступен IPC VSTest/BuildHost.
- Временный корневой `global.json` закреплял 10.0.402 только на время проверок и удалён.

Для повторения из корня репозитория используйте подготовленный .NET и исходные фильтры:

```powershell
$validationDotnetRoot = (Resolve-Path 'artifacts/test-validation-20261115/sdk-setup/extracted').Path
$env:DOTNET_ROOT = $validationDotnetRoot
$env:PATH = $validationDotnetRoot + ';' + $env:PATH
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
foreach ($name in @('MSBUILD_EXE_PATH', 'MSBuildSDKsPath',
    'DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR', 'DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR',
    'DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER')) {
    Remove-Item -LiteralPath "Env:\$name" -ErrorAction SilentlyContinue
}
& "$validationDotnetRoot/dotnet.exe" test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter 'Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk'
$env:ROSLYN_MCP_ANALYZER_LIFECYCLE = '1'
& "$validationDotnetRoot/dotnet.exe" test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter 'Category=AnalyzerLifecycle'
```

Исходники и тесты перед прогоном собраны `run_dotnet_build` для решения, Release.
TRX сохранены в `TestResults/test-validation-20261115/`: `main.trx`, `lifecycle.trx`,
`source-structure.trx`, `focused-six.trx`.

GitHub workflow, нативный macOS/Linux-прогон, commit, push и публичный релиз не выполнялись.
Предыдущие проверки Windows-архивов и кросс-публикаций macOS записаны в первом отчёте;
новые публикации в этой части работы не выполнялись.
