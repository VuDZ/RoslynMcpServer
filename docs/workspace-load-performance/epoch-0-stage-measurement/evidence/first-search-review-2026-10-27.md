# Ревью production-задач первого поиска (task-10, 12, 13, 15, 16) — 2026-10-27

Решение владельца 2026-10-27: перед приёмкой провести независимое ревью кода, уже перенесённого в `main`
squash-коммитами f071de4 (1.5.8–1.5.13), ec3c63b (1.5.14) и 5ee2cdd (1.5.15).

## Раунд 1

- Версия: ветка `research/workspace-load-performance`, HEAD `83a2a35`, дата 2026-10-27.
- Рецензенты: три новых сессии Claude Opus 5.5 (`claude-opus-5-5`), только чтение. Сессия A — task-10 и
  task-12, сессия B — task-13, сессия C — task-15 и task-16. Идентификаторы сессий не сохранены.
- Каждый рецензент прочитал `docs/code-style.md` и `AGENTS.md` до замечаний по стилю.
- Must-fix нет. Вердикты: task-10, task-13, task-15 и task-16 — **требуются исправления**, task-12 — **принято
  с мелкими замечаниями**.

### Находки и разбор

Исправления внёс координатор (Claude Opus 5.5) в той же ветке. Счётчик исправлений заполняется после
перепроверки раундом 2.

| ID | Важность | Суть | Исход |
|---|---|---|---|
| T10-R1-01 | should-fix | `Get-RedactedText` не узнавал путь в форме JSON сервера: System.Text.Json пишет не-ASCII и `& ' + < >` как `\uXXXX`; путь с кириллицей или `R&D` проходил в `runs.json` и `summary.md` | исправлено: варианты в форме JSON; регрессионный кейс в `Test-MeasureWorkspaceLoad.ps1`, мутационная проверка — кейс падает без исправления |
| T10-R1-02 | minor | логи dotnet-trace (`.log`, `.err`) с полными путями не обезличивались и не сканировались | исправлено: оба файла переписываются через `Get-RedactedText`, сканер смотрит `.log` и `.err` |
| T10-R1-03 | minor | `ToJson` сериализовал под блокировкой, которую ждёт обработчик каждого исключения процесса | исправлено: под блокировкой только копия |
| T10-R1-04 | minor | тест чужого потока проверял только `attributed == 0` | исправлено: проверяется группа `IOException` без проекта и `unattributed ≥ 1`. Тест пути failure не добавлен — отложено: отписку держит `using var` |
| T10 заметки | — | префиксная замена без границы сегмента; цена обработчика мерилась микробенчмарком; prewarm (task-16) читает документы до поиска, и его исключения сводка не видит | без изменений; ограничения сводки при включённом prewarm записаны здесь |
| T12-R1-01 | minor | паузы GC общие для процесса, но в выводе это не сказано; prewarm добавляет свои сборки | исправлено: «process-wide» в строке поиска и в колонке `comparison.md`, пояснение под таблицей |
| T12-R1-02 | minor | счётчики сборок накопительные (gen2 входит в gen0/gen1) | исправлено: подпись «cumulative» |
| T12-R1-03 | minor | после task-16 офисная команда по умолчанию сравнивает только prewarm; спецификация говорит о сравнении GC | исправлено частично: пометка в спецификации и в `comparison.md`. Пара server/workstation в self-check — отложено |
| T12-R1-04 | minor, опц. | `DOTNET_gcServer=0` проверен только вручную | отложено: утверждение верно по коду CoreCLR |
| T13-R1-01 | should-fix | Roslyn передаёт новый loader всем связанным документам, а связывает пути без учёта регистра. На Linux существующий `Foo.cs` становился пустым из-за отсутствующего `foo.cs` | исправлено: путь, у которого есть существующий двойник с другим регистром, loader не получает. Тест для файловой системы с учётом регистра, мутационная проверка — падает без исправления |
| T13-R1-02 | minor | нет тестов additional и analyzer config документов без файла | исправлено: MSBuild-тест с `AdditionalFiles` и `EditorConfigFiles`. Отсутствующий `GlobalAnalyzerConfigFiles` SDK не передаёт в workspace, поэтому взят `EditorConfigFiles` |
| T13-R1-03 | minor | исключение reflection роняло всю загрузку | исправлено: `installed: false`, предупреждение в логе, загрузка продолжается |
| T13-R1-04 | minor | `installed: true` при нуле документов скрывал недоступность механизма | исправлено: поле `available`, `installed` = null, когда ставить нечего; скрипт замера показывает оба |
| T13-R1-05 | minor | `File.Exists` ложен и при отказе в доступе | принятое отличие, записано в remarks `MissingSourceFiles.Find` |
| T13-R1-06 | minor, стиль | private static поля в `PascalCase` | исправлено в `MissingSourceFiles`; тестовое поле `BomFree` в существующем файле не тронуто |
| T13-R1-07 | minor, спорно | `MissingSourceDocument` в общем `Services/Models` | без изменений: рецензент сам счёл вариант спорным |
| T15-R1-01 | should-fix | тесты удаляли общий кеш индексов `dotnet-<hash>`: на Linux это кеш всех framework-dependent серверов и параллельных прогонов | исправлено: на Linux и macOS кеш процесса тестов перенаправлен во временную папку процесса (`XDG_DATA_HOME`/`HOME` на время статического конструктора Roslyn) и удаляется целиком; на Windows папка `testhost.exe` прежняя. Проверено: папка `dotnet-…` после прогона тестов индексов не изменилась |
| T15-R1-02 | minor | PRIVACY.md не описывал общую папку `dotnet` и путь macOS | исправлено в PRIVACY.md и в ограничениях task-15 |
| T15-R1-03 | minor, не подтв. | тест инвалидации не проверяет, что индекс записан | отложено |
| T15-R1-04 | minor | `Get-IndexCacheState` не видит запуск через `dotnet` | отложено: офисный запуск идёт через exe |
| T15-R1-05 | minor | release smoke не проверяет хранилище в опубликованном артефакте | отложено: к задаче релизов |
| T15 стиль | minor | порядок `internal` перед `public` в `RoslynIndexCache` | исправлено |
| T16-R1-01 | should-fix | отмена прогрева не ждала его; запись прошлой загрузки появлялась после clear или внутри следующей загрузки | исправлено: clear и новая загрузка ждут отменённый прогрев до 10 с, при превышении — предупреждение |
| T16-R1-02 | should-fix | тест отмены проходил при любом коде; нет тестов reload, cache hit, opt-in | исправлено: seam `BeforeSearchPrewarmProjectForTests` держит прогрев; тесты clear и reload (запись `cancelled` до clear/следующей загрузки), cache hit, opt-in и поиск при удерживаемом прогреве. Мутационная проверка: без ожидания тест clear падает |
| T16-R1-03 | minor | приоритет переменной над jsonc не тестировался | исправлено: перегрузка `IsEnabled(fileSetting, environmentValue)` и тест |
| T16-R1-04 | minor | `get_mcp_server_info` не показывал действующее значение | исправлено: строка «Search prewarm in effect» с источником |
| T16-R1-05 | minor | «поиск не повторяет работу прогрева» неточно | исправлено в remarks `SearchIndexPrewarm` и в спецификации |
| T16-R1-06 | minor | прогрев включён в LifecycleTestHost и части тестов | исправлено для LifecycleTestHost; прочие тесты с прямым `new SolutionManager` пишут кеш в папку процесса (T15-R1-01) |
| T16-R1-07 | minor | любое `OperationCanceledException` считалось отменой | исправлено: `when (cancellationToken.IsCancellationRequested)`, тест |

