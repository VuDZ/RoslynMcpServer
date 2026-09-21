# Сравнение с форком VladD2/RoslynMcpServer (`simplification`)

Статус: **shipped 1.3.33–1.4.3; archived**. Runtime — корневой README
«Agent tools by version». Этот пакет — история сравнения, не живой контракт.
Канон сравнения — этот файл. **Ship-план (отдельные файлы):**
[implementation/README.md](implementation/README.md). Разбор по пунктам —
[port-candidates-1-2-3-4-7.md](port-candidates-1-2-3-4-7.md),
[port-candidate-5-nonzero-exit.md](port-candidate-5-nonzero-exit.md),
[navigation-surface-8.md](navigation-surface-8.md).

Не входило в ship: **7b** (missing C# project), **9** (`AnalyzerShadowLoader`),
навигация **S4** (enclosing member) и **S5** (`directOnly`).

## 0. Короткий вердикт

Форк не заменяет основной репозиторий и целиком переносить его не следует.
Однако в нём есть несколько исправлений, которых в локальном/upstream `main`
нет:

- переносить в первую очередь: CLI locale (1), analyzer sanitizer (4), VSTest
  aggregation (2), multi-root watcher для известных документов (6), базовую
  коррекцию test FQN (10);
- переносить только после адаптации: target-aware platform (3), non-zero exit
  с доказанным summary (5), только mixed non-C# часть diagnostics (7a),
  позиционную/name-based навигацию слоями (8), multi-root search scope как union
  существующего root и external projects (11);
- оставить исследованием: default-ALC `AnalyzerShadowLoader` (9); добавить как
  необязательную observability-фичу — lossless raw report с lifecycle policy (12);
- не переносить: config-based lazy/prewarm, удаление tool groups/editing/docs и
  упрощение analyzer provenance/lifecycle подсистемы.

Независимая перепроверка (2026-09-21): основная инвентаризация подтверждена,
но прямой перенос нескольких решений форка небезопасен. Уточнения и исправления
собраны в [§9](#9-коррекции-независимой-проверки): platform должен зависеть от
типа target, mixed C++ и отсутствующий C# project нельзя смягчать одной политикой,
а search/overflow/analyzer preload требуют переработки.

## 1. Предмет сравнения

| Роль | Ref | SHA |
|---|---|---|
| Проверяемый checkout (наш) | локальный `main` / `HEAD` | `46d796d258c9af5212caa4fb673cc896ab1ac53e` (v**1.3.32**) |
| Upstream main | `origin/main` | `bc3ec0946def13ee575a81873854ad6bb1001758` (v**1.3.32**) |
| Форк, ветка из задания | `VladD2/RoslynMcpServer@simplification` | `40020fc6c2d027d66218e1872c646e33d4fa2cd2` |
| Форк, фактический верх | `VladD2/RoslynMcpServer@main` | `58dc361248fb51c06f1ab5c131849bbb4a1a9883` (v**1.4.0**) |

Метод: refs форка обновлены по HTTPS и сверены через `git ls-remote`; SHA
`origin/main` также проверен через HTTPS (SSH fetch origin недоступен из этой
сессии, но cached ref и remote SHA совпали). Далее использованы `git diff
--stat`, `git ls-tree`, `git grep`, commit-by-commit разбор и чтение файлов обоих
деревьев. Локальный `main` на два документационных/CI-коммита впереди
`origin/main`; продуктовый код в этой дельте не менялся. Истории нашего дерева и
форка **несвязанные** — `git merge-base` не находит общего предка, поэтому merge
невозможен, перенос — только вручную по отдельным изменениям.

**Ключевое:** ветка `simplification` — предок `fork/main`, и три коммита поверх
неё содержат самые ценные исправления. Сравнивать имеет смысл с `fork/main`,
не только с `simplification`.

| Коммит | Что чинит |
|---|---|
| `dfde2b9` | platform passthrough (MSB4126), агрегация VSTest-summary, `DOTNET_CLI_UI_LANGUAGE=en-US`, `AnalyzerShadowLoader` |
| `fa5a264` | false «no matching tests», pass/fail по `failed==0` вместо exit code, temp-отчёты вместо `TruncatedProcessLog` |
| `58dc361` | `get_test_list`: 0 тестов **и** битый FQN в JSON; concise-отчёты тестов |

Полный разбор main-only дельты (15 файлов) — в [§5.2](#52-что-именно-принёс-forkmain-поверх-simplification).

## 2. Профиль форка

| Метрика | Наш локальный `main` | Форк `simplification` | Форк `main` |
|---|---|---|---|
| MCP-тулов | **63** | **41** | **41** (тот же набор) |
| Diff по коду (Tools/Services/Diagnostics/Hosting/Config) | — | 94 файла, +5 323 / −14 639 | 96 файлов, +5 899 / −14 890 |
| Полный diff дерева | — | 434 файла, +11 797 / −56 593 | 437 файлов, +12 765 / −56 806 |
| Версия csproj | 1.3.32 | **1.4.0** | **1.4.0** |
| `docs/` | спеки, review/arbitration, archive | удалён целиком | то же |
| Analyzer shadow / provenance | ~7k строк, `LifecycleTestHost`, эпохи + v2/v3 | удалено целиком | то же |
| Загрузка workspace | явный `load_workspace` | lazy из `RoslynMcp.jsonc` + `WorkspacePrewarmService` | то же |
| Тесты | xUnit v2, `TestAttributeMatcher` | xUnit **v3** | то же |

Итог: форк — это «ужать поверхность до ~40 тулов, выкинуть docs и
analyzer-машинерию, перейти на config-based lazy load». Часть идей ценна,
часть — деградация для наших сценариев (см. §6, §7). **Не всё закрыто в
основном репо.** Уверенные кандидаты: 1, 2, 4, 6 и базовая часть 10. Кандидаты
3, 5, 7, 8 и 11 ценны только после описанной ниже адаптации; их fork-код нельзя
считать готовым drop-in patch.

## 3. Отличия поверхности инструментов

Только у нас (24 шт.): `add_field_to_class`, `add_method_to_class`,
`add_property_to_class`, `add_type_to_class_bases`, `add_using`, `apply_patch`,
`enable_tool_group`, `execute_dotnet_command`, `find_usages`,
`generate_test_method_stub`, `get_changed_files`, `get_file_content`,
`get_tool_help`, `list_directory_tree`, `list_tool_groups`,
`manage_agent_scratchpad`, `move_type_to_new_file`, `read_file_range`,
`read_log_tail`, `remove_member`, `remove_using`, `run_test_by_filter`,
`tail_tool_log`, `update_file_content`.

Только у форка (2 шт.): `add_member` (слияние четырёх `add_*_to_class` /
`_to_class_bases` в одну вставку сырого `memberSource`), `reload` (перечитывание
`RoslynMcp.jsonc` и lazy load).

Продуктовое решение: наша поверхность не проходит порог «40 тулов» — это
осознанный размен в пользу headless-редактирования и tool-groups. `add_member`
уже **DEFER** в [mcp-tool-surface-evolution Stage 3](../../mcp-tool-surface-evolution/stage-3-v2-spec.md)
(1.x не удаляет три имени и не вводит dispatcher). Из форка сюда не переносить.

## 4. Что у нас уже закрыто (переносить не нужно)

| Находка форка | Наш эквивалент |
|---|---|
| `get_test_list` возвращает **0 тестов** (`58dc361`, резолв атрибута через `.ctor`) | `1.3.28`/`1.3.29` — `TestAttributeMatcher` (обход base-chain, `DataRowAttribute` не маркер). **FQN в JSON не закрыт — см. гэп 10** |
| False «no matching tests» на фильтрованных прогонах (`fa5a264`) | `1.3.23` — dot-bounded suffix и method-only display names |
| Инференс `.slnx` fail-only summary (без `Passed:`) | `1.0.28` |
| Длительности VSTest `[1 s]` / `[1 m 28 s]` | `1.0.32` |
| `apply_patch` `replaceAll` hang | `1.0.30` |
| Design-time warnings как Failure | `1.0.34` (`WorkspaceDiagnosticFormatter.IsMsBuildDesignTimeAdvisory`) |
| `TargetFrameworks` / CrossTargeting → нужен inner TFM | `1.0.33` |
| Pure-Total-only вывод → `partial`, счётчики не выдумываются | есть (`TryParseCountsNearTotalTestsLine` возвращает `null`) |
| Disk-sync мутирует `.csproj` | `1.3.30`/`1.3.31`. Форк, **наоборот**, делает `AddDocument`/`RemoveDocument` + `workspace.TryApplyChanges` (`fork/main` `Services/WorkspaceDocumentDiskSync.cs:90` / `:127`) — ровно паттерн NETSDK1022. Наш вариант уже исправлен, назад не переносим |
| Таймаут `dotnet` + drain pipes после kill | `1.3.32` (форк тоже kill+`ReadToEndAsync`; не регресс и не перенос) |

## 5. Открытые гэпы, закрытые в форке

Приоритет P1 — ломает работу на реальных репозиториях; P2 — мешает, есть обход;
P3 — производительность / UX.

| # | Гэп у нас | Где закрыто в форке | Приоритет | Файл плана |
|---|---|---|---|---|
| 1 | Локаль CLI не зафиксирована → на ru-RU MSBuild/VSTest печатают `Пройдено!`, парсеры не срабатывают | `fork/main@dfde2b9`, `Services/DotNetCliRunner.cs` (`DOTNET_CLI_UI_LANGUAGE=en-US`) | **P1** | [impl 01](implementation/01-cli-locale.md) |
| 2 | VSTest-summary для `.sln` с несколькими тест-сборками склеивает counts из разных блоков | `fork/main@dfde2b9`, `fa5a264`; `TryParseTotalTestsBlocks`, `TryParseEndSummaryLines` | **P1** | [impl 02](implementation/02-vstest-aggregation.md) |
| 3 | `Any CPU` → `AnyCPU` на CLI-пути → MSB4126 на `.sln`; при этом безусловный verbatim форка может сломать `.csproj`-конфигурации | `fork/main@dfde2b9`; идея `LoadedPlatformRaw`, но нужен target-aware выбор | **P1** для `.sln`, перенос с адаптацией | [impl 03](implementation/03-platform-target-aware.md) |
| 4 | `UnresolvedAnalyzerReference` в solution ломает **все** solution-wide `SymbolFinder`-операции (Roslyn 5.9.0 checksum) | `fork@c04e41c`, `Services/WorkspaceAnalyzerSanitizer.cs` | **P1** | [impl 04](implementation/04-analyzer-sanitizer.md) |
| 5 | `failed==0 && exitCode!=0` рендерится как `❌ 0 Tests Failed` (xUnit sibling no-match на `.sln`) | `fork/main@fa5a264` | P2 (на фильтрованном `.sln` — ложный красный) | [impl 05](implementation/05-nonzero-exit.md) |
| 6 | Disk watcher видит только каталог `.sln`, не каталоги всех проектов (multi-root) | `fork@486bd77`, `ComputeWatchRoots` | P2 | [impl 06](implementation/06-multi-root-watcher.md) |
| 7 | Mixed C++/C# решения могут ложно валить `load_workspace`; форк заодно смягчает любой `Project file not found`, что уже может скрыть неполный C# graph | `fork@a33c0ae`, `IsExpectedNonCSharpProjectAdvisory` | P2 для non-C++; missing C# project — DEFER | [impl 07a](implementation/07a-mixed-non-csharp.md) / [07b](implementation/07b-missing-csharp-project.md) |
| 8 | Нет name-based / FQN / positional навигации, `directOnly`, агрегации коллизий, overflow вместо обрезки | `fork@a04dfba…c04e41c`, `NavigationTools` + `SourcePositionHelper`, `SearchOverflowHelper` | P2 (крупно) | [impl 08](implementation/08-navigation.md) |
| 9 | Analyzer/generator DLL вне in-solution проектов (centralized `bin/` через `Directory.Build.props`) может оставаться залоченной | `fork/main@dfde2b9`, `AnalyzerShadowLoader`; гипотеза не подтверждена интеграционным тестом | исследование, не перенос | [impl 09](implementation/09-analyzer-shadow-research.md) |
| 10 | `get_test_list` JSON `fullyQualifiedName` = `IMethodSymbol.ToDisplayString(FullyQualifiedFormat)` (`()` / `global::`, не VSTest FQN) | `fork/main@58dc361`, `BuildMethodFqn`; у нас хелпер уже есть — `TestFilterHelper.FormatVstestFullyQualifiedName` | **P1** (дешёвый), но нужны nested/generic adapter-тесты | [impl 10](implementation/10-test-list-fqn.md) |
| 11 | `search_code` без `directoryPath` сканирует только каталог `.sln`, не внешние project roots | `fork@bc34ccd`; цель верна, `SearchScopeResolver` не переносить как есть | P2 | [impl 11](implementation/11-search-scope.md) |
| 12 | При длинном неразобранном build/test output середина теряется в head/tail excerpt | `fork/main@fa5a264`, `TempReportWriter` | P3, гибридная реализация | [impl 12](implementation/12-lossless-output.md) |

### 5.1. `AnalyzerShadowLoader` (гэп 9) — почему не в плане 1–4, 7

Механизм форка: analyzer DLL проекта, лежащие под каталогом загруженного файла
или внутри `bin`/`obj`, копируются в `%Temp%\RoslynMcpServer\analyzer-shadow\<hash>\`
и предзагружаются в default ALC до построения компиляции. Автор рассчитывает на
identity-unification default ALC: последующая загрузка Roslyn должна вернуть уже
загруженную shadow-копию и не открыть оригинал.

Это пока **гипотеза реализации, а не подтверждённый фикс**. Тесты форка проверяют
только классификацию путей; нет интеграционного теста «генератор реально
выполнился + исходная DLL перезаписывается». Не копируются private dependencies,
не проверены две DLL с одинаковым simple name/разными версиями, а первая версия
остаётся в default ALC до конца процесса. `Cleanup` удаляет только файлы, но не
выгружает сборку. Поэтому пункт 9 нельзя оценивать как готовый P2-патч.

Отличие от нашей `shadowCopyInSolutionAnalyzers` (`1.3.4`, overlay-контракт `1.3.5`–`1.3.8`):

| | наша | форка |
|---|---|---|
| Что чинит | `ProjectReference` с `OutputItemType="Analyzer"` резолвится в отсутствующий путь → генератор не отработал (`CS0103`) | файл в `bin/` залочен → следующий `dotnet build` не перезаписывает |
| Область | in-solution проекты (сопоставление по `AssemblyName`) | любой analyzer под каталогом загрузки / `bin` / `obj` |
| Активация | opt-in, session-sticky (`shadowCopyInSolutionAnalyzers=true`) | всегда, при каждом load |
| Цена | наша подсистема с provenance/admission/письменным контрактом | ~200 строк, но: первая загруженная версия используется до конца процесса (ALC identity), пересобранный анализатор подхватывается **только после рестарта MCP** |

Почему не в плане:

1. Пересекается по цели (держать DLL не залоченной) с уже специфицированной
   подсистемой; второй механизм рядом с ней — риск двух конкурирующих политик
   теневых копий и двух наборов инвариантов записи.
2. `restart-required` семантика и «какая версия анализатора победила» — это ровно
   те вопросы, которые у нас закрыты эпохами analyzer-shadow (loader contract
   `1.3.7`, publication state `1.3.20`), и решение «предзагружать в default ALC»
   нужно совместить с ними, а не добавлять параллельно.
3. Симптом (залоченная DLL при centralized `bin/`) у нас частично диагностируется
   (`logProjectOutputDiagnostics`, `1.3.3`) — то есть это доработка существующей
   подсистемы, а не самостоятельный багфикс.

Практический вывод: если симптом подтвердится на реальном решении владельца
(централизованный `bin/` + анализ), сначала нужен воспроизводящий integration
test с private dependency и rebuild второй версии. Затем решение должно войти в
существующий loader/provenance contract как новая эпоха, а не добавляться вторым
независимым shadow-механизмом.

### 5.2. Что именно принёс `fork/main` поверх `simplification`

Полный дельта-набор (15 файлов, +1 113 / −358). Все изменения уже разобраны выше;
таблица — чтобы не осталось «необработанных» файлов:

| Файл | Куда попал |
|---|---|
| `Services/DotNetCliRunner.cs` (+23) | гэп 1 (локаль). **Не копировать файл целиком:** у нас есть `MSBUILDDISABLENODEREUSE=1`, в форке его нет |
| `Diagnostics/VstestOutputParser.cs` (+427/−) | гэпы 2 и 5 |
| `Services/DotNetConfigurationArguments.cs`, `Services/SolutionManager.cs`, `Tools/BuildTools.cs`, `Tools/TestTools.cs` | гэп 3 (platform verbatim). **Не копировать `DotNetConfigurationArguments` целиком:** форк для build-probe ставит `-c`, у нас `-p:Configuration=` (`FormatConfigurationProperty`) |
| `Services/TestDiscoveryHelper.cs` (+30), `RoslynMcpServer.Tests/TestDiscoveryHelperTests.cs` | 0 тестов закрыто у нас (§4); **FQN в JSON — гэп 10** |
| `Services/AnalyzerShadowLoader.cs` (+198), `AnalyzerShadowLoaderTests.cs` | гэп 9, см. §5.1 |
| `Diagnostics/TempReportWriter.cs` (+79) | замена их `TruncatedProcessLog`; см. ниже |
| `Diagnostics/TruncatedProcessLog.cs` (−84), `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj` | рефакторинг форка, не переносится |
| `RoslynMcpServer.Tests/VstestOutputParserTests.cs` (+299), `DotNetConfigurationArgumentsTests.cs` | тесты к гэпам 2 и 3 |

`TempReportWriter` не переносим **как замену**: у нас verbose-вывод ограничивается
`TruncatedProcessLog` (strip MSBuild-footer, head/tail бюджеты) в рамках
`1.3.22`–`1.3.24`. Полезная часть идеи — lossless диагностический artifact —
оставлена отдельным гибридным кандидатом 12 (§5.4).

### 5.3. `search_code`: scope и ripgrep

Первая проверка списала это как «отдельное направление производительности».
Это два разных слоя; ripgrep не обязателен для первого.

**Scope (P2, корректность multi-root).** У нас `ResolveSearchRootDirectory`
без `directoryPath` берёт `GetLoadedWorkspaceDirectory()` — каталог файла
`.sln`/`.csproj` (`Tools/UtilityTools.cs`). Проекты вне этого дерева не
сканируются. Цель форка (`bc34ccd`) верна, но его `SearchScopeResolver` нельзя
копировать как есть:

- на Unix `GetLowestCommonAncestor` теряет ведущий `/` при `Split(...,
  RemoveEmptyEntries)` и может вернуть относительный путь;
- замена solution root только project directories исключает loose `.cs` у
  корня решения, которые текущее поведение находило;
- merge зависит от фактического списка sibling directories на диске, поэтому
  scope меняется от посторонних каталогов, а не только от workspace graph.

Нормативный вариант для нас: переиспользовать корни пункта 6 — каталог
`.sln`/`.csproj` **плюс** внешние project directories с удалением вложенных
дублей. Это сохраняет текущий scope и добавляет недостающие внешние проекты.
Managed line-scan остаётся достаточным для correctness-фикса.

**Движок (P3, скорость).** `Services/RipgrepRunner.cs`: `rg`/`rg.exe` с PATH
или `ripgrep-path` из `RoslynMcp.jsonc`, уважение `.gitignore`, лимит command
line 24k. Без `rg` — прежний managed scan. Это идея, не drop-in: `--stats`
обычно пишет статистику в stderr, а код считает `files searched` только из
stdout; glob-исключения `!bin`/`!obj` нужно проверить против вложенных каталогов;
stdout сначала целиком накапливается в памяти, то есть cap результата не является
memory cap. Путь можно брать из env (`ROSLYN_MCP_RIPGREP`) или PATH, не вводя
`RoslynMcp.jsonc`.

В текущий план 1–4, 7, 10 **не входит**. Scope имеет смысл делать после гэпа 6
на общем resolver корней. Ripgrep — отдельная оптимизация после correctness.

### 5.4. Полный диагностический output

Форк заменяет inline head/tail на `TempReportWriter`: полный raw output уходит
в `%Temp%`, ответ содержит путь. В основном репо `TruncatedProcessLog` лучше
показывает краткую причину и сохраняет текущий MCP-контракт, но середина длинного
неразобранного лога действительно теряется. Поэтому ценна **гибридная** версия:
оставить inline excerpt и дополнительно писать bounded full report только при
фактическом truncation/unparsed failure.

Код форка как есть не переносить: нет retention/cleanup, size cap, атомарной
записи и явной политики для секретов в process output; путь на host filesystem
может быть бесполезен удалённому клиенту. Это P3 observability, а не замена
пп. 1, 2 и 5.

## 6. Осознанные расхождения (не переносить)

| Решение форка | Почему не переносим |
|---|---|
| Config-based lazy load `RoslynMcp.jsonc` + `reload` + `WorkspacePrewarmService` | Наш контракт — «no implicit semantic workspace load» (`U-ARB-04-IMPL-1`, `1.3.15`). Ценность prewarm реальна, но это смена контракта загрузки, а не багфикс |
| Удаление analyzer shadow / provenance подсистемы | Специфицированное поведение с приёмками (v2/v3, 8 эпох); у форка вместо неё `AnalyzerShadowLoader` (pre-load shadow DLL в default ALC) — тот же класс проблемы, более простой механизм; может быть взят как идея дефолта, но не как замена |
| Удаление `docs/` | Операторская ценность; у нас правила `roslyn-mcp-docs-lifecycle.mdc` |
| Удаление tool-groups/help (`enable_tool_group`, `get_tool_help`, `list_tool_groups`) | Профили `full`/`lite` и `ROSLYN_MCP_TOOL_GROUPS` — shipped-поведение `1.2.0` |
| Удаление editing-тулов (`apply_patch`, `update_file_content`, `get_file_content`, `read_file_range`, `list_directory_tree`) и `find_usages` | Нужны для headless-сценариев; `find_usages` документирован в `AGENTS.md.sample` как name-based вход (см. [navigation-surface-8.md](navigation-surface-8.md) о слиянии) |
| `add_member` вместо четырёх `add_*` | Stage 3 `mcp-tool-surface-evolution`; 1.x не удаляет имена |
| Слепой `failed==0` **без** parsed summary | Не копировать; gated-норма — [п. 5](implementation/05-nonzero-exit.md) |
| xUnit v3 / `[GeneratedRegex]` | Стиль форка; не багфикс |
| `publish-debug.cmd` | Локальный helper форка |

### 6.1. Не регрессировать при переносе

Форк **проще** в нескольких местах, куда нельзя копировать файлы целиком:

| Наше поведение | Форк | Риск |
|---|---|---|
| `MSBUILDDISABLENODEREUSE=1` в `CreateProcessStartInfo` | нет | node reuse + зомби MSBuild после таймаута |
| `FormatConfigurationProperty` → `-p:Configuration=` на build-probe | только `-c` (`FormatSwitch`) | ломает уже покрытые `DotNetBuildProbeTests` |
| Disk-sync только `WithDocumentText` известных документов | `AddDocument`/`RemoveDocument` | NETSDK1022 |
| `TestAttributeMatcher` base-chain (`[WpfFact]`, `[AnalyzerLifecycleFact]`) | `EndsWith("Fact"/"Theory")` + плоский набор имён | ложные промахи/попадания |
| VSTest StdOut/StdErr бюджеты `1.3.24` | temp-файл вместо inline excerpt | не заменять; возможен только гибрид из §5.4 |
| MCP progress heartbeats `1.3.25`–`1.3.27` | нет | UX длинных прогонов |

## 7. Что форк удалил относительно нас

Удалено целиком: `RoslynMcpServer.LifecycleTestHost/`, `RoslynMcpServer.Tests/AnalyzerLifecycle/`,
`Hosting/McpTool*` (catalog/help/groups/activation/profile), `Services/Analyzer*` (9 файлов),
`Services/SemanticPublicationState.cs`, `Services/WorkspaceWriteBoundary.cs`,
`Services/WorkspaceWriteResult.cs`, `Services/InProcessAnalyzerAssemblyLoader.cs`,
`Services/SolutionProjectTargetResolver.cs`, `Services/TestAssemblyPathResolver.cs`,
`Services/TestAttributeMatcher.cs`, `Services/GitChangedFilesHelper.cs`,
`Services/LogTailReader.cs`, `Services/CliProgress.cs`, `Services/ProjectOutputDiagnosticsLogger.cs`,
`Tools/EditingTools.cs`, `Tools/PatchMatchHelper.cs`, `Tools/RoslynTools.cs`,
`Tools/ToolHelpTools.cs`, `Tools/McpToolProgressReporter.cs`,
`Diagnostics/TestOutputReportOptions.cs`, `Diagnostics/WorkspaceLoadDiagnosticsReporter.cs`,
`Diagnostics/TruncatedProcessLog.cs` (заменён `TempReportWriter`).

Часть из этого — наша функциональность (см. §6), часть — альтернативная
реализация (`TestAttributeMatcher` → `TestDiscoveryHelper`, `TruncatedProcessLog`
→ `TempReportWriter`). Из неё не нужен wholesale-перенос; полезна только идея
lossless artifact как дополнение к нашему excerpt (§5.4).

Файлы, которые есть **только** в форке (кроме тестов): `Config/WorkspaceConfig.cs`,
`RoslynMcp.jsonc.sample`, `Services/{AnalyzerShadowLoader,RipgrepRunner,SearchOverflowHelper,SearchScopeResolver,SourcePositionHelper,WorkspaceAnalyzerSanitizer,WorkspacePrewarmService}.cs`,
`Diagnostics/TempReportWriter.cs`, `publish-debug.cmd`.

## 8. Следующие шаги

Порядок и чеклисты — [implementation/README.md](implementation/README.md)
(`10 → 1 → 4 → 2 → 5 → 6 → 3 → 7a → 11`). После каждого ship: patch в
`RoslynMcpServer.csproj`, строка в README «Agent tools by version».
П. 8 — отдельная серия (minor). П. 12 — P3 с policy. П. 9 и 7b не ship.

## 9. Коррекции независимой проверки

Независимый разбор локального `main` `46d796d`, upstream `origin/main`
`bc3ec09`, `fork/simplification` `40020fc` и `fork/main` `58dc361`.
Что было неточно или недоговорено в первой версии каталога:

| Было | Стало |
|---|---|
| `origin/main` = `46d796d` | `46d796d` — локальный `main`; публичный `origin/main` = `bc3ec09`. Два локальных коммита меняют CI/privacy docs, не runtime |
| `58dc361` целиком «уже закрыто `1.3.28`/`1.3.29`» | Закрыт только ноль тестов. JSON FQN всё ещё `FullyQualifiedFormat` (гэп 10). `nameContains` уже идёт через правильный хелпер — рассинхрон внутри одного файла |
| Гэп 6 «уже запланировано отдельно» | В active docs нет плана `ComputeWatchRoots`. `workspace-load-cache` Epoch 3 говорит о coverage map для **кэша**, не shipped и не этот фикс. Гэп переоткрыт как P2 |
| `search_code`/ripgrep — «производительность, не в план» | Scope внешних проектов — **корректность** multi-root (гэп 11), но resolver форка теряет Unix root и сужает текущий solution-root scope; нужен union с корнями п. 6. Ripgrep — отдельный P3 |
| Platform verbatim можно применить ко всему CLI | Exact `Any CPU` нужен solution target; `.csproj` может требовать canonical `AnyCPU`. Выбор должен зависеть от target kind |
| `Project file not found` безопасно считать advisory | Это может быть отсутствующий C# `ProjectReference` и неполная семантическая модель. Автоматически смягчаем только non-C# project diagnostics; missing C# требует degraded-state контракта |
| `IsSoftWorkspaceAdvisory` проверяется после hard/error | В текущем `IsBlockingLoadFailure` soft проверяется **раньше** explicit error/hard. Новый classifier обязан сам отвергать hard/error либо порядок нужно безопасно переработать |
| Sanitizer следует публиковать как новый workspace solution | Форк хранит raw solution и кэширует отдельный sanitized snapshot. Это важно для health/diagnostics и минимизации влияния на analyzer pipeline |
| `AnalyzerShadowLoader` — готовое решение lock-проблемы | Тесты покрывают только пути; нет проверки загрузки generator/private dependencies/rebuild. Оставлен как исследовательский input |
| Temp overflow «не переносить» | Сохранение полного сырого лога ценно как P3, но только дополнение к inline excerpt с retention/size/security policy |
| `Пройден!` | Комментарий форка: `Пройдено!` (ru-RU VSTest) |
| `DotNetConfigurationArguments` форка как drop-in | У них нет `-p:Configuration=` / `MSBUILDDISABLENODEREUSE`; копировать файлы целиком нельзя |
| `+2 488 строк` NavigationTools как рост файла | git `--stat` 1702 ins / 786 del (сумма 2488); итоговый файл форка ~1517 строк + два мелких сервиса |
| Diff code к `simplification` «96 файлов, +5382/−14667» | К `simplification`: 94 файла, +5323/−14639; к `fork/main`: 96, +5899/−14890. Для текущего локального `main` полный diff: 434 / +11797/−56593 и 437 / +12765/−56806 соответственно |
| TryApplyChanges «`SolutionManager.cs:849`» | Актуальный `fork/main`: `WorkspaceDocumentDiskSync.cs` 90/127, apply в `SolutionManager` ~876 |

Подтверждено без изменений: набор тулов 63 vs 41; `simplification` ⊂ `fork/main`;
гэпы 1, 2, 4 и mixed-C++ часть 7 живы в нашем коде; solution-часть гэпа 3
воспроизводима по коду; disk-sync форка по-прежнему `AddDocument`; гэп 9 не
мешать с overlay-контрактом; lazy `RoslynMcp.jsonc` не переносить.

### 9.1. Разбор правок GPT-sol

Проверено по коду нашего `IsBlockingLoadFailure`, форка `GetSanitizedSolution` /
`AnalyzerShadowLoaderTests`, `SearchScopeResolver.GetLowestCommonAncestor`.

| Утверждение | Вердикт |
|---|---|
| Soft в `IsBlockingLoadFailure` идёт **раньше** hard/error | **ACCEPT.** Строки 140–150: soft → затем error token → hard. Мой предыдущий «hard сначала» описывал `IsMsBuildDesignTimeAdvisory`, не весь `IsBlockingLoadFailure`. Classifier 7a обязан сам отвергать `: error MSB` |
| 7a (`.vcxproj` / not associated) vs 7b (`Project file not found` на C#) | **ACCEPT.** Blanket-фраза скрывает дырявый C# graph. 7b только через degraded-контракт |
| Sanitizer — кэш snapshot, не `TryApplyChanges` в workspace | **ACCEPT**, с уточнением: `GetCurrentSolutionAfterDiskSyncAsync` форка **уже возвращает** sanitized. У нас аналог — `GetPublishedSolutionAfterDiskSyncAsync` (overlay/admission). Подменять published raw нельзя: admission увидит другие analyzer refs. Sanitize на границе `SymbolFinder`, не в `SetPublishedSolution` |
| `SearchScopeResolver` теряет `/` на Unix; сужает scope | **ACCEPT.** `Split(..., RemoveEmptyEntries)` + `Join` даёт `home/foo`, не `/home/foo`. Union sln-root ∪ external project dirs |
| `AnalyzerShadowLoader` — гипотеза, тесты только path class | **ACCEPT.** `AnalyzerShadowLoaderTests` — `IsUnderDirectory` / `IsBuildOutputPath`, без generator/lock/rebuild |
| Platform target-aware (`.sln` raw, `.csproj` `AnyCPU`) | **ACCEPT WITH LIMIT.** Склейка `AnyCPU` на `.sln` — доказанный MSB4126. Обратная склейка `Any CPU` на SDK `.csproj` правдоподобна (`bin/Any CPU` vs `bin/AnyCPU`), в форке нет падающего CLI-теста. Не откладывать `.sln`-фикс; ветка по расширению target дешёвая |
| Nested/generic FQN в п. 10 | **PARTIAL.** Нужны как follow-up; не блокер дешёвого `FormatVstestFullyQualifiedName` для обычного `Ns.Type.Method` |
| П. 12 гибридный full report | **ACCEPT как P3-идея**, не как must-ship с 1–4. Security/retention/remote path — обязательны, иначе не делать |
| `origin/main` = `bc3ec09`, HEAD = `46d796d` (+2 docs/CI) | **ACCEPT.** `git log origin/main..HEAD`: telemetry + Actions Node 20 |

Итог: вердикт §0 оставляем. Единственное, что нельзя принять буквально из форка при п. 4 — «отдать sanitized из AfterDiskSync»: у них нет overlay publication.
