# Coverage — ревью результатов task-00, 2026-10-28

## Прочитано полностью

- Task-00, README эпохи и темы, `AGENTS.md`, `docs/code-style.md`.
- `evidence/task-00-state-inventory.md` (S01–S33, 54 tools, N01–N08).
- `evidence/task-00-impact-2026-10-28.md` (S34–S42, поправки, validation).

## Независимый поиск полноты

Поиск по production-коду (без тестов, spikes, samples, `forks/`) изменений
глобального состояния процесса: `Environment.SetEnvironmentVariable`,
`AppContext.SetSwitch`, `Environment.CurrentDirectory =`,
`Directory.SetCurrentDirectory`, `Console.Set*`, `ThreadPool.SetMinThreads`,
события `AssemblyLoadContext.Default` и `AppDomain.CurrentDomain`, `GCSettings`,
`CultureInfo.Default*`. Найдено: `Program.cs:135` (S04),
`SearchExceptionRecorder` FirstChanceException (S40),
`InProcessAnalyzerAssemblyLoader` AssemblyResolve (S16), `GCSettings.IsServerGC`
только для чтения (S38). Пропусков в инвентаре не найдено.

## Проверено в коде

См. «Проверенные утверждения» в [result.md](result.md). Для F-02 написаны тесты и
выполнен red/green: без вызова сброса оба теста падают (`Assert.Null` после clear,
`Assert.NotSame` после свежего load), с ним проходят.

## Validation исправлений

Фикс меняет production-код, поэтому проверки по `AGENTS.md` (Linux):
Release build решения, основной набор `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk&Category!=RequiresWindows`
(включает SourceStructure) и AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`,
поскольку затронуты reset и путь свежего load. Результаты — в
[evidence/2026-10-28-task-00-review-fixes.md](../evidence/2026-10-28-task-00-review-fixes.md).

## Не проверялось

- Утверждения S01–S42 сверх перечисленных: проверены выборочно, не построчно.
- Замеры памяти: эффект F-02 в байтах не измерялся; это работа task-05/task-02.
- Windows и macOS: тесты запускались только на Linux.
