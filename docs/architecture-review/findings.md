# Находки

Статусы — только в [README](README.md). Номера строк — по коммиту `56da689`.

<a id="ar-01"></a>
## AR-01 · P1 · README расходится с реальным `tools/list`

**Доказательство.**
- `Hosting/McpToolCatalog.cs:24-35` — 9 tools в `WithheldUntilV2` (`remove_using`,
  `organize_usings`, `add_property_to_class`, `add_field_to_class`,
  `add_type_to_class_bases`, `remove_member`, `implement_interface`,
  `extract_interface`, `move_type_to_new_file`) не попадают в `tools/list`.
- README описывает все 9 как обычные tools без пометки (например `README.md:510`, `:1421`).
- `get_changed_files` и `run_nuget_audit` зарегистрированы, но в списке tools README
  (`<summary>`) отсутствуют.
- `README.md:895`, `:1804`: «ожидай v1.3.32 и 63 tools в `full`, или 19 в `lite`».
  Живой сервер: v1.5.6, `Registered MCP tools: 54`; в каталоге 15 `core`.

**Требование.** `docs/ARCHITECTURE.md:145` (инвариант 9: у публичных метаданных один
владелец), `:166` (шаг 7: обновлять README при изменении видимой агенту политики).
`docs/README.md:7`: корневой README — Reference.

**Последствие.** Агент, читающий README, вызывает несуществующие tools или не знает о
существующих. Проверка после publish по `:895` даёт ложную тревогу на каждом релизе.

**Предложение.** Пометить 9 withheld-tools (или убрать из README до v2), добавить два
недостающих, заменить числа на «сверь с `get_mcp_server_info`». Дёшево закрепить
тестом: сравнить имена в `<summary><code>…</code>` README с `McpToolCatalog.All` и
`WithheldUntilV2`. Цена: низкая.

<a id="ar-02"></a>
## AR-02 · P1 · Конфиг целевого репозитория управляет загрузкой плагинов

**Доказательство.**
- `Program.cs:122-140`: `ROSLYN_MCP_WORKSPACE` делает каталог репозитория cwd процесса.
- `Program.cs:55-58`: `RoslynMcp.jsonc` читается после этого (каталог exe, затем cwd).
- `Config/RoslynMcpFileSettings.cs:225-227`: `plugins` из cwd целиком заменяет список из
  каталога exe.
- В `Plugins/` нет проверки подписи/strong name; плагин получает весь
  `IServiceCollection` хоста (`Plugins/RoslynMcpPluginContext.cs`).
- `ROSLYN_MCP_PLUGINS` не упомянут в корневом README (описан только в
  `samples/RoslynMcpPlugin/README.md`).

**Требование.** `docs/ARCHITECTURE.md:148` (инвариант 12): сервер доверяет клиенту и
OS-идентичности. Доверие к *содержимому открываемого репозитория* не сформулировано.

**Последствие.** Клонированный чужой репозиторий с `RoslynMcp.jsonc` исполняет код в
процессе MCP-хоста при старте — до любого вызова tool и до решения агента загрузить
workspace. Оговорка: `load_workspace` недоверенного решения и так исполняет его MSBuild-
логику; разница в моменте (старт процесса) и в том, что плагин живёт в хосте всю сессию.

