# M-1–M-4 и F-4. Независимая проверка на Linux `02b5531`

Дата: 2026-11-26. Проверяющий: Claude Opus 5.5 (`claude-opus-5-5`) в отдельной сессии Claude Code. Объект — коммиты `c87d413`..`02b5531` ветки `docs/fix-series-root-causes`: M-3, F-4, M-2, M-4 и M-1 по [плану](../mcp-followups-plan.md).

Итог: M-2, M-3 и M-4 соответствуют плану. M-1 соответствует плану, есть мелкие замечания. **F-4 не выполняет критерий приёмки** в репозиториях с обычным тестовым проектом и в репозиториях больше 24 проектов (R-1, R-2). Предлагается открыть F-4 повторно.

## Что проверено

- Diff production-кода всех пяти коммитов: `McpServerInfoHelper`, `WorkspaceInputPath` и перевод проверок вида входа, `SolutionFilterPath`, `TestOutputPreflight`, маршрут `noBuild` в `TestTools`, `UnloadedSourceReaders`.
- Сервер опубликован из `02b5531` через `publish-and-verify.sh` и переподключён. `get_mcp_server_info` сообщил `1.7.0+02b55314ad9ea4d5990149e55021da732c5d7b93`, путь и время `RoslynMcpServer.dll`, MVID. Все вызовы ниже сделаны через публичные инструменты этого сервера. Сбоев MCP не было, обход через shell не понадобился.
- Linux x64, SDK 11.0.100. Фикстура `~/tmp/mcpfu` под Git: `Lib`, `Cp` с `CodePage=1251`, `Other` с ошибкой компиляции, тестовый проект xunit `Tests` с `Microsoft.NET.Test.Sdk` 17.14.1, `App.sln` и `AppX.slnx`, фильтры `App.slnf` и `AppX.slnf` без `Other`, повреждённый `Broken.slnf`, `NoBacking.slnf` со ссылкой на отсутствующее решение. Объявления кодировки нет.

## Release-проверка на Linux

Через MCP на `02b5531`: `run_dotnet_build` `RoslynMcpServer.sln -c Release` прошёл. `SourceStructure` **128 из 128**. Основная серия `Category!=AnalyzerLifecycle&Category!=RequiresWindows` **2 863 из 2 863**. Разница с Windows (2 875) — тесты `RequiresWindows`.

## Полевые проверки

- **M-2.** Путь процесса указывает на apphost снимка, путь сборки — на `RoslynMcpServer.dll`. Информационная версия содержит коммит. Подсказка в конце отчёта нейтральная.
- **M-4.** `run_dotnet_build` на `App.slnf` и `AppX.slnf` прошёл, `Other` не собран: каталога `Other/bin` нет. После `load_workspace` `App.sln` с `platform = Any CPU` прошли `run_dotnet_build` `App.slnf`, `run_dotnet_test` `AppX.slnf` и `run_specific_test` `App.slnf`. `load_workspace` на `.slnf` отклонён, загруженное `App.sln` осталось. `projectName` с фильтром отклонён с подсказкой. `Broken.slnf` дал `MSB5025`.
- **M-1.** Собран только Release, решение не загружено. `run_test_by_filter`, `run_specific_test` и `run_dotnet_test` с `noBuild=true` назвали `Tests/bin/Debug/net11.0/Tests.dll`, `Debug`, `AnyCPU`, `net11.0` и не запустили тесты. То же для `App.sln`. С `configuration = Release` все три прошли, 2 из 2. Фильтр без совпадений получил отдельный отчёт. Без `noBuild` и конфигурации `run_test_by_filter` собрал Debug и прошёл.
- **F-4.** Результаты `apply_patch` с фигурными скобками без загруженного решения:

| Фикстура | Результат |
|---|---|
| `Lib` под Git, без решения | применён |
| `Lib` и `App.sln` с шестью конфигурациями | применён |
| `Lib` и тестовый проект xunit с `ProjectReference` на `Lib` | отказ |
| то же без `ProjectReference` | отказ |
| то же без `Microsoft.NET.Test.Sdk` | применён |
| `mcpfu` после `load_workspace` `App.sln` | применён |
| Этот репозиторий на `cfddc76`, без `.editorconfig`, `VstestTrxTestFiles.cs` | отказ |

