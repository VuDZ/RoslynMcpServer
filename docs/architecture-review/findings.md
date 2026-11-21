# Находки

Статусы — только в [README](README.md). Номера строк — по коммиту `4bd1d20` (версия 1.5.24),
кроме закрытых находок: у них номера строк остались от коммита `56da689`.

У каждой находки одна и та же структура: что проверено в последнем раунде, доказательство,
нарушенное требование, последствие, предложение. Сначала идут открытые находки по номерам,
в конце файла — закрытые.

## Открытые

<a id="ar-06"></a>
### AR-06 · P2 · Загрузка блокирует все семантические запросы

**Проверено 2026-11-15:** подтверждено; уточнены назначение sync-метода и предложение.

**Доказательство.**
- `Services/Workspace/SolutionManager.cs:983-1141`: `LoadAndPrepareAsync` держит
  `_workspaceLock` на всё время MSBuild-открытия и подготовки анализаторов.
- `SolutionManager.cs:529-540`: публичный `GetSanitizedPublishedSolution()` ждёт тот же
  лок через синхронный `_workspaceLock.Wait()`. Это сделано намеренно: метод — источник повтора
  при сбое контрольной суммы проекта в Roslyn 5.9.0, его передают лямбдой из девяти мест в
  `Tools/` (например `Tools/NavigationTools.cs:202`). Форму метода закрепляет
  `SourceStructure/SanitizedEntryChecksTests.cs`, а `BlockingMemberRules.cs:12-13` явно
  разрешает `SemaphoreSlim.Wait()`.
- Метод публичный, поэтому доступен плагинам: они получают `SolutionManager` через DI.

**Требование.** `docs/ARCHITECTURE.md:78` упоминает задержку лишь общей фразой
(«long workspace operations can delay other semantic calls»); `:159` (инвариант 8)
требует бюджета и cancellation для долгих операций.

**Последствие.** На больших решениях каждый семантический вызов ждёт загрузку целиком.
Синхронный вызов занимает поток пула на то же время и не отменяется токеном вызова.

**Предложение.** Модель не переделывать. Описать в ARCHITECTURE и в руководстве по
плагинам, что sync-метод блокирует на время загрузки. Сделать метод `internal`: все его
вызовы находятся в сборке сервера. Насколько часто вызовы стоят за загрузкой, теперь можно
оценить по статистике использования: она записывает длительность вызовов и ожидание лока
загрузкой (`SolutionManager.cs:986`). Учесть при интеграции кэша (AR-10).

<a id="ar-07"></a>
### AR-07 · P2 · Одна запись кэша workspace и рост памяти

**Проверено 2026-11-15:** подтверждено.

**Доказательство.**
- `SolutionManager.cs:3363`: единственное поле `MSBuildWorkspace? _workspace`.
- `SolutionManager.cs:2209-2213`: при промахе (другой путь, конфигурация, платформа или
  TFM) workspace уничтожается и открывается заново.
- `docs/ARCHITECTURE.md:67`: «caches it in process by path plus MSBuild configuration,
  platform, and target framework» — читается как кэш с несколькими ключами.
- CLR-сборки анализаторов не выгружаются (`ARCHITECTURE.md:81`, `:85`). Единственная
  защита — предупреждение в лог при 1,5 ГБ (`SolutionManager.cs:3285-3296`, `ARCHITECTURE.md:89`).
- После раунда 1 память процесса стал записывать сборщик статистики
  (`Diagnostics/UsageStatistics/UsageMemorySampler.cs`), но в ответы tools она не попадает:
  в `Tools/` и `Hosting/` обращений к working set нет.

**Последствие.** Переключение TFM или конфигурации — полная перезагрузка; память процесса
монотонно растёт с каждым новым набором анализаторов. Для долгих сессий на больших
решениях это предел без сигнала агенту.

**Предложение.** Исправить формулировку в ARCHITECTURE (кэш на один ключ). Для
диагностики — показывать working set и число загрузок в `get_mcp_server_info`, чтобы
агент мог предложить перезапуск. Что делать с памятью по существу, решает тема
[usage-statistics](../usage-statistics/README.md) по собранным данным. Цена: низкая.

<a id="ar-08"></a>
### AR-08 · P2 · Shadow-копии плагинов не удаляются

**Проверено 2026-11-15:** подтверждено.

**Доказательство.**
- `Plugins/PluginShadowCopy.cs:90-93`, `:115`: каждый старт создаёт
  `{temp}/…/plugins/{Guid}`; удаления в файле нет.
- Для сравнения: поколения анализаторов тоже без сборки мусора, но это
  задокументировано (`ARCHITECTURE.md:85`, `Services/Analyzers/AnalyzerShadowCacheCleanup.cs:3-8`),
  а `AnalyzerShadowCacheCleanup.TryDeleteRoot` вызывается только из тестов.