**Предложение.** Решение владельца — [P-01](proposals.md#p-01).

<a id="ar-03"></a>
## AR-03 · P1 · Отгруженное поведение обосновано отложенным черновиком

**Доказательство.**
- `docs/ARCHITECTURE.md:45`: `docs/backlog/workspace-load-cache/` — «forward-looking …
  not shipped behavior».
- `docs/ARCHITECTURE.md:79`: текущая политика «live semantic reads trust the watcher …»
  и отвергнутые альтернативы — ссылкой на
  `backlog/workspace-load-cache/epoch-3-live-consistency/spec.md`; у spec статус
  «proposed», задачи эпохи — planned; сама программа отложена (`docs/README.md:30-33`).

**Требование.** `docs/README.md:49`: после ship факты копируются в ARCHITECTURE / README.

**Последствие.** Решение, по которому работает production watcher, существует только в
документе, который индекс велит не считать нормой. При архивировании backlog обоснование
потеряется.

**Предложение.** Перенести в ARCHITECTURE (или короткий contract-файл) выбранную
политику и список отвергнутых альтернатив; ссылку на backlog оставить как историю.
Цена: низкая.

<a id="ar-04"></a>
## AR-04 · P1 · Расхождение статусов workspace-state-cache

**Доказательство.**
- `docs/README.md:18`: «S-002 not-run». В README темы, README эпох E0/E1 и в
  `task-11-…-hi.md` — accepted (bounded test-only partial experiment, 2026-10-09).
  Коммит `fc72dc8` обновил всё, кроме индекса.
- `docs/backlog/workspace-load-cache/README.md:9`, `docs/backlog/README.md:14`:
  state-cache — «ready-for-review; реализация не начата».
- `docs/workspace-state-cache/validation.md:3,10-12`: «Обновлено 2026-10-08 … task-00
  accepted, task-08 accepted, остальные planned … S-002 not-run».
- `docs/workspace-state-cache/benchmark.md:3`: «Числа ускорения ещё не получены» —
  после принятого task-07 benchmark.
- Дата статуса спеки: 2026-10-09 в индексе, 2026-10-07 в README темы (`:3`).
- В индексе не отражено, что E1/task-00 blocked.

**Требование.** `AGENTS.md:28`: task, index, report и packet должны быть согласованы.

**Последствие.** Агент, читающий индекс (как велит `docs/README.md:3`), получает неверную
картину; при возобновлении экспериментов легко перезапустить уже принятую работу.

**Предложение.** Синхронизировать перечисленные места. Структурная причина — AR-16.
Цена: низкая.

<a id="ar-05"></a>
## AR-05 · P2 · Экспериментальный StateCache в production-сборке

**Контекст (со слов владельца).** Код появился в ходе экспериментов с кэшированием
MSBuild/solution; эксперименты упёрлись в тупик, но продолжаются.

**Доказательство.**
- `Services/Workspace/StateCache/` — около 2,9 тыс. строк, компилируется в
  `RoslynMcpServer.dll`.
- Живой `find_usages WorkspaceSnapshotCodec`: 51 ссылка — тесты
  (`RoslynMcpServer.Tests/WorkspaceStateCache/*`) и сама папка StateCache; из
  `SolutionManager`, `Tools/`, `Hosting/` — ни одной.
- `docs/ARCHITECTURE.md:67` верно говорит «no on-disk evaluation cache yet», но о
  существовании экспериментального кода не сообщает.

**Требование.** `docs/README.md:7` (ARCHITECTURE описывает систему как она есть),
`AGENTS.md:29` (приёмка изолированного эксперимента не означает production readiness).

**Последствие.** Читатель кода не отличает эксперимент от рабочего пути; ничто не
мешает случайно вызвать StateCache из production — структурные тесты проверяют изоляцию
гидратора (`IsolatedWorkspaceHydratorTests.cs`), но не отсутствие production-вызовов.

**Предложение.** Решение владельца о размещении — [P-02](proposals.md#p-02). Независимо
от него: одна строка в ARCHITECTURE и запись тупика в самой теме
`docs/workspace-state-cache/` (что пробовали, где упёрлись, что продолжается).

<a id="ar-06"></a>
## AR-06 · P2 · Загрузка блокирует все семантические запросы

**Доказательство.**
- `Services/Workspace/SolutionManager.cs:858-981`: `LoadAndPrepareAsync` держит
  `_workspaceLock` на всё время MSBuild-открытия и подготовки анализаторов.
- `SolutionManager.cs:480-492`: публичный `GetSanitizedPublishedSolution()` ждёт тот же
  лок через синхронный `_workspaceLock.Wait()`. Метод доступен плагинам (они получают
  `SolutionManager` через DI). Структурное правило `BlockingMemberRules` запрещает
  `Task.Wait`/`.Result`, но не `SemaphoreSlim.Wait()`.

**Требование.** `docs/ARCHITECTURE.md:78` упоминает задержку лишь общей фразой
(«long workspace operations can delay other semantic calls»); `:144` (инвариант 8)
требует бюджета и cancellation для долгих операций.

**Последствие.** На больших решениях каждый семантический вызов ждёт загрузку целиком;
синхронный вызов из плагина занимает поток пула на то же время.

**Предложение.** Без переделки модели: явно описать в ARCHITECTURE и в руководстве по
плагинам, что sync-метод блокирует на время загрузки; рассмотреть сужение его видимости
до `internal`, если внешним потребителям он не нужен. Учесть при интеграции кэша (AR-10).

<a id="ar-07"></a>
## AR-07 · P2 · Одна запись кэша workspace и рост памяти

**Доказательство.**
- `SolutionManager.cs:3061`: единственное поле `MSBuildWorkspace? _workspace`.
- `SolutionManager.cs:2037-2040`: при промахе (другой путь, конфигурация, платформа или
  TFM) workspace уничтожается и открывается заново.
- `docs/ARCHITECTURE.md:67`: «caches it in process by path plus MSBuild configuration,
  platform, and target framework» — читается как кэш с несколькими ключами.
- CLR-сборки анализаторов не выгружаются (`ARCHITECTURE.md:81`, `:85`); единственная
  защита — предупреждение в лог при 1,5 ГБ (`SolutionManager.cs:3035`, `ARCHITECTURE.md:89`).

**Последствие.** Переключение TFM/конфигурации — полная перезагрузка; память процесса
монотонно растёт с каждым новым набором анализаторов. Для долгих сессий на больших
решениях это предел без сигнала, кроме строки в логе.

**Предложение.** Исправить формулировку в ARCHITECTURE (кэш на один ключ). Для
диагностики — показывать working set и число загрузок в `get_mcp_server_info`, чтобы
агент мог предложить перезапуск. Цена: низкая.

<a id="ar-08"></a>
## AR-08 · P2 · Shadow-копии плагинов не удаляются

**Доказательство.**
- `Plugins/PluginShadowCopy.cs:92`, `:115`: каждый старт создаёт
  `{temp}/…/plugins/{Guid}`; удаления в файле нет.
- Для сравнения: поколения анализаторов
  (`Services/Analyzers/AnalyzerReferenceShadowCopier.cs:87`) тоже без GC, но это
  задокументировано (`ARCHITECTURE.md:85`, `AnalyzerShadowCacheCleanup.cs:4-7`), а
  `TryDeleteRoot` вызывается только из тестов.

**Последствие.** При dev-плагине (`ROSLYN_MCP_PLUGINS`, shadow copy) каждый перезапуск
хоста оставляет полную копию плагина в temp.

**Предложение.** При старте удалять папки предыдущих копий, чьи процессы завершены
(или хотя бы задокументировать рядом с правилом для анализаторов). Для анализаторов —
описать ручную процедуру очистки для оператора. Цена: низкая–средняя.

<a id="ar-09"></a>
## AR-09 · P2 · `list_projects` читает csproj строковым разбором

**Доказательство.** `Tools/UtilityTools.cs:1441-1446`: TFM и OutputType берутся из
`File.ReadAllTextAsync(project.FilePath)` через `ExtractSimpleCsprojValue`
(`:1715`, поиск по `IndexOf(openTag)`).

**Требование.** `docs/ARCHITECTURE.md:40`, `:160-161`: Tools — протокольные адаптеры,
поведение — в Services; данные проекта уже есть в загруженном Roslyn `Project`.

**Последствие.** Значения из `Directory.Build.props`, импортов и условий не видны:
выводится `(unknown)` или значение без учёта условия — при том что `load_workspace` в том
же процессе показывает тип проекта по результату MSBuild-оценки.

**Предложение.** Брать OutputType из `CompilationOptions.OutputKind`, TFM — из
загруженного проекта, если доступен; строковый разбор оставить запасным вариантом.
Остальная логика в Tools (поиск в `UtilityTools.cs:677-913`, оркестрация тестов в
`TestTools.cs:421-740`) дефектов не показала — переносить её ради соответствия схеме
не предлагается.

<a id="ar-10"></a>
## AR-10 · P3 · Концентрация в SolutionManager перед интеграцией кэша

**Доказательство.**
- `SolutionManager.cs` — 3072 строки: загрузка/кэш, публикация snapshot'ов,
  shadow-copy анализаторов, граница записи, watcher, non-C# sync, staleness, MEF.
- Живой поиск: 36 из 39 обращений к `Workspace.CurrentSolution` — в этом классе.
- 17 совпадений `ForTests|TestSeam|TestHook` в классе; `ApplyCaptureTestSeams()`
  вызывается в production-пути загрузки (`:935`, тело `:1417-1432`).
- План E1 (`docs/workspace-state-cache/README.md`, раздел «Маршрут») предусматривает
  интеграцию в host/preparation/load/watch/write.

**Последствие.** Интеграция кэша по умолчанию ляжет в этот же класс и под этот же лок
(AR-06), увеличив число состояний, которые должны держать инварианты публикации.

**Предложение.** Разделять класс сейчас не нужно — его защищают структурные тесты. До
начала E1-интеграции решить, через какой узкий API кэш взаимодействует с
`SolutionManager`, и записать это в спецификации E1. Тестовые флаги в production-пути —
оставить, если они дешевле альтернатив, но перечислить их в ARCHITECTURE.

<a id="ar-11"></a>
## AR-11 · P3 · Статическое состояние вне инварианта single-session

**Доказательство.** `Services/DiagnosticReportStore.cs:22-27` и
`Services/Navigation/NavigationOverflowStore.cs:17-20` — статические хранилища курсоров
(TTL, лимит записей), общие для процесса.

**Требование.** `docs/ARCHITECTURE.md:61`, `:143` (инвариант 7) называют только
singleton-регистрации DI.

**Последствие.** При будущем переходе на per-session scopes эти хранилища останутся
общими между сессиями, хотя инвариант этого не подсказывает.

**Предложение.** Дописать их в текст инварианта 7. Код не менять. Цена: минимальная.

<a id="ar-12"></a>
## AR-12 · P3 · Платформы: CI и релиз только Windows

**Доказательство.**
- `.github/workflows/test-suite.yml:18`, `release.yml:25`, `:90` — `windows-latest`;
  публикуется только `win-x64` (`release.yml:121-133`).
- `RoslynMcpServer.csproj:4` объявляет 6 RID, включая linux и osx.
- `RoslynMcpServer.Tests/Support/WpfFactAttribute.cs:9-11` пропускает тест вне Windows,
  а `.github/scripts/Assert-NoSkippedTests.ps1` считает любой skip провалом.
- Сообщения об ошибках предполагают `Program Files\dotnet`
  (`Services/Build/DotNetCliRunner.cs:85`, `:183`, `:508`).
- В этом ревью сервер собран и успешно отработал на Linux (SDK 10.0.113).

**Последствие.** Поддержка не-Windows заявлена RID-ами и фактически работает, но ничем
не проверяется; регресс заметят только пользователи.

**Предложение.** Решение владельца — [P-03](proposals.md#p-03).

<a id="ar-13"></a>
## AR-13 · P3 · SDK не закреплён

**Доказательство.** `global.json` нет (`docs/ARCHITECTURE.md:175`).
`test-suite.yml:40-45` ставит `10.0.300` и `10.0.x` (комментарий: последний 10.0.x уже
ломал fixture builds); `release.yml:112` — только `10.0.x`.

**Последствие.** Бинарь релиза собирается SDK, который был последним на runner'е в день
релиза; воспроизвести сборку позже нельзя.

**Предложение.** Либо `global.json` в репозитории, либо явная версия SDK в release
workflow. Цена: низкая.

<a id="ar-14"></a>
## AR-14 · P3 · Классификация read/write не доходит до протокола

**Доказательство.** `Hosting/McpToolDescriptor.cs:16-29`: `IsReadOnly` /
`ExecutesProcess` используются для строки `Kind` в `get_tool_help`; MCP-аннотации
(`readOnlyHint`, `destructiveHint`) не выставляются. Для плагинов
`Plugins/RoslynMcpPluginContext.cs:128` всегда ставит `IsReadOnly = true`.

**Требование.** `docs/ARCHITECTURE.md:37`: каталог — источник истины для
read/write/process-классификации.

**Последствие.** Клиент не может отличить читающие tools от пишущих и процессных
(например, для автоподтверждения). Если аннотации когда-нибудь начнут экспортироваться,
плагинные tools окажутся помечены как read-only.

**Предложение.** При экспорте аннотаций не переносить захардкоженное значение для
плагинов (дать плагину объявлять его). Пока экспорта нет — только пометка. Цена: низкая.

<a id="ar-15"></a>
## AR-15 · P3 · Поиск ссылок на символы из метаданных по FQN

**Доказательство (живой вызов).** `find_symbol_references` с
`symbolName = "Microsoft.CodeAnalysis.Workspace.TryApplyChanges"` →
«No declaration matches FQN … (and no symbols share the simple name)». Тот же поиск по
`filePath+line+column` места вызова нашёл 3 использования. Описание параметра
(«Simple name or exact FQN») ограничения на source-символы не оговаривает.

**Последствие.** Типичный вопрос агента «где используется API библиотеки X» требует
сначала найти любое место вызова текстовым поиском.

**Предложение.** Оговорить ограничение в описании tool и/или искать FQN также в
metadata-символах компиляций. Цена: низкая (описание) – средняя (поиск).

<a id="ar-16"></a>
## AR-16 · P3 · Объём process-документации и ручная синхронизация статусов

**Доказательство.**
- `docs/`: 1003 файла, около 109 тыс. строк; production C# — около 46 тыс. строк.
- С 2026-09-01: 143 из 196 коммитов меняют только `docs/`.
- `docs/workspace-state-cache/`: 591 файл, из них около 400 — evidence json/txt/trx
  (около 45 тыс. строк); коммит `fc72dc8` добавил два почти одинаковых binlog-JSON
  (`task-11/` и `task-11-recheck/`).
- Статус одной задачи по `docs/README.md:54-165` и `AGENTS.md:24-29` ведётся в шапке
  задачи, README эпохи, README темы, индексе и validation; ячейка статуса в
  `docs/README.md:18` — около 2 тыс. символов.
- В `workspace-state-cache` используются `archive/` и корневой `review/` вместо
  `_archive/` из правил индекса (`docs/README.md:44`, `:48`).

**Последствие.** AR-04 — прямое следствие: обновление «забывает» одно из мест. Объём
делает аудит дорогим и для людей, и для агентов, которым индекс велит читать канон.

**Предложение.** Решение владельца — [P-04](proposals.md#p-04).

<a id="ar-17"></a>
## AR-17 · P3 · Мелкие расхождения документации

Каждое исправляется правкой текста; собраны вместе, чтобы закрыть одним коммитом.

| Где | Сейчас | Факт |
|---|---|---|
| `docs/ARCHITECTURE.md:47` | «two projects and one dependency edge» | 5 проектов (`load_workspace`): Tests → Server, LifecycleTestHost, DeadlockWitnessHost; LifecycleTestHost → Server; RoslynMcpPlugin → Server |
| `docs/ARCHITECTURE.md:166` | «Update `README.md` tool history» | история версий только в `CHANGELOG.md`, README на него ссылается |
| `docs/ARCHITECTURE.md:152-156` | раздел на русском в английском документе | — |
| `docs/ARCHITECTURE.md:82` | `C:\Scratch\GenRepro` | машинный путь |
| `.cursor/rules/roslyn-mcp-overview.mdc:26` | `c:\Devel\BrqMover` с оговоркой «do not hardcode that path» | правило нарушает само себя |
| `.cursor/rules/roslyn-mcp-overview.mdc:28-100` | история версий | нет 1.5.5, 1.4.0–1.4.14, 1.3.33–1.3.41; `roslyn-mcp-version-bump.mdc` не требует её обновлять |
| `docs/mcp-tool-surface-evolution/README.md:19` | база v1.3.24, 63/19, «нет CI test gate» | v1.5.6, 54/15, CI есть; часть этапов 0 и 2 отгружена |
| `docs/mcp-tool-surface-evolution/README.md:52` | «`PackageVersion` (`0.1.0-beta`) не bumpать» | `roslyn-mcp-version-bump.mdc:22`: `PackageVersion` нет и не добавлять |
| `docs/github-releases/README.md:41` | «`PackageVersion` отличается от …» | `PackageVersion` в csproj нет |
| `RoslynMcpServer.csproj:25` | `Description` из шаблона SDK | — |
| `docs/archive/mcp-plugins/v2/README.md:59` | ссылка на `Services/SolutionManager.cs` (контракт плагинов, на который ссылается ARCHITECTURE) | файл в `Services/Workspace/` |
| `.cursor/skills/epoch-subagent-loop` vs `.agents/skills/epoch-subagent-loop` | две версии одного скилла | `.cursor` содержит дополнительный раздел приёмки |
| `docs/README.md` | `docs/code-style.md` не в индексе | — |