Новый предел structural-анализа: вызов параметра `beforeProject` в `SearchIndexPrewarm.RunAsync` достижим из
загрузки. Он добавлен в точный инвентарь
`Production_reachability_records_only_the_known_delegate_limits` по имени, виду и файлу.

### Validation исправлений

- Release build решения: без новых предупреждений (3 прежних).
- SourceStructure: 123/0/0.
- Focused: `SearchIndexPrewarm`, `RoslynIndexStorage`, `RoslynMcpFileSettings`, `McpServerInfo`,
  `MissingSourceFile`, `SearchExceptionRecorder` — пройдены.
- PowerShell 7 (Linux): `Test-MeasureWorkspaceLoad.ps1`, `Test-FirstSearchDiagnostics.ps1` — PASS.
  Windows PowerShell 5.1 не проверялся.
- Полный main-набор, Linux, Release (`Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk`):
  **1426/0/0**, 4 мин 7 с, `TMPDIR=~/tmp/testtmp`.
- `AnalyzerLifecycle` (`ROSLYN_MCP_ANALYZER_LIFECYCLE=1`), Linux, Release: **13/0/0**.
- Первый запуск полного набора 2026-10-27 оборвался OOM: tmpfs `/tmp` (в RAM, 1,9 ГБ) был заполнен на 1,6 ГБ
  остатками прошлых прогонов тестов (`/tmp/RoslynMcpServer/plugins` — 1,5 ГБ). Остатки удалены, набор повторён
  с временными файлами в домашнем каталоге. Windows-прогон не выполнялся.

## Раунд 2

Pending.