**Последствие.** При dev-плагине (`ROSLYN_MCP_PLUGINS`, shadow copy) каждый перезапуск
хоста оставляет полную копию плагина в temp.

**Предложение.** При старте удалять папки предыдущих копий, чьи процессы завершены,
или хотя бы задокументировать это рядом с правилом для анализаторов. Для анализаторов —
описать ручную процедуру очистки для оператора. Цена: низкая–средняя.

<a id="ar-10"></a>
### AR-10 · P3 · Концентрация в SolutionManager перед интеграцией кэша

**Проверено 2026-11-15:** подтверждено; класс вырос с 3072 до 3375 строк.

**Доказательство.**
- `SolutionManager.cs` — 3375 строк: загрузка и кэш, публикация snapshot'ов,
  shadow-copy анализаторов, граница записи, watcher, non-C# sync, staleness, MEF, а после
  раунда 1 ещё прогрев поиска, уборка BuildHost и точки сбора статистики.
- Текстовый поиск: 39 из 52 упоминаний `CurrentSolution` в production-коде — в этом классе.
- 19 совпадений `ForTests|TestSeam|TestHook` в классе; `ApplyCaptureTestSeams()`
  вызывается в production-пути загрузки (`:1075`, тело с `:1580`).
- План E1 (`docs/workspace-state-cache/README.md:39`, раздел «Маршрут») предусматривает
  интеграцию в host/preparation/load/watch/write.

**Последствие.** Интеграция кэша по умолчанию ляжет в этот же класс и под этот же лок
(AR-06), увеличив число состояний, которые должны держать инварианты публикации.

**Предложение.** Разделять класс сейчас не нужно — его защищают структурные тесты. До
начала E1-интеграции решить, через какой узкий API кэш взаимодействует с
`SolutionManager`, и записать это в спецификации E1. Тестовые флаги в production-пути —
оставить, если они дешевле альтернатив, но перечислить их в ARCHITECTURE.

<a id="ar-11"></a>
### AR-11 · P3 · Статическое состояние вне инварианта single-session

**Проверено 2026-11-15:** подтверждено.

**Доказательство.** `Services/DiagnosticReportStore.cs:22-24` и
`Services/Navigation/NavigationOverflowStore.cs:17-19` — статические хранилища курсоров
(TTL, лимит записей), общие для процесса.

**Требование.** `docs/ARCHITECTURE.md:61`, `:158` (инвариант 7) называют только
singleton-регистрации DI.

**Последствие.** При будущем переходе на per-session scopes эти хранилища останутся
общими между сессиями, хотя инвариант этого не подсказывает. Вопрос стал ближе:
исследование общего workspace host ведётся в ветке `research/workspace-host-process`.

**Предложение.** Дописать их в текст инварианта 7. Код не менять. Цена: минимальная.

<a id="ar-14"></a>
### AR-14 · P3 · Классификация read/write не доходит до протокола

**Проверено 2026-11-15:** подтверждено.

**Доказательство.** `Hosting/McpToolDescriptor.cs:16-32`: `IsReadOnly` и
`ExecutesProcess` используются для строки `Kind` в `get_tool_help`; MCP-аннотации
(`readOnlyHint`, `destructiveHint`) не выставляются — поиск по `Hosting/` и `Plugins/`
их не находит. Для плагинов `Plugins/RoslynMcpPluginContext.cs:128` всегда ставит
`IsReadOnly = true`.

**Требование.** `docs/ARCHITECTURE.md:37`: каталог — источник истины для
read/write/process-классификации.

**Последствие.** Клиент не может отличить читающие tools от пишущих и процессных
(например, для автоподтверждения). Если аннотации начнут экспортироваться,
плагинные tools окажутся помечены как read-only.

**Предложение.** При экспорте аннотаций не переносить захардкоженное значение для
плагинов: дать плагину объявлять его. Пока экспорта нет — только пометка. Цена: низкая.

<a id="ar-15"></a>
### AR-15 · P3 · Поиск ссылок на символы из метаданных по FQN

**Проверено 2026-11-15:** описание tool не изменилось; живой вызов не повторялся.

**Доказательство.**
- Живой вызов раунда 1: `find_symbol_references` с
  `symbolName = "Microsoft.CodeAnalysis.Workspace.TryApplyChanges"` →
  «No declaration matches FQN … (and no symbols share the simple name)». Тот же поиск по
  `filePath+line+column` места вызова нашёл 3 использования.
- `Tools/NavigationTools.cs:29-33`: описание tool и параметра («Simple name or exact FQN»)
  ограничения на source-символы не оговаривают.