В фикстуре без тестового проекта: `Cp/C.cs` с `CodePage=1251` и ASCII-фрагментом применён без BOM. `Lib/WithBom.cs` сохранил BOM. `Loose/L.cs` вне проекта и `Scripts/run.cs` с `#!` получили отказ с подсказкой, SHA-256 совпали до и после.

## Замечания

### R-1

**Обычный тестовый проект в репозитории отключает F-4 для всех файлов.** Открыто.

Достаточно одного проекта с `Microsoft.NET.Test.Sdk` в границе поиска. После этого каждый патч C# без BOM и объявления кодировки получает отказ «Cannot determine how the owning project reads the BOM-free source», даже если этот проект не читает файл и не ссылается на его проект. После `load_workspace` тот же патч проходит. Это нарушает критерий плана: после загрузки владельца результат тот же, что и без загрузки.

Причина: [`HasUnresolvedCompilerInputs`](../../../Services/Workspace/UnloadedSourceReaders.cs) считает неизвестным любой target вне toolset с `Compile` в `ItemGroup`. Пакет Test SDK добавляет такой target `GenerateProgramFile` (`build/net8.0/Microsoft.NET.Test.Sdk.targets`). Проверку `AddReader` вызывает для каждого вычисленного проекта до поиска файла среди `Compile`, поэтому неизвестный проект делает неизвестным весь результат. M-1 уже распознаёт эту цель в [`IsStandardProgramGeneration`](../../../Services/Build/TestOutputPreflight.cs), F-4 нет. В тестах `WorkspaceWriteEncodingTests` нет проекта с Test SDK, поэтому ни регрессии, ни полевая приёмка на Windows этого не обнаружили.

### R-2

**Лимиты поиска меньше целевых репозиториев.** Открыто.

Поиск охватывает весь checkout Git и прерывается после 24 проектов или 256 каталогов. В этом репозитории 26 проектов и около 990 каталогов. Исходный сценарий F-4 на `cfddc76` до объявления кодировки по-прежнему даёт отказ. В этом репозитории его закрывает только M-3. Производственные решения владельца содержат десятки проектов, и там отказ будет всегда. В [отчёте о приёмке](mcp-followups-validation.md#f-4) отказ на копии с 26 проектами записан как ожидаемый. Нужно решение: поднять лимиты с замером или сузить поиск, чтобы не обходить весь checkout.

### R-3

**M-1: строка «Solution configuration» содержит только конфигурацию.** Мелкое.

Для `App.sln` отказ пишет `Solution configuration: Debug`, а не `Debug|Any CPU`. `TestOutputPreflight.GetProjects` передаёт `effectiveConfiguration` вместо собранной пары `solutionConfiguration`.

### R-4

**M-1: стоимость проверки перед запуском на крупных решениях не измерена.** Риск.

Проверка вычисляет в процессе сервера каждый управляемый проект решения, а не только тестовые: `IsTestProject` известен только после вычисления. Замер +0,6 с выполнен на `RoslynMcpServer.sln`, в нём 5 проектов. На решении с десятками проектов задержка вырастет пропорционально.

### R-5

**M-4: отсутствующее решение за фильтром даёт стек MSBuild.** Мелкое.

`NoBacking.slnf` вернул `MSB4014` с `FileNotFoundException` и двумя копиями стека, около 14 строк каждая. Суть видна во второй строке. Сервер мог бы заранее проверить путь через `SolutionFilterPath.TryReadBackingSolution` и вернуть одну строку.

### R-6

**Версия не изменилась.** Мелкое.

Production-код менялся, а `<Version>` остался `1.7.0`. Сборки различает только коммит в информационной версии.

### R-7

**`run_specific_test` запускает методы с общим префиксом.** Существовало до серии.

`className = CalcTests`, `methodName = Adds` запустил `Adds` и `AddsZero`. Фильтр строится как `FullyQualifiedName~…` и при FQN, найденном через Roslyn, тоже совпадает по подстроке.
