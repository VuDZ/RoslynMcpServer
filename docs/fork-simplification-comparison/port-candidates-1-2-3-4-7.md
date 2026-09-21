# План переноса из форка: пункты 10, 1, 2, 3, 4, 6, 7

Статус: **proposal, не реализовано**. Это разбор, не чеклист ship.
Реализация: [implementation/README.md](implementation/README.md).

Base truth — локальный `main`
`46d796d258c9af5212caa4fb673cc896ab1ac53e` (v1.3.32); публичный
`origin/main` — `bc3ec0946def13ee575a81873854ad6bb1001758`, форк —
`VladD2/RoslynMcpServer@main` `58dc361248fb51c06f1ab5c131849bbb4a1a9883`.

Общий контекст — [README.md](README.md). **Ship:** [implementation/README.md](implementation/README.md).
Пункты 8, 9, 11, 12 — свои файлы в `implementation/`.
Пункт 5 — после п. 2.

Порядок работ: **10 → 1 → 4 → 2 → 6 → 3 → 7a**. Пункты 10, 1 и 4 имеют смысл
как отдельные patch-релизы; 2 и 3 меняют то, что видит агент в отчётах/CLI, и
проверяются на реальном многосборочном решении. `7b` (missing C# project) не
входит в перенос без отдельного degraded-workspace контракта.

**Не копировать файлы форка целиком.** У нас должны остаться
`MSBUILDDISABLENODEREUSE=1`, `FormatConfigurationProperty` (`-p:Configuration=`
на build-probe), disk-sync без `AddDocument`, `TestAttributeMatcher` base-chain
(см. [каталог §6.1](README.md#61-не-регрессировать-при-переносе)).

---

## 10. `get_test_list` VSTest FQN

**Симптом.** JSON-поле `fullyQualifiedName` в `get_test_list` собирается через
`symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`
(`Services/TestDiscoveryHelper.cs:97`).

`SymbolDisplayFormat.FullyQualifiedFormat` для метода даёт `global::Ns.Type.Method()`
(скобки ломают VSTest `--filter`, pitfall 11). Форк (`58dc361`) дополнительно
фиксирует случай `GetDeclaredSymbol` → в FQN остаётся одно имя метода. Фильтр
`nameContains` в том же файле уже идёт через правильный хелпер
(`TestFilterHelper.FormatVstestFullyQualifiedName`, строка 247) — агент видит
один FQN в JSON и другой смысл в фильтре.

Первая проверка каталога записала весь `58dc361` в «уже закрыто `1.3.28`».
Закрыт только ноль тестов (`TestAttributeMatcher`). Тестов на форму JSON FQN
у нас нет.

**Как в форке.** `BuildMethodFqn`: FQN типа (без `global::`) + `.` + имя метода,
без `()`. Рецепт совпадает с нашим `FormatVstestFullyQualifiedName`.

**Правка.** В payload `get_test_list` вызывать
`TestFilterHelper.FormatVstestFullyQualifiedName(symbol)`. Новый хелпер не
нужен. Не подменять matcher форковским `EndsWith("Fact")`.

**Тесты** (по образцу `fork/RoslynMcpServer.Tests/TestDiscoveryHelperTests.cs`):

- класс без namespace → `MyTests.DoesThing`;
- класс в namespace → `Acme.Tests.WidgetTests.Parses`;
- нет `global::`, нет `()`.
- nested class и generic/parameterized test — значение сверяется с FQN,
  который реально сообщает установленный VSTest adapter (форма может зависеть
  от adapter, например separator nested type).

**Acceptance.** Значение из `get_test_list` можно передать в VSTest
`FullyQualifiedName=...`/`~...` без ручной зачистки как минимум для обычных и
nested тестовых классов поддерживаемых adapters. Fork-тесты доказывают только
обычный `Namespace.Class.Method`, не весь adapter-specific FQN contract.

**Риск.** Минимальный. Единственный контрактный сдвиг: агенты, которые уже
парсили `global::`/`()`, увидят VSTest-форму. Это исправление, не фича.

---

## 1. `DOTNET_CLI_UI_LANGUAGE=en-US`

**Симптом.** На машине с русской UI-локалью MSBuild/VSTest локализуют консоль
(`Пройдено!` вместо `Passed!`). Наши парсеры — английские, поэтому прогон тестов
уходит в `Status: partial`, а не в разобранный summary.

**Где видно у нас.**

- `Services/DotNetCliRunner.cs` — `CreateProcessStartInfo` (строка 327) выставляет
  только `MSBUILDDISABLENODEREUSE` (строка 343); `DOTNET_CLI_UI_LANGUAGE` не задаётся
  нигде в репозитории (проверено `git grep`).
- `Diagnostics/VstestOutputParser.cs` — `RxTotalTests` (строка 61),
  `RxVstestPassedLine` (26), `RxEndSummaryLine`, `RxTestRunSuccessful` (78):
  все шаблоны — английские литералы.

**Как в форке.** `fork/main@dfde2b9`, `Services/DotNetCliRunner.cs:267`:

```csharp
psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
```

В комментарии форка: «a ru-RU machine prints `Пройдено!` instead of `Passed!`».
Переменная наследуется host → MSBuild → VSTest → testhost.

**Правка.** Ставить `DOTNET_CLI_UI_LANGUAGE=en-US` в `CreateProcessStartInfo`
(а не в отдельных вызовах), чтобы покрыть `run_dotnet_build`, `run_dotnet_test`,
`run_specific_test`, `run_test_by_filter`, `run_dotnet_run`, `run_nuget_audit`
одной точкой. **Рядом оставить** `MSBUILDDISABLENODEREUSE=1` (в форке этой
строки нет). Плюс — значение должно попадать в metadata отчёта, чтобы
`diagnostics` не приходилось гадать о локали.

**Тесты.** Unit: `ProcessStartInfo`, возвращаемый фабрикой, содержит
`DOTNET_CLI_UI_LANGUAGE=en-US` (по аналогии с существующими
`DotNetTestArgumentsTests` / `DotNetConfigurationArgumentsTests`).
Ручная приёмка: на ru-RU машине `run_dotnet_test` по тестовому решению даёт
`Total/Passed/Failed`, а не `partial`.

**Acceptance.** Ни один тестовый/билдовый прогон не зависит от UI-локали ОС;
парсинг не имеет ветвлений по локали.

**Риск.** Минимальный: переменная влияет только на сообщения CLI, не на коды
возврата и не на формат VSTest `--logger`, который мы задаём сами.

---

## 2. Агрегация VSTest-блоков

**Симптом.** На `.sln` с несколькими тест-сборками каждый проект печатает свой
блок `Total tests: N / Passed: N / Skipped: N`. Мы берём **первый** блок и
дочитываем counts по всему тексту — итог склеивается из разных сборок.
В форке на `Nitra.sln` это давало `Total: 1, Skipped: 2` вместо `254 / 252 / 2`.

**Где видно у нас.**

- `Diagnostics/VstestOutputParser.cs:81` — `RxVstestTotalsBlock`
  (`Total tests: N` + `Passed: M` в пределах 2000 символов) применяется как
  `Match(...)`, то есть к первому блоку: строка 465.
- `Diagnostics/VstestOutputParser.cs:573` — `TryReadCountAfterTotalTests`
  ищет `Failed:` / `Skipped:` **первым совпадением по всему тексту**, вне
  границ блока: строки 471–472.
- `Diagnostics/VstestOutputParser.cs:448` — `RxEndSummaryLine` перебирается с
  перезаписью `lastEnd`, то есть берётся последняя end-summary строка, а не сумма
  по сборкам: строки 448–455.

**Как в форке** (`fork/main@dfde2b9`, `fa5a264`, `Diagnostics/VstestOutputParser.cs`):

- `TryParseTotalTestsBlocks` — цикл по всем `Total tests:` строкам,
  накопление `total/passed/failed/skipped` по блокам; per-block скан
  `TryParseCountsNearTotalTestsLine` останавливается на следующем `Total tests:`,
  поэтому counts не «протекают» между блоками. Нюанс форка: блок только с
  `Total` всё равно прибавляет `total` и идёт дальше; если **ни один** блок не
  дал counts — `null`/`partial`; смешанный truncated (один блок с counts, другой
  Total-only) даёт `Total != Passed+Failed+Skipped`. При переносе явно решить:
  либо как в форке, либо Total-only в смеси тоже → `null`. Для нашего контракта
  выбираем fail-closed: любой `Total > 0` блок без counts делает общий summary
  `null`; иначе получается внутренне противоречивое `Total != Passed+Failed+Skipped`.
  `Total tests: 0` без count-lines допустимо агрегировать как нулевой блок.
- Если counts не найдены ни в одном блоке — возвращается `null` (значит
  `partial`, счётчики не выдумываются).
- `TryParseEndSummaryLines` (строка 527) — при `matches.Count > 1` суммирует все
  `Passed! / Failed! - Failed: …, Passed: …` строки.

**Правка.**

1. Добавить агрегирующий `TryParseTotalTestsBlocks` и использовать его вместо
   одиночного `RxVstestTotalsBlock.Match`.
2. Ограничить per-block скан следующей строкой `Total tests:` (сейчас — окно
   24 строки, `VstestOutputParser.cs:533`).
3. Заменить `lastEnd` на агрегацию всех end-summary совпадений.
4. Сохранить текущие инварианты: `Total`-only без counts → `null` → `partial`;
   `.slnx` fail-only (есть `Failed:`, нет `Passed:`) → инференс `Passed`;
   моно-сборочный вывод не меняется; для каждого нечастичного результата
   `Total == Passed + Failed + Skipped`.

**Тесты.** Фикстуры (по образцу `VstestOutputParserTests`):

- две сборки, обе прошли — Total/Passed суммируются;
- две сборки, первая 0 совпадений по фильтру, вторая 3 passed;
- fail-only блок в одной сборке и `Passed!`-end-summary во второй;
- обрезанный вывод «только Total» в обоих блоках → `null`/`partial`;
- одна сборка — регресс на текущее поведение.

**Acceptance.** На реальном многосборочном `.sln` `run_dotnet_test` без фильтра
рапортует суммарные counts, равные сумме по сборкам; counts никогда не берутся
из разных блоков.

**Риск.** Средний: это горячий путь разбора, легко сломать `.slnx` fail-only.
Порядок предпочтений источников должен остаться явным и покрытым тестами:
end-summary → агрегированные totals-блоки → line-wise fallback.

**Замечание.** Пункт 5 ([ship](implementation/05-nonzero-exit.md))
делать после этой правки: `failed==0` на склейке блоков ещё не доказан, и часть
ложных «no matching tests» тоже из-за смеси сборок.

---

## 3. `Platform` verbatim на CLI

**Симптом.** `.sln` знает только свои точные имена конфигураций (`Debug|Any CPU`
с пробелом). Мы алиасим `Any CPU` → `AnyCPU` на CLI-пути, и MSBuild падает:
`MSB4126: The specified solution configuration "Debug|AnyCPU" is invalid`.

**Где видно у нас.**

- `Services/DotNetConfigurationArguments.cs:43` — `NormalizePlatform` делает
  алиас `Any CPU` → `AnyCPU`.
- `Services/DotNetConfigurationArguments.cs:57` — `CoalescePlatform` идёт через
  `NormalizePlatform`; строка 83 `FormatPlatformProperty`, строка 106
  `AppendPlatform` — тоже.
- Вызовы: `Tools/BuildTools.cs:74`, `Tools/TestTools.cs:391`,
  `Services/DotNetTestArguments.cs:29` и `:67`.
- `Services/SolutionManager.cs:306` — load нормализует платформу, а
  `_loadedPlatform` (строка 1662, публичное `LoadedPlatform`, строка 250)
  сохраняет **уже алиаснутое** значение, которое затем наследуют build/test при
  опущенном аргументе.

**Как в форке** (`fork/main@dfde2b9`, коммит-сообщение):

- `CoalescePlatform` / `FormatPlatformProperty` передают платформу **без
  изменений** для всех CLI targets;
- `NormalizePlatform` сохраняет алиас `AnyCPU` **только** для global properties
  `MSBuildWorkspace`;
- `SolutionManager` хранит `LoadedPlatformRaw` (trimmed, без алиаса), и build/test
  наследуют именно его.

Это исправляет `.sln`, но не является универсальной CLI-семантикой. Для
SDK-style `.csproj` custom conditions/output paths обычно используют
`Platform=AnyCPU`; безусловная передача `Any CPU` может выбрать другой набор
properties. Поэтому fork patch требует target-aware адаптации.

**Правка.**

1. Хранить raw и canonical platform. Для `.sln`/`.slnx` CLI использовать raw
   (trim + валидация); для `.csproj` — canonical `NormalizePlatform`. Не делать
   `CoalescePlatform` глобально verbatim без знания target path. **Не удалять**
   `FormatConfigurationProperty` / `AppendConfigurationProperty`
   (`-p:Configuration=` на probe): форк для build ставит только `-c`, у нас
   тесты `DotNetBuildProbeTests` ждут property.
2. В `SolutionManager`: ввести `LoadedPlatformRaw` рядом с `LoadedPlatform`;
   `LoadCoreAsync` получает оба значения (алиас — для workspace, raw — для
   наследования в build/test).
3. `Tools/BuildTools.cs`, `Tools/TestTools.cs` при опущенном `platform` выбирают
   raw/canonical по фактическому target (`workspacePath`/test target), а не
   всегда берут `LoadedPlatformRaw`.

**Тесты.**

- Существующий `NormalizePlatform("Any CPU") == "AnyCPU"` остаётся.
- Новые target-aware тесты: solution formatter даёт
  `-p:Platform="Any CPU"`, project formatter — `-p:Platform="AnyCPU"`.
- Тест на `NormalizePlatform` для workspace-пути остаётся (алиас `AnyCPU`).
- Тест наследования: `load_workspace` с `platform="Any CPU"` → build/test без
  явного `platform` получают verbatim `Any CPU` для `.sln`/`.slnx`.
- Парный тест `.csproj`: тот же ввод даёт `Platform="AnyCPU"` и не меняет
  условные properties/output path.

**Acceptance.** `run_dotnet_build` / `run_dotnet_test` по `.sln` с
`Debug|Any CPU` больше не дают MSB4126; `.slnx`-путь не регрессирует; прямой
`.csproj` сохраняет `AnyCPU`; workspace по-прежнему грузится с `AnyCPU`.

**Риск.** Средний: значение платформы влияет и на путь `bin/`, и на global
properties; нужны явные тесты на `.sln`, `.slnx` и `.csproj`, желательно на
реальном решении владельца с непустым `Platform`.

---

## 4. `WorkspaceAnalyzerSanitizer`

**Симптом.** Если analyzer DLL не найдена на диске (кастомный анализатор не
собран под текущую конфигурацию), `MSBuildWorkspace` добавляет в проект
`UnresolvedAnalyzerReference`. Roslyn 5.9.0 не может посчитать checksum проекта
с таким стабом: `SerializerService.CreateChecksum` бросает
`InvalidOperationException: Unexpected value '…UnresolvedAnalyzerReference'`.
Checksum считает `DependentTypeFinder` для любого solution-wide поиска, поэтому
одна отсутствующая DLL ломает **все** solution-wide `SymbolFinder`-операции,
независимо от того, в каком проекте искомый символ.

**Где видно у нас.** `UnresolvedAnalyzerReference` не упоминается в репозитории
ни разу (`git grep` — 0 совпадений). При этом solution-wide поиск у нас есть:
`Tools/NavigationTools.cs` (`FindUsages` — `SymbolFinder.FindReferencesAsync`,
строка 323; `FindSymbolReferences`, `FindImplementations`), `CallGraphHelper`,
`rename_symbol`. Roslyn у нас 5.9.0 (`1.0.35`), то есть дефект платформы
воспроизводим.

**Как в форке** (`fork@c04e41c`, `Services/WorkspaceAnalyzerSanitizer.cs`, ~90 строк):

- `RemoveUnresolvedAnalyzers(Solution)` — убирает стабы из всех проектов, возвращает
  тот же инстанс `Solution`, если удалять нечего, иначе новый + `RemovedCount`.
- `IsUnresolvedAnalyzerError(Exception)` — `InvalidOperationException` с
  `UnresolvedAnalyzerReference` в тексте.
- `WithSanitizedRetryAsync(search, resanitize, solution, ct)` — один retry на
  освобождённом от стабов solution; исходное исключение пробрасывается, если
  `resanitize` вернул тот же/`null` solution.

Ключевой аргумент из комментария: стаб не предоставляет анализатор (его сборка
никогда не загружалась), поэтому удаление не меняет диагностики, а checksum
снова становится вычислимым.

**Правка.** Перенести статический helper, но повторить важную архитектурную
деталь форка: raw `MSBuildWorkspace.CurrentSolution` остаётся источником истины,
а `GetSanitizedSolution()` кэширует отдельный snapshot по identity raw solution.
Не публиковать sanitized snapshot обратно в workspace — иначе health/reporting
потеряет сведения о missing analyzer, а analyzer pipeline получит неочевидно
изменённую модель. После disk-sync кэш инвалидируется естественно сменой raw
solution. Solution-wide поиски получают sanitized snapshot; retry нужен как
защита от смены raw solution между получением snapshot и поиском.

У нас нельзя повторить удобный API форка «AfterDiskSync сразу возвращает
sanitized»: `GetPublishedSolutionAfterDiskSyncAsync` кормит overlay/admission.
Sanitize на границе `SymbolFinder` (отдельный `GetSanitizedPublishedSolution` /
retry), raw published не подменять.

**Тесты** (рецепт из `fork/RoslynMcpServer.Tests/WorkspaceAnalyzerSanitizerTests.cs`):

- `AdhocWorkspace` + два проекта с `ProjectReference`, затем
  `project.AddAnalyzerReference(new UnresolvedAnalyzerReference(path))` +
  `workspace.TryApplyChanges(...)`; после `RemoveUnresolvedAnalyzers` стабов нет
  и `SymbolFinder` находит базовый/производный тип;
- `IsUnresolvedAnalyzerError`: true для текста с `UnresolvedAnalyzerReference`,
  false для `"boom"` и для другого `…SomeOtherReference`;
- `WithSanitizedRetryAsync`: поиск падает на «грязном» solution, succeeds на
  санитизированном, `Retried == true`; при `ReferenceEquals` — rethrow оригинала.

**Acceptance.** `find_usages` / `find_symbol_references` / `get_call_graph` /
`find_implementations` / `rename_symbol` работают на solution с отсутствующей
analyzer DLL; health/load diagnostics по-прежнему показывают unresolved path из
raw solution; поведение на «здоровых» solution не меняется (sanitize — no-op,
тот же инстанс).

**Риск.** Низкий. Ограничение: если стаб появился **после** последней
санитизации, спасает только retry-путь — он должен быть подключён во всех
solution-wide точках, иначе дефект снова станет невидимым.

---

## 7. Mixed C++/C# и отсутствующие проекты

Здесь форк объединяет два разных случая. Их нельзя переносить одной проверкой.

**7a, переносим: mixed non-C# project.** Смешанное C++/C#-решение даёт diagnostic
`file extension '.vcxproj' is not associated with a language`. Для native
project C#-сервисы неприменимы, а загруженные C# projects остаются полезными.
Это корректный non-blocking advisory, который остаётся видимым.

**7b, не переносим автоматически: missing C# project.** `Project file not found`
может означать отсутствующий `ProjectReference` на C# библиотеку. Тогда workspace
graph и семантика реально неполны; обычный success скрывает потерю типов и
ссылок. Виртуализованный checkout может быть допустимым режимом, но для него
нужен отдельный контракт: `loaded (degraded)`, список пропущенных project
paths/count и явное влияние на solution-wide results.

**Как в форке** (`fork@a33c0ae`, `Diagnostics/WorkspaceDiagnosticFormatter.cs:89`):

```csharp
public static bool IsExpectedNonCSharpProjectAdvisory(string message) =>
    message.Contains("is not associated with a language", StringComparison.OrdinalIgnoreCase)
    || message.Contains("Project file not found", StringComparison.OrdinalIgnoreCase)
    || RxQuotedNonCSharpProjectExtension().IsMatch(message);
```

`RxQuotedNonCSharpProjectExtension` — `['"]\.(?:vcx|cpp|wix|sql|njs|sh|cd|db|x|fsx)proj['"]`
(`IgnoreCase | CultureInvariant`). Смысл второго маркера: английская фраза не
переживёт локализацию MSBuild, а закавыченное **расширение** (`.vcxproj`) —
переживёт, потому что этот diagnostic family квотит расширение целиком, а не путь.
Метод добавлен в конец `IsSoftWorkspaceAdvisory`.

**Где видно у нас.** `Diagnostics/WorkspaceDiagnosticFormatter.cs:80` —
`IsSoftWorkspaceAdvisory` = NuGet audit / prune / compat / design-time MSBuild,
и всё. Ни `vcxproj`, ни «Project file not found» не обрабатываются
(`git grep` по репозиторию — совпадений в наших мягких путях нет).

**Правка для 7a.**

1. Добавить `IsExpectedNonCSharpProjectAdvisory` только для фразы
   `is not associated with a language` и закавыченного non-C# расширения. Не
   включать blanket `Project file not found`.
2. Включить метод в `IsSoftWorkspaceAdvisory`, чтобы `IsBlockingLoadFailure`
   возвращал `false`. Диагностика **остаётся видимой** в ответе.
3. `IsBlockingLoadFailure` сейчас проверяет soft **до** explicit error/hard.
   Поэтому новый classifier должен сам возвращать false при
   `HasExplicitErrorToken`/`IsHardMsBuildLoadFailure`, либо порядок проверок
   нужно переработать с регресс-тестами существующих advisory. Нельзя полагаться
   на «hard сначала» — это не соответствует текущему коду.
4. Дополнительно (низкий приоритет, по желанию): ограничить verbose-список
   диагностик 20 строками со счётчиком — как `MaxDiagnosticsInResponse = 20`
   в `fork/Tools/WorkspaceTools.cs:147`. У нас уже есть `briefOutput` и
   `WorkspaceLoadDiagnosticsReporter`, поэтому это UX-правка, а не дефект.

**Тесты** (`WorkspaceDiagnosticFormatterTests`):

- `IsSoftWorkspaceAdvisory` true для английской фразы «is not associated with a
  language» и локализованного текста с `".vcxproj"`;
- `Project file not found` для `.csproj` остаётся blocking до реализации 7b;
- сообщение одновременно с non-C# marker и `: error MSB...` остаётся blocking;
- жёсткие кейсы не стали мягкими: `NETSDK1045`, `XMakeElements`,
  `ResolvePackageAssets`+`TargetFramework`, `does not contain 'Compile' target`,
  `: error NU/MSB/NETSDK`.

**Acceptance 7a.** `load_workspace` по mixed C++/C# решению возвращает
загруженный C# workspace + видимый список пропущенных native projects, а не
«Workspace Load Failed». Missing C# project остаётся failure (будущий 7b может
дать явный degraded result, но не обычный success).

**Риск.** Средний: слишком широкая мягкость маскирует реальные ошибки. В текущем
коде soft-check идёт раньше hard/error, поэтому защиту надо обеспечить внутри
нового classifier или доказанным reorder. `The imported project was not found`,
`: error NU/MSB/NETSDK` и missing C# `.csproj` остаются жёсткими.

---

## 6. Multi-root disk watcher

**Симптом.** `StartDiskWatcherUnderLock` ставит один `FileSystemWatcher` на
каталог загруженного `.sln`/`.csproj` (`Services/SolutionManager.cs:1838`).
Проекты вне этого дерева (multi-root / `Directory.Build.props` layout) не
дают dirty-set для saved `.cs`. Pitfall 20 про IDE/git sync тогда не
выполняется для этих файлов.

Первая проверка написала «уже запланировано отдельно». Active-спека
[`workspace-load-cache`](../workspace-load-cache/README.md) Epoch 3 требует
coverage map для **хеш-кэша** (не shipped, U-ARB-02 открыт) — это не
`ComputeWatchRoots` и не замена тактическому фиксу.

**Как в форке** (`486bd77`): `ComputeWatchRoots(loadedFilePath, projectFilePaths)`
— каталог загруженного файла ∪ каталоги проектов, без вложенных дублей;
по одному recursive watcher на корень; сбой одного корня не слепит остальные.
Чистая функция, покрыта `SolutionManagerWatchRootsTests` /
`SolutionManagerExternalRootWatchTests`.

**Правка.** Перенести `ComputeWatchRoots` + `StartDiskWatchersUnderLock` (список
watcher'ов вместо одного). Политику disk-sync **не** менять: по-прежнему только
`WithDocumentText` известных документов, без `AddDocument`. Ancestor
`Directory.Build.props` выше всех project dirs этим не закрывается — это уже
Epoch 3.

**Тесты.** Рецепт форка: nested dir отбрасывается; внешний project dir остаётся
корнем; пустые пути игнорируются.

**Acceptance.** Изменение уже известного workspace документа `.cs` в проекте
вне папки `.sln` попадает в dirty-set и видно `find_symbol_*` без
`reset_workspace`. Добавление/удаление файлов намеренно не обещается: наша
disk-sync policy не делает `AddDocument`/`RemoveDocument`, чтобы не вернуть
NETSDK1022-регрессию.

**Риск.** Несколько `FileSystemWatcher` на Windows (буфер 64KB каждый) + Linux
inotify. Как сейчас: ошибка старта корня → log, degrade, не crash. Не
расширять filter на `obj`/`bin` (pitfall 20 / overlay).

---

## Общие требования к переносу

- Стиль: минимальный диф, без переписывания файлов; `async`/`await` без
  `.Result`/`.Wait()`; null-check входов.
- Каждый пункт — свои unit-тесты в `RoslynMcpServer.Tests` (xUnit).
- После успешного self-build: patch-версия в `RoslynMcpServer.csproj`
  (`Version`/`AssemblyVersion`/`FileVersion`), строка в README
  «Agent tools by version»; `AGENTS.md.sample` правится только если меняется
  session policy (здесь не меняется).
- Ничего из §6 каталога (lazy config load, prewarm, удаление analyzer-подсистемы)
  в рамках этих пунктов не трогаем.
- Не регрессировать §6.1 каталога (`MSBUILDDISABLENODEREUSE`,
  `-p:Configuration=`, disk-sync, `TestAttributeMatcher`).