- `README.md:235`: с 1.5.9 поиск объявлений по имени намеренно идёт только по исходникам.
  Значит, ограничение — принятое поведение, а не дефект поиска.

**Последствие.** Типичный вопрос агента «где используется API библиотеки X» требует
сначала найти любое место вызова текстовым поиском, а описание tool этого не подсказывает.

**Предложение.** Оговорить ограничение в описании tool и подсказать обходной путь
(позиция вызова). Искать FQN ещё и в metadata-символах — отдельное решение. Цена: низкая.

<a id="ar-16"></a>
### AR-16 · P3 · Объём process-документации и ручная синхронизация статусов

**Проверено 2026-11-15:** подтверждено, объём вырос; одна цифра раунда 1 отозвана.

**Доказательство.**

| Показатель | Раунд 1 | Раунд 2 |
|---|---|---|
| Файлов в `docs/` | 1003 | 1359 |
| Строк в `docs/` | около 109 тыс. | около 166 тыс. |
| Строк production C# | около 46 тыс. | около 49 тыс. |
| Самая длинная ячейка статуса в `docs/README.md` | около 2 тыс. символов | 2176 символов (`:19`) |

- `docs/workspace-state-cache/`: 591 файл, из них 314 — evidence json/txt/trx; коммит
  `fc72dc8` добавил два почти одинаковых binlog-JSON (`task-11/` и `task-11-recheck/`).
  С 2026-10-28 в `main` лежит ещё и исследование `docs/workspace-load-performance/` — 299 файлов.
- Статус одной задачи по правилам `docs/README.md` и `AGENTS.md` ведётся в шапке
  задачи, README эпохи, README темы, индексе и validation.
- Ячейки статуса новых тем в индексе уже по 975 и 1159 символов (`docs/README.md:25`, `:23`):
  в них пересказана история исправлений и проверок.
- В `workspace-state-cache` используются `archive/` и корневой `review/` вместо
  `_archive/` из правил индекса (`docs/README.md:49`, `:53`).

**Отозвано.** В раунде 1 было записано, что с 2026-09-01 только `docs/` меняют 143 из 196
коммитов. Пересчёт по `main` без merge-коммитов даёт 45 из 204 на 2026-10-21 и 61 из 236
на 2026-11-15. Откуда взялась прежняя цифра, установить не удалось.

**Последствие.** AR-04 и AR-21 — прямое следствие: обновление «забывает» одно из мест.
Объём делает аудит дорогим и для людей, и для агентов, которым индекс велит читать канон.

