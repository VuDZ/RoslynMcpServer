# E1/task-00.4 — final review и validation

Дата: **2026-10-09**, Europe/Moscow. Статус: **accepted, round 2 — bounded
ordinary analysis/profiling с honest no-go**. Принят [исследовательский отчёт](task-00.4-ordinary-alternative.md):
конкретный critical-path hotspot для маленькой production оптимизации не доказан.
Предложен один optional bounded follow-up — измерить provenance replay.
Production оптимизация не реализована и не принята; выбор ordinary маршрута и
отсрочка disk restore не приняты. Task-00 остаётся blocked, E1/E2/E3 не приняты.

## Независимое ревью

Исполнитель: Codex subagent `/root/ordinary_analysis`; рецензенты:
`/root/ordinary_review_1` и `/root/ordinary_review_2`, отдельные сессии.
Все унаследовали модель сессии; точные runtime model ID/reasoning инструментами
**not confirmed**. Рекомендованные GPT-6.1 Sol / medium не подставлены как фактические.

[Round 1](task-00.4-review-round-1.md): **ACCEPT**, 0 must-fix.
После наблюдения reviewer о membership warning координатор вернул тому же
исполнителю одно документальное уточнение: scoped hashes не доказывают stable
memberships/generated/excluded paths, путь/причина warning неизвестны, full graph
и E0 parity не заявлены. Новых samples/code изменений нет.
[Fresh round 2](task-00.4-review-round-2.md): **ACCEPT**, уточнение подтверждено.
Итого **2 review rounds / 1 clarification после review / 0 must-fix fixes /
0 открытых must-fix**. Уточнение учитывается отдельно от исправлений must-fix.

Final reviewed report SHA256:
`11388DFFB445E7D6AF30C68624BBFA2320E97A3D6A795D68A7A8A5071E1CBE3D`.
После финального review report/raw не менялись.

## Final validation координатора

Выполнены Roslyn MCP runners на текущем production/test tree:

- `run_dotnet_build`, RoslynMcpServer.sln, Release, noIncremental=true:
  **Build succeeded**. Existing warnings: CS8603 (два места в
  NuGetFallbackAssemblyResolver), CS8601 (HostSession), xUnit1031
  (WpfTemporaryProjectCallbackTests).
- `run_test_by_filter`, RoslynMcpServer.Tests.csproj, Release,
  `Category!=AnalyzerLifecycle`, noBuild=true, noRestore=true, timeout 1200 s:
  **1363 total / 1363 passed / 0 failed**. Total=Passed; skips по parsed summary
  отсутствуют. Полный SourceStructure входит в main.
- AnalyzerLifecycle **not-run / not required**: production analyzer/shadow-copy/
  load/prepare/publication, lifecycle host и build/test configuration не менялись.
  Structural limit inventory сохранён.

[Неизменённые ответы MCP](task-00.4/validation-tools.json) сохранены.
MCP runners не возвращали TRX; отдельный Assert-NoSkippedTests не запускался.
Main/build относятся к конечному неизменённому C#/tests tree. После них менялись
только diagnostic outputs и документы; повторные suites без C#/test/config
изменений не требовались. Profiling не выполнялся одновременно с build/main.

Новый measurement tooling — [diagnostic driver](task-00.4/profile-command.txt) и
[analysis command](task-00.4/analysis-command.txt); existing ordinary runner не
менялся. Final profiler/rundown и topN parser завершились успешно.
[Final attempt](task-00.4/attempt-01.json): PID 79380, expected SamplePlugin
declaration получен, n=1 instrumented diagnostic; request-only useful 2517.0 ms.
Attach wait раскрыт отдельно, sampling overhead неизвестен, membership warning
не расследован. Это не median, не E0 parity, не эффект оптимизации.
Все setup/attach/sandbox/rundown failures сохранены в report/raw и не скрыты.
Предыдущие E0/S-002 measurements не переписаны и не выданы за новые runs.

## Consistency и сохранность

[Validator](task-00.4/validate.py) проверяет final local links/anchors, JSON,
task metadata/acceptance links, task requirements body, inventory/dependencies,
protected contracts и pre-existing dirty SHA256, `git diff --check` и HEAD.
[Final checks](task-00.4/checks.json): **passed / 0 errors**.
Граф **38 tasks / 59 edges** не изменён и не содержит циклов.
Исходные [dirty/contracts hashes](task-00.4/baseline.json) и
[dependencies](task-00.4/dependencies-before.json) сохранены.
Task-00 manifest, spec, H-001/H-002 и benchmark contract не менялись.

Preliminary validator сначала имел syntax error, исправленный координатором;
затем показывал только ожидаемые pending acceptance metadata. Ни один из этих
прогонов не засчитан как final pass. Python environment печатает
`Failed to find real location of C:\Soft\Python313\python.exe`, но исполняет
final script с exit 0 и parsed checks.json; это не свидетельство C# validation.

Task header, E1 README, series summary, docs index, execution и current update
отчёта подготовки синхронизированы на accepted research/no-go scope.
Будущие deferral statuses описаны только условно; downstream planned/blocked
statuses и activation gates не изменены. E0 artifacts и посторонние dirty
C#/tooling файлы сохранены.

HEAD до/после: `fc72dc80d330874ae5160950e972a8c8b4d67874`.
**Коммитов нет.** Версия/defaults/production C# не менялись.

При сохранении исследования 2026-11-21 сырые `.nettrace`, `.etlx` и экспорты
Speedscope оставлены локально. В репозитории доступны [агрегат профиля](task-00.4/profile-analysis.json),
[сводка по inclusive](task-00.4/top-inclusive.txt) и [сводка по exclusive](task-00.4/top-exclusive.txt).
Исторический SHA256 и проверки относятся к отчёту, который рецензировали 2026-10-09,
до обновления ссылок на локальные трассы.
