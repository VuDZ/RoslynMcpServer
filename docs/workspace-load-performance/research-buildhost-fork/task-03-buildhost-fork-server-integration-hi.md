# BuildHost/task-03 — Форк BuildHost с A + B в сервере

- Дата: 2026-10-26.
- Статус: **in progress, 2026-10-26** — постановка по решению владельца; интеграция сделана, Linux-проверки сборки,
  publish, pack и загрузки сервером пройдены; полный main-набор и `AnalyzerLifecycle` — pending, их прогоняет
  владелец (здесь — OOM машины) ([отчёт](evidence/task-03-server-integration.md)).
- Сложность: hi; риск — production загрузка любым решением идёт через форк, а не через host из пакета.
- Depends on: [task-02](task-02-buildhost-optimization-spike-hi.md) (выполнен, итог go для A + B).

## Решение владельца (2026-10-26)

- Интегрировать A + B в сервер. Это снимает для форка границу README «изолированный форк не изменяет production
  package references/defaults»: форк становится production BuildHost сервера.
- A + B включены по умолчанию. Выключатель — настройка; без оптимизаций форк по task-00 identical host из пакета.
- Число рабочих потоков B — настройка, по умолчанию 4, как в замерах task-02. Подбор значения — после всех
  оптимизаций, не в этой задаче.

## Что меняется

1. **Сборка.** Сервер собирает `forks/roslyn-buildhost/build/BuildHost` и кладёт его вывод в `BuildHost-netcore`
   вместо файлов из пакета `Microsoft.CodeAnalysis.Workspaces.MSBuild`. Подмена — на уровне элементов `Content`,
   поэтому build, publish и pack dotnet tool получают одно и то же; тесты получают форк через `ProjectReference` на
   сервер. `BuildHost-net472` остаётся из пакета. Сборка падает, если версия пакета не совпадает с ревизией форка
   (`forks/roslyn-buildhost/UPSTREAM.md`): клиент из пакета и host из форка должны говорить на одном протоколе.
2. **Включение.** Переключатели A и B идут host глобальными свойствами workspace, как путь к решению:
   `RoslynMcpReferencesFirst` (1 или 0) и `RoslynMcpEvaluateAhead` (число потоков, 0 — выкл.). Сервер передаёт оба
   всегда; форк забирает их до любой evaluation, и они побеждают его переменные окружения
   `ROSLYN_BUILDHOST_REFERENCES_FIRST` и `ROSLYN_BUILDHOST_EVALUATE_AHEAD`, которые остаются для спайков. Первая
   версия ставила эти переменные в окружении процесса сервера (клиент 5.9.0 запускает BuildHost с ним), но окружение
   общее для всех workspace процесса: тесты, которые сами создают `MSBuildWorkspace` и изучают binlog host из
   пакета, получали A + B от параллельных загрузок `SolutionManager` (`RawImportEvidenceTests`, см. отчёт).
3. **Настройки** (RoslynMcp.jsonc, переменная окружения побеждает, как у `search-prewarm`):
   - `buildhost-optimizations` / `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS` — `true` по умолчанию; `false` или `0`
     выключает A и B;
   - `buildhost-evaluate-ahead-workers` / `ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS` — 4 по умолчанию, 0
     выключает только B.
   `get_mcp_server_info` показывает итоговые значения.
4. **Решение для B.** `SolutionManager` при открытии `.sln`, `.slnx` или `.slnf` добавляет к глобальным свойствам
   `MSBuildWorkspace` свойство `RoslynMcpSolutionPath` с полным путём. Эти три свойства — только в вызов
   `MSBuildWorkspace.Create`:
   запрошенные свойства в снимке происхождения анализаторов, ключах кэша загрузки и отчётах остаются прежними.
   Host форка забирает свойство до любой evaluation; при открытии проекта, а не решения, свойство не передаётся.

## Риски и проверки

- **Binlog происхождения анализаторов.** Сервер открывает workspace с `BinaryLogger` и связывает элементы
  анализаторов с проектами по контекстам событий сборки (`AnalyzerProvenanceCaptureService`). В экспериментах
  task-02 binlog не писался. С A ссылки собираются отдельными сборками раньше проекта, и вызовы целей ссылок из
  сборки проекта попадают в готовые результаты; с B evaluation идут на рабочих потоках. Связывание может
  измениться: проверяется набором `AnalyzerLifecycle` и тестами происхождения с включёнными A + B, а при
  расхождении — сравнением снимков происхождения с оптимизациями и без них на одном решении.
- **Ответ загрузки.** Корректность A + B доказана task-02 на трёх решениях; здесь — main-набор, `AnalyzerLifecycle`
  и `SourceStructure` на сервере с форком, оптимизации включены по умолчанию.
- **Пакет и релиз.** Проверить состав `BuildHost-netcore` в выводе build, publish (`win-x64`) и в `.nupkg` dotnet
  tool: файлы форка (`InformationalVersion` с `fork`), без файлов host из пакета. Лицензия Roslyn — MIT; уведомление
  о форке добавляется к уведомлениям сервера.
- **Откат.** Выключатель настроек возвращает поведение форка без оптимизаций. Возврат к host из пакета — только
  новой сборкой сервера без подмены; отдельный переключатель host в эту задачу не входит.

## Вне задачи

Подбор числа потоков и окна записей решения; извлечение `ProjectFileInfo` и повторная evaluation при смене TFM
(следующие кандидаты task-02); перенос в `main` — по правилу squash и отдельному решению о релизе.

## Приёмка

Main-набор, `AnalyzerLifecycle` (`ROSLYN_MCP_ANALYZER_LIFECYCLE=1`) и `SourceStructure` пройдены; состав
`BuildHost-netcore` в build, publish и pack проверен; загрузка сервером Roslyn.slnx или офисного решения на Windows
владельцем — без регрессии ответа. Отчёт — `evidence/task-03-server-integration.md`; статусы здесь и в README.