**Предложение.** Решение владельца — [P-04](proposals.md#p-04).

<a id="ar-17"></a>
### AR-17 · P3 · Мелкие расхождения документации

**Проверено 2026-11-15:** все строки таблицы подтверждены, исправленных нет; обновлены номера
строк и цифры.

Каждое исправляется правкой текста; собраны вместе, чтобы закрыть одним коммитом.

| Где | Сейчас | Факт |
|---|---|---|
| `docs/ARCHITECTURE.md:47` | «two projects and one dependency edge» | 5 проектов в `RoslynMcpServer.sln`: Tests → Server, LifecycleTestHost, DeadlockWitnessHost; LifecycleTestHost → Server; RoslynMcpPlugin → Server |
| `docs/ARCHITECTURE.md:181` | «Update `README.md` tool history» | история версий только в `CHANGELOG.md`, README на него ссылается |
| `docs/ARCHITECTURE.md:167-171` | раздел на русском в английском документе | — |
| `docs/ARCHITECTURE.md:82` | `C:\Scratch\GenRepro` | машинный путь |
| `.cursor/rules/roslyn-mcp-overview.mdc:26` | `c:\Devel\BrqMover` с оговоркой «do not hardcode that path» | правило нарушает само себя |
| `.cursor/rules/roslyn-mcp-overview.mdc:8`, `:28-30` | «currently 1.5.15», история версий начинается с 1.5.7 | версия 1.5.24; `roslyn-mcp-version-bump.mdc` не требует обновлять этот файл |
| `docs/mcp-tool-surface-evolution/README.md:14`, `:19` | база v1.3.24, 63/19, «отсутствие CI test gate» | v1.5.24, 54/15, CI есть; часть этапов 0 и 2 отгружена |
| `docs/mcp-tool-surface-evolution/README.md:52` | «`PackageVersion` (`0.1.0-beta`) не bumpать» | `roslyn-mcp-version-bump.mdc:22`: `PackageVersion` нет и не добавлять |
| `docs/github-releases/README.md:45` | «`PackageVersion` отличается от …» | `PackageVersion` в csproj нет |
| `RoslynMcpServer.csproj:31` | `Description` из шаблона SDK | — |
| `docs/archive/mcp-plugins/v2/README.md:59` | ссылка на `Services/SolutionManager.cs` (контракт плагинов, на который ссылается ARCHITECTURE) | файл в `Services/Workspace/` |
| `.cursor/skills/epoch-subagent-loop` и `.agents/skills/epoch-subagent-loop` | две версии одного скилла | файлы `SKILL.md` различаются |
| `docs/README.md` | `docs/code-style.md` не в индексе | на него ссылается `AGENTS.md` |
| `README.md:229`, `:305`, `:906` и русские `:1166`, `:1229`, `:1821` | числа 54 и 15 вписаны вручную в шести местах | сейчас верны; тест README с каталогом не сверяет (остаток [AR-01](#ar-01)) |

<a id="ar-19"></a>
### AR-19 · P2 · ARCHITECTURE не описывает форк BuildHost и смешивает разделы

Найдено 2026-11-15, раунд 2.

**Доказательство.**
- С 1.5.16–1.5.17 сервер по умолчанию грузит проекты собственным форком BuildHost
  (`forks/roslyn-buildhost`, подключён через `RoslynMcpServer.csproj` и
  `forks/roslyn-buildhost/build/BuildHostFork.targets`). Форк меняет порядок загрузки: сначала
  ссылки, evaluation наперёд на рабочих потоках, общий кэш SDK resolution.
- В `docs/ARCHITECTURE.md` слово BuildHost встречается один раз — в инварианте 10 (`:161`), про
  single-file. Форка, его выключателей (`ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS`,
  `ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS`, ключи `buildhost-*` в `RoslynMcp.jsonc`) и
  условия сборки «только пакет 5.9.0» там нет. Они описаны в `README.md:279-280`,
  `forks/roslyn-buildhost/README.md` и в теме исследования.
- Раздел «Deployment and compatibility» (`:186-196`) содержит поведение загрузки, а не
  развёртывания: `MissingSourceFileTextLoader` (`:190`), хранилище индексов Roslyn (`:192`),
  прогрев поиска (`:194`). Раздел «Workspace lifecycle» (`:63-89`) о них не упоминает.
- Список «Key runtime components» (`:35-45`) не называет `Plugins/`, `Config/`, `forks/` и
  `Diagnostics/UsageStatistics/`.

**Требование.** `docs/README.md:7` (ARCHITECTURE — как система ведёт себя сейчас), `:54` (после
ship факты копируются в ARCHITECTURE).

**Последствие.** Агент, который по ARCHITECTURE разбирается в медленной или упавшей загрузке,
не узнаёт, что проекты грузит изменённый BuildHost и что его можно выключить одной переменной.
Читатель раздела о жизненном цикле workspace пропускает три механизма, которые на него влияют.

**Предложение.** Добавить в «Workspace lifecycle» абзац о форке: что он меняет, как выключить,
ссылка на `forks/roslyn-buildhost/README.md`. Перенести туда же три абзаца о загрузке из
раздела о развёртывании. Дополнить список компонентов. Код не менять. Цена: низкая.

<a id="ar-20"></a>
### AR-20 · P2 · Привязка к внутренностям Roslyn 5.9.0 без общего списка

Найдено 2026-11-15, раунд 2.

**Доказательство.** После раунда 1 в коде накопились места, которые зависят от конкретной
версии или от недокументированного устройства Roslyn:

| Место | От чего зависит | Что будет при другой версии |
|---|---|---|
| `forks/roslyn-buildhost/build/BuildHostFork.targets:15`, `:54-55` | протокол клиента BuildHost из пакета 5.9.0 | сборка падает с понятной ошибкой |
| `RoslynMcpServer.csproj`, пакет `SQLite3MC.PCLRaw.bundle` 2.3.5 | версия `SQLitePCLRaw.batteries_v2`, на которую ссылается Workspaces | `RoslynIndexStorageTests` падает; без теста — индексы пересоздаются при каждом старте |
| `Services/Workspace/MissingSourceFiles.cs:121-125` | protected-методы `Workspace.On*TextLoaderChanged`, вызов рефлексией | загрузчик не ставится, в логе `installed: false` |
| `Services/Workspace/RoslynDocumentBuildSideEffect.cs:42` | непубличный член `IsGenerated` состояния документа, чтение рефлексией | возвращается `null`, сбоя нет |
| `Services/Workspace/UnregisteredBuildHostReaper.cs` | порядок запуска и регистрации BuildHost в 5.9.0 | код становится лишним после коммита Roslyn `2624f5c156` |
| `Services/Analyzers/WorkspaceAnalyzerSanitizer.cs:11`, `SolutionManager.cs:524` | сбой контрольной суммы проекта в 5.9.0 | обход остаётся в пути поиска и держит sync-метод из AR-06 |
| `docs/ARCHITECTURE.md:192` | путь кэша из внутреннего `DefaultPersistentStorageConfiguration` | документация и PRIVACY.md называют неверный каталог |

Каждое место описано рядом с кодом или в своей задаче, но списка всех мест нет ни в
ARCHITECTURE, ни в `forks/roslyn-buildhost/README.md`, ни в `.cursor/rules`. Обновление пакета
при этом запланировано: задача task-00b форка ждёт пакета новее 5.9.0
(`docs/workspace-load-performance/research-buildhost-fork/README.md:43`).

**Требование.** `docs/ARCHITECTURE.md:162` (инвариант 11) уже задаёт такой порядок для MCP SDK:
при обновлении пакета перепроверить названный класс и его тесты. Для Roslyn такого правила нет.

**Последствие.** Первые два места остановят обновление сами. Остальные пять деградируют молча
или остаются мёртвым кодом: сборка и тесты пройдут, а поведение изменится. Тот, кто обновляет
пакет, должен найти их поиском по «5.9.0» и рефлексии.

**Предложение.** Один раздел «Обновление Roslyn» в ARCHITECTURE: таблица выше, для каждой
строки — тест, который её проверяет, и условие удаления обхода. Инвариант по образцу 11.
Для двух мест с рефлексией добавить тест, который падает, если член не найден. Цена: низкая.

<a id="ar-21"></a>
### AR-21 · P3 · Реализованные темы остаются в разделе Active

Найдено 2026-11-15, раунд 2.

**Доказательство.**
- `docs/README.md:8`: Active — «план, который ещё может менять код».
- `docs/README.md:24`: [user-data-directory](../user-data-directory/README.md) — все решения
  O-01…O-04 приняты, реализовано и проверено в 1.5.19.
- `docs/README.md:25`: [disk-watcher-inotify](../disk-watcher-inotify/README.md) — решения
  O-01…O-05 приняты, реализовано в 1.5.23, исправление 1.5.24 проверено на Windows и Linux.
- Факты обеих тем уже перенесены в `docs/ARCHITECTURE.md:69`, `:133-138`.
- Для сравнения: серия linux-x64-support после завершения ушла в `docs/archive/` (коммит `ca1ac4a`).

**Требование.** `docs/README.md:54` (после ship design-пакет помечается shipped), `:57` (когда
всё принято и открытых вопросов нет — убрать тему в `docs/archive/`).

**Последствие.** Раздел Active растёт и перестаёт отвечать на вопрос «что сейчас в работе».
Агент читает как действующий план то, что уже стало поведением сервера.

**Предложение.** Проверить, что в обеих темах нет открытых пунктов, и перенести их в
`docs/archive/` с короткой строкой в Record. Цена: низкая. Общая причина — AR-16.

## Закрытые

Номера строк в исторических доказательствах относятся к ревизиям соответствующих раундов.

<a id="ar-01"></a>
### AR-01 · P1 · README расходится с реальным `tools/list`

**Закрыта 2026-10-22**, коммит `22b9924`. Проверено 2026-11-15: 54 имени в `<summary><code>…</code>`
README совпадают с 54 зарегистрированными tools; девять скрытых перечислены в `README.md:305`
как недоступные до v2; `get_changed_files` и `run_nuget_audit` описаны; проверка после publish
ссылается на `Version` из csproj.

**Что было.** `Hosting/McpToolCatalog.cs:24-35` скрывает 9 tools (`WithheldUntilV2`), а README
описывал их как обычные и не описывал два зарегистрированных. `README.md:895`, `:1804` велели
ожидать «v1.3.32 и 63 tools в `full`, или 19 в `lite`» при живых v1.5.6 и 54/15.

**Остаток.** Предложенный тест, который сверяет имена в README с `McpToolCatalog`, не добавлен.
Числа 54 и 15 вписаны в README вручную в шести местах; это записано строкой в [AR-17](#ar-17).

<a id="ar-12"></a>
### AR-12 · P3 · Платформы: CI и релиз только Windows

**Закрыта 2026-10-22** по решению [P-03](proposals.md#p-03): коммиты `0cf6b05` (CI на Linux и
Windows), `b270eba` (релиз публикует `win-x64` и `linux-x64`), `408e02f` (поиск SDK на Linux и
описание платформ). Серия — [linux-x64-support](../archive/linux-x64-support/README.md).
Проверено 2026-11-15: в `test-suite.yml:24-47` четыре job'а — `unit` и `lifecycle` на
`windows-latest` и `ubuntu-latest`; `release.yml:96-105` собирает оба архива; сообщения об
отсутствующем SDK различают Windows и остальные системы (`Services/Build/DotNetCliRunner.cs:526-534`).

**Что было.** `test-suite.yml:18`, `release.yml:25`, `:90` — только `windows-latest`,
публиковался только `win-x64` при 6 объявленных RID. `WpfFactAttribute` пропускал тест вне
Windows, а `Assert-NoSkippedTests.ps1` считает любой skip провалом. Сообщения об ошибках
предполагали `Program Files\dotnet`.

**Остаток.** Остальные четыре RID собираются, но не проверяются и не поддерживаются; это
записано в `docs/ARCHITECTURE.md:188`.

<a id="ar-13"></a>
### AR-13 · P3 · Корневой global.json отсутствовал

**Закрыто, проверено 2026-11-21.** Коммит `59f6936` добавил [global.json](../../global.json)
с базой 10.0.100 и `latestFeature`. Релизный workflow закрепляет SDK 10.0.402 с `disable`.
Допустимые версии локального SDK описаны в [архитектуре](../ARCHITECTURE.md#deployment-and-compatibility).
Серверный выбор SDK исправлен отдельно в [AR-18](#ar-18).

<a id="ar-02"></a>
### AR-02 · P1 · Конфиг из рабочего каталога управляет загрузкой плагинов

**Закрыто 2026-11-21.** Коммит `abb4f9d`. [Проверка](priority-fixes.md#проверка)

**Проверено 2026-11-21.** Подтверждено по коду на `6d88c97`. Вариант 1 P-01 принят к реализации. [Сверка](priority-fixes.md).

**Проверено 2026-11-15:** подтверждено, область стала шире. Исправлена одна часть: `ROSLYN_MCP_PLUGINS`
теперь описан в корневом README (`README.md:261`).

**Доказательство.**
- `Program.cs:150-161`: `ROSLYN_MCP_WORKSPACE` делает каталог репозитория рабочим каталогом процесса.
  Клиент может запустить сервер в каталоге проекта и без этой переменной.
- `Program.cs:59-61`: `RoslynMcp.jsonc` читается после этого из трёх мест.
- `Config/RoslynMcpFileSettings.cs:187`: порядок слияния — каталог exe, каталог пользователя,
  рабочий каталог. `:273-275`: ключ `plugins` из более позднего источника целиком заменяет список
  из более ранних.
- `Program.cs:94-105`: плагины находятся, копируются и загружаются до сборки хоста, то есть до
  первого `tools/list`.
- В `Plugins/` нет проверки подписи или strong name. Плагин получает весь `IServiceCollection`
  хоста (`Plugins/RoslynMcpPluginContext.cs:27`).
- Решения O-01 и O-02 темы [user-data-directory](../user-data-directory/README.md#решения-владельца)
  (2026-10-29) задали порядок источников конфига и плагинов. Доверие к рабочему каталогу в них
  не рассматривалось, ссылки на эту находку там нет.

**Требование.** `docs/ARCHITECTURE.md:163` (инвариант 12): сервер доверяет клиенту и
OS-идентичности. Доверие к *содержимому открываемого репозитория* не сформулировано.

**Последствие.** Клонированный чужой репозиторий с `RoslynMcp.jsonc` исполняет код в процессе
MCP-хоста при старте — до любого вызова tool и до решения агента загрузить workspace. Такой конфиг
ещё и отменяет список плагинов, который пользователь задал в своём каталоге. Оговорка:
`load_workspace` недоверенного решения и так исполняет его MSBuild-логику; разница в моменте
(старт процесса) и в том, что плагин живёт в хосте всю сессию.

**Предложение.** Решение владельца — [P-01](proposals.md#p-01).

<a id="ar-09"></a>
### AR-09 · P2 · `list_projects` читает csproj строковым разбором

**Закрыто 2026-11-21.** Коммит `abb4f9d`. [Проверка](priority-fixes.md#проверка)

**Проверено 2026-11-21.** Воспроизведено на `6d88c97`: оба свойства из `Directory.Build.props` показаны как `(unknown)`. [Сверка](priority-fixes.md).

**Проверено 2026-11-15:** подтверждено.

**Доказательство.** `Tools/UtilityTools.cs:1443-1447`: TFM и OutputType берутся из
`File.ReadAllTextAsync(project.FilePath)` через `ExtractSimpleCsprojValue`
(`:1716`, поиск по `IndexOf(openTag)`).

**Требование.** `docs/ARCHITECTURE.md:40`, `:175-176`: Tools — протокольные адаптеры,
поведение — в Services; данные проекта уже есть в загруженном Roslyn `Project`.

**Последствие.** Значения из `Directory.Build.props`, импортов и условий не видны:
выводится `(unknown)` или значение без учёта условия — при том что `load_workspace` в том
же процессе показывает тип проекта по результату MSBuild-оценки.

**Предложение.** Брать OutputType из `CompilationOptions.OutputKind`, TFM — из
загруженного проекта, если доступен; строковый разбор оставить запасным вариантом.
Остальная логика в Tools дефектов не показала — переносить её ради соответствия схеме
не предлагается.

<a id="ar-18"></a>
### AR-18 · P2 · Выбор SDK по `global.json` без `rollForward`

**Закрыто 2026-11-21.** Коммит `abb4f9d`. [Проверка](priority-fixes.md#проверка)

**Проверено 2026-11-21.** Воспроизведено на `6d88c97` при допустимом `latestFeature`, включая `Build failed` при коде выхода 0. [Сверка](priority-fixes.md).

Найдено 2026-10-24 при закрытии [AR-13](#ar-13), вне раундов ревью.

**Проверено 2026-11-15:** подтверждено по исходникам: `rollForward` и `allowPrerelease` в
`Services/Build/` не читаются.

**Доказательство.** `Services/Build/GlobalJsonSdkReader.cs`: `TryGetPinnedSdkVersion` читает
только `sdk.version`; `TryResolveSdkDirectory` (`:78-85`) берёт папку SDK с этим номером, иначе
папки с префиксом из `GetRollForwardPrefix` (`:140`) — «номер + точка», каких у SDK не бывает.
Результат используют `DotNetSdkEnvironment.ApplyPinnedSdk` (`MSBUILD_EXE_PATH`, пути SDK для
дочерних `dotnet`), `MsBuildBootstrapper` (MSBuild для загрузки workspace),
`SdkMismatchDiagnostics` и отчёт о здоровье workspace.

**Последствие.** `global.json` с `"version": "10.0.300", "rollForward": "latestFeature"` на машине
с 10.0.300 и 10.0.402: хост `dotnet` выбирает 10.0.402, сервер подставляет MSBuild из 10.0.300 и
сообщает об ошибке `MCP_MSBUILD_SDK_MISMATCH` («`dotnet --version` reported 10.0.402 but
global.json pins 10.0.300»). Воспроизведено на Windows во втором раунде ревью BuildHost/task-00
([GPT-6](../workspace-load-performance/research-buildhost-fork/evidence/task-00-review/review-gpt-6-round-2.md),
часть B): вызовы `DotNetSdkEnvironment.TryGetPin` и `SdkMismatchDiagnostics.CreateErrors` из
собранного сервера; полный MCP-запрос не выполнялся.

Ложная ошибка возникает, когда указанный номер установлен, а политика выбирает другой SDK:
`latestPatch` при более новом патче той же полосы, `latestFeature`, `latestMinor`, `latestMajor`.
Если указанного номера нет, а политика разрешает другой, сервер закрепление не видит вовсе и грузит
не тот MSBuild, что `dotnet build`. Политика по умолчанию — `patch`
([matching rules](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#matching-rules)):
установленный точный номер она оставляет, поэтому `global.json` без `rollForward` ложной ошибки не
даёт; без этого номера хост берёт последний патч полосы, а сервер закрепление теряет.

**Предложение.** Спрашивать выбор у самого хоста: `dotnet --version` в каталоге решения применяет
все правила `global.json`; результат кэшировать на каталог и путь `global.json` с его временем
изменения. Либо реализовать правила `rollForward` по документации, что дороже и расходится с
хостом на краях. Цена: средняя; нужны тесты на политики `disable`, `patch`, `latestPatch`,
`latestFeature` и отсутствующую версию.

<a id="ar-03"></a>
### AR-03 · P3 · Отгруженное поведение обосновано отложенным черновиком

**Закрыто 2026-11-21.** Коммит `abb4f9d`. [Сверка документации](priority-fixes.md#сверка-документации)

**Проверено 2026-11-21.** В ARCHITECTURE перенесено обоснование действующей политики. Приоритет снижен до P3. [Сверка](priority-fixes.md).

**Проверено 2026-11-15:** подтверждено без изменений.

**Доказательство.**
- `docs/ARCHITECTURE.md:45`: `docs/backlog/workspace-load-cache/` — «forward-looking …
  not shipped behavior».
- `docs/ARCHITECTURE.md:79`: текущая политика «live semantic reads trust the watcher …»
  и отвергнутые альтернативы даны ссылкой на
  `backlog/workspace-load-cache/epoch-3-live-consistency/spec.md`. У spec статус «proposed»,
  задачи эпохи — planned, сама программа отложена (`docs/README.md:35-39`).

**Требование.** `docs/README.md:54`: после ship факты копируются в ARCHITECTURE или README.

**Последствие.** Решение, по которому работает production watcher, существует только в
документе, который индекс велит не считать нормой. При архивировании backlog обоснование
потеряется. После 1.5.23 политика watcher'а менялась ещё раз
([disk-watcher-inotify](../disk-watcher-inotify/README.md)), и её текущее описание уже собрано из
двух тем.

**Предложение.** Перенести в ARCHITECTURE (или короткий contract-файл) выбранную
политику и список отвергнутых альтернатив; ссылку на backlog оставить как историю.
Цена: низкая.

<a id="ar-04"></a>
### AR-04 · P1 · Расхождение статусов workspace-state-cache

**Закрыто 2026-11-21.** Коммит `abb4f9d`. [Сверка документации](priority-fixes.md#сверка-документации)

**Проверено 2026-11-21.** Статусы индекса и backlog уточнены. S-002 принят как ограниченный тестовый эксперимент, E1/task-00 заблокирован. [Сверка](priority-fixes.md).

**Проверено 2026-11-15:** подтверждено, все перечисленные места не менялись.

**Доказательство.**
- `docs/README.md:19`: «S-002 not-run». В README темы, README эпох E0/E1 и в
  `task-11-…-hi.md` — accepted (bounded test-only partial experiment, 2026-10-09).
  Коммит `fc72dc8` обновил всё, кроме индекса.
- `docs/backlog/workspace-load-cache/README.md:9`, `docs/backlog/README.md:14`:
  state-cache — «ready-for-review; реализация не начата».
- `docs/workspace-state-cache/validation.md:3,10-12`: «Обновлено 2026-10-08 … task-00
  accepted, task-08 accepted, остальные planned … S-002 not-run».
- `docs/workspace-state-cache/benchmark.md:3`: «Числа ускорения ещё не получены» —
  после принятого task-07 benchmark.
- Дата статуса спеки: 2026-10-09 в индексе, 2026-10-07 в README темы (`:3`).
- В индексе не отражено, что E1/task-00 blocked
  (`docs/workspace-state-cache/epoch-1-disk-cache-mvp/README.md:4,12`).

**Требование.** `AGENTS.md`, раздел «Task status bookkeeping»: task, index, report и packet
должны быть согласованы.

**Последствие.** Агент, читающий индекс (как велит `docs/README.md:3`), получает неверную
картину; при возобновлении экспериментов легко перезапустить уже принятую работу.

**Предложение.** Синхронизировать перечисленные места. Структурная причина — AR-16.
Цена: низкая.

<a id="ar-05"></a>
### AR-05 · P2 · Экспериментальный StateCache в production-сборке

**Закрыто 2026-11-21.** Коммит `38e5481`: код удалён по варианту 3 [P-02](proposals.md#p-02), тема заморожена, итоги — в [workspace-state-cache](../workspace-state-cache/README.md).

**Проверено 2026-11-15:** подтверждено по исходникам; живой `find_usages` не повторялся.

**Контекст (со слов владельца).** Код появился в ходе экспериментов с кэшированием
MSBuild/solution; эксперименты упёрлись в тупик, но продолжаются.

**Доказательство.**
- `Services/Workspace/StateCache/` — 2918 строк, компилируется в `RoslynMcpServer.dll`.
- Текстовый поиск `StateCache`, `WorkspaceSnapshotCodec`, `IsolatedWorkspaceHydrator` и
  `OrdinaryWorkspaceCapture` вне этой папки в `Program.cs`, `Hosting/`, `Tools/`, `Services/`,
  `Plugins/`, `Config/`, `Diagnostics/` ничего не находит. Ссылки есть только в
  `RoslynMcpServer.Tests/WorkspaceStateCache/`.
- `docs/ARCHITECTURE.md:67` верно говорит «no on-disk evaluation cache yet», но о
  существовании экспериментального кода не сообщает.
- В `RoslynMcpServer.Tests/SourceStructure/` нет правила про `StateCache`.

**Требование.** `docs/README.md:7` (ARCHITECTURE описывает систему как она есть),
`AGENTS.md` (приёмка изолированного эксперимента не означает production readiness).

**Последствие.** Читатель кода не отличает эксперимент от рабочего пути. Ничто не мешает
случайно вызвать StateCache из production: тесты проверяют изоляцию гидратора
(`IsolatedWorkspaceHydratorTests.cs`), но не отсутствие production-вызовов.

**Предложение.** Решение владельца о размещении — [P-02](proposals.md#p-02). Независимо
от него: одна строка в ARCHITECTURE и запись тупика в самой теме
`docs/workspace-state-cache/` (что пробовали, где упёрлись, что продолжается).
