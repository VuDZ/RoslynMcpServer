# BuildHost/task-03 — форк BuildHost с A + B в сервере: отчёт

Дата: **2026-10-26**. Статус: **in progress** — интеграция сделана, Linux-проверки ниже; pending: полный main-набор и
`AnalyzerLifecycle` (здесь не поместились в память машины, прогоняет владелец), загрузка сервером на Windows. Контракт: [task-03](../task-03-buildhost-fork-server-integration-hi.md).

## Что сделано

- **Сборка.** `forks/roslyn-buildhost/build/BuildHostFork.targets`, импорт в `RoslynMcpServer.csproj`:
  - `ProjectReference` на проект форка (`ReferenceOutputAssembly=false`, `OutputItemType`) — restore и сборка форка
    вместе с сервером; командная строка сервера до форка не доходит (`-o`, `-r`, `--self-contained` убраны через
    `GlobalPropertiesToRemove`), `ValidateExecutableReferencesMatchSelfContained=false` (NETSDK1150 не относится к
    отдельному процессу BuildHost);
  - цель перед `AssignTargetPaths` убирает элементы `Content` пакета с `TargetPath` `BuildHost-netcore/…` и кладёт
    вместо них все файлы вывода форка плюс `License.txt` и `THIRD-PARTY-NOTICES.txt` Roslyn (`Always`, чтобы старые
    файлы пакета в выводе не пережили сборку);
  - ошибка сборки, если элементов пакета нет, если версия пакета не 5.9.0 или если форк ничего не собрал;
  - ссылка, цель и сам импорт не действуют при `DesignTimeBuild=true`: иначе `MSBuildWorkspace`, загружая сервер
    (свой репозиторий или плагин-пример), видел бы проекты форка как ссылки сервера.
- **Включение — свойствами workspace.** `BuildHostOptimizations.ForWorkspace` добавляет к глобальным свойствам
  `MSBuildWorkspace.Create` `RoslynMcpReferencesFirst` (1/0), `RoslynMcpEvaluateAhead` (потоки) и, для `.sln`,
  `.slnx`, `.slnf`, `RoslynMcpSolutionPath`. Форк (`InitializeReferencesFirst`, `InitializeEvaluateAhead`) забирает
  их до создания `ProjectCollection`; свойства побеждают переменные окружения форка, которые остаются для спайков.
  Запрошенные свойства для снимка происхождения анализаторов и кэша загрузки не меняются.
- **Настройки.** `buildhost-optimizations` / `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS` (по умолчанию вкл.),
  `buildhost-evaluate-ahead-workers` / `ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS` (по умолчанию 4, 0 — без B);
  переменная побеждает файл. `get_mcp_server_info` показывает ключи и итог. README (оба языка), CHANGELOG,
  `RoslynMcp.jsonc.sample`.

## Первая версия включения и почему её заменили

Первая версия ставила переменные форка `ROSLYN_BUILDHOST_REFERENCES_FIRST` и `ROSLYN_BUILDHOST_EVALUATE_AHEAD` в
окружении процесса сервера перед каждой загрузкой: клиент 5.9.0 запускает BuildHost с окружением процесса. Main-набор
показал цену: окружение общее, и `RawImportEvidenceTests`, который сам создаёт `MSBuildWorkspace` и изучает binlog
host из пакета, получал A от загрузок `SolutionManager` в соседних тестах. С A в binlog загрузки нет ни одного
события evaluation: у upstream их даёт только повторная evaluation ссылки внутри сборки проекта (корневые evaluation
идут в коллекции без логгеров), а A эту evaluation и убирает. Это ожидаемое поведение A, а не ошибка; ошибкой было
включать его для чужих workspace. Свойства workspace действуют только на свой BuildHost.

Из production-кода события evaluation binlog читает только отчёт таймингов `DesignTimeBuildTimings`: с A в нём
меньше строк evaluation. Связывание анализаторов (`AnalyzerProvenanceCaptureService`) опирается на `ProjectStarted`
и элементы задач — проверяется набором `AnalyzerLifecycle`.

## Linux, 2 vCPU, 3,8 ГБ

| проверка | результат |
|---|---|
| build, `build -o`, publish `linux-x64` self-contained и framework-dependent, pack dotnet tool (все 7 пакетов) | в `BuildHost-netcore` — файлы форка (BuildHost.dll 135 680 байт против 112 464 у пакета, `5.9.0-fork+…`), лицензия Roslyn; `deps.json` сервера и корневой `Contracts.dll` — как без форка |
| загрузка `RoslynMcpServer.sln` опубликованным сервером через MCP (`load_workspace`), трасса BuildHost | 5 проектов; по умолчанию — `evaluate-ahead-start` с 4 потоками, решение взято (5 записей), 4 evaluation наперёд, 2 сборки ссылок заранее; с `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS=0` — ни одного span A и B |
| тесты настроек, `ForWorkspace`, форк в выводе тестов, бывшие падения (`RealControlCapability`, `OrdinaryWorkspaceCapture`, `SnapshotSchema`, `CrossProcessRoundtrip`, `RawImportEvidence`) | 57 passed |

## Main-набор: не прогнан на этой машине

- Первый запуск (первая версия, переменные окружения): 1 405 passed, 5 failed — 4 теста на число проектов
  (проекты форка в workspace, исправлено условием `DesignTimeBuild`) и `RawImportEvidenceTests` (A у чужого
  workspace, исправлено переходом на свойства). Все пять проходят после исправлений (57 passed выше).
- Полный набор после исправлений три раза убит системным OOM-killer: на машине 3,8 ГБ, процессы вне тестов держат
  около 2,2 ГБ, swap заполнен. С `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS=0` — тоже OOM (пик 1,7 ГБ против 1,5 ГБ с
  оптимизациями), так что причина — память машины, а не A + B. Результата полного набора нет.
- Решение владельца (2026-10-26): закоммитить, полный main-набор и `AnalyzerLifecycle` владелец прогоняет на
  домашнем компьютере. Pending.
