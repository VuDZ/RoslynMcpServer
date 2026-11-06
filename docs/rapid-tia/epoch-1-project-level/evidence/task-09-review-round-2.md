# Task-09 — независимое ревью, раунд 2

Дата: **2026-11-06**. Рецензент: **Codex**, новая независимая сессия; точный runtime model ID средой не предоставлен. Результат: **ACCEPT**. **R1-001 закрыто**, новых обязательных замечаний по R-18 нет. Это приёмка review scope; окончательная validation, accepted status задачи/индексов и commit принадлежат координатору.

## Что меняет реализация и что проверено

`estimate_affected_tests(run: true)` последовательно строит и исполняет планы VSTest через общий структурированный исполнитель. Старые `run_*` используют тот же исполнитель и сохраняют свои внешние форматирование, параметры и общий timeout budget. Новый run получает отдельный timeout на build/test, самостоятельные исходы и завершающий `Not covered`.

Проверен весь diff относительно **`2c16c9e`**, включая новые untracked production/test/evidence files task-09, а не только исправление раунда 1. Полностью прочитаны `AGENTS.md` и `docs/code-style.md`; style рассматривался только для новых/изменённых строк. Прочитаны task-09, README эпохи и нормативные R-13/R-16/R-18, разделы run/build/outcomes/output программы. Применён ponytail-review с порогом R-18: возврат только при молчаливой потере тестов или неверном ответе в конкретном реальном сценарии.

Прослежены executor/result/outcome, `TestTools`, runner, formatter, catalog/help/README, их callers и общие `DotNetTestArguments`, `TestBuildRoute`, `TestDllEnsure`, `CliProgressStep`, `BuildOutputReport`, `TestOutputReportOptions`, `VstestOutputParser`, `WindowsParentProcess`, estimator и VSTest planner. Прочитаны все новые execution/integration/cancellation tests и неизменённые legacy expectations. Предполагаемая нагрузка — последовательный запуск планов одного загруженного workspace; независимые параллельные builds не добавлялись в контракт.

Task-10/11 и посторонний untracked `.agents/skills/prepare-github-release/` исключены. Рецензент не менял production/tests, статусы и commit, не запускал build/tests.

## R1-001 — закрыто

Исходный дефект подтверждён по [раунду 1](task-09-review-round-1.md): реальная compiler error приводила к инструкции передать `reportCursor` этому же инструменту; длинный failed StdOut предлагал `includeFullOutput`/`maxOutputChars`. Все три параметра отсутствуют в его минимальной schema.

Исправление проверено независимо по коду:

- `Diagnostics/BuildOutputReport.cs:23,68` сохраняет прежнее attachment-поведение по умолчанию (`attachFullReport = true`). Только TIA formatter передаёт `attachFullReport: false` (`Services/Testing/TestImpactFormatter.cs:109–110`), поэтому build failure больше не создаёт неподдерживаемую cursor-инструкцию. Все прежние callers продолжают использовать default.
- `Diagnostics/TestOutputReportOptions.cs:55` добавляет внутренний opt-out с default false. Только TIA задаёт `SuppressOutputOptionHints = true` (`Services/Testing/TestImpactFormatter.cs:114`). В `Diagnostics/VstestOutputParser.cs:1284` условным стал только текст action-подсказки; truncation-сообщение, head/tail, бюджеты и parsing сохраняются. Для default options итоговая строка прежняя.
- Публичная schema остаётся ровно `includeDetails`, `run`, `timeoutSeconds`; новое продолжение отчёта, параметры и разбор Markdown другого инструмента не вводились.

Regression evidence предметно соответствует дефекту: существующий настоящий build-failure scenario запрещает `reportCursor` (`EstimateAffectedTestsRunTests.cs:78,89`), новый настоящий xUnit/`ITestOutputHelper` case печатает BEGIN/END и 6000 символов, падает и проверяет failed outcome, бюджет 2500, оба края и отсутствие неподдерживаемых параметров (`:96`). Он не заменяет execution assertions проверкой одного текста плана.

Прочитаны [RED — 2/2 expected failed](task-09-review-round-1-red.md) после успешной сборки и [GREEN — 93/93](task-09-review-round-1-green.md). RED захватывает обе неподдерживаемые инструкции; GREEN включает также прежние cursor/output-hint/budget tests, legacy test tools и schema. Исправление не ослабляет прежние ожидания.

## Полный повторный аудит поведения

- Общий исполнитель возвращает данные, а не Markdown. Build failure не запускает test process; успешный build передаёт прежний exit code и remaining budget старым `run_*`. Новый run явно задаёт `noBuild: false`, наследует loaded configuration/platform/buildArgs и использует отдельный timeout для каждой стадии.
- Реальные MSBuild/xUnit tests проверяют совпадение возвращённого фильтра с выполненным набором, допустимый substring extra, отсутствие Other, whole-project без фильтра и пустой план. Stale-DLL case сначала действительно проходит на старой DLL, затем проверяет исполненный маркер нового failing source, Failed:2 и продолжение второго проекта. Build-failure case проверяет compiler diagnostic, отсутствие старых тестовых маркеров и выполнение следующего проекта.
- Failure, build failure и timeout продолжают обработку остальных проектов. Timed-out process получает самостоятельный исход, а не `failed`; сохранённые parser counters доступны в результате. Реальный timeout test проверяет остановку testhost и его дочернего процесса, а не только ответ.
- Настоящий MCP `tools/call` и явный `notifications/cancelled` используют наблюдённый типизированный request ID. Integration test проверяет PID обоих процессов, отсутствие следующего проекта, единственную однострочную запись со всеми `cancelled`/`not run` и рабочий следующий запрос. Доставка отменённого ответа правильно не является обязательной.
- Windows snapshot удерживает process handles, проверяет start time относительно каждого родителя и завершает листья перед root tree kill. Ошибки доступа и concurrent exit не отключают root fallback. Остальные платформы сохраняют прежний tree-kill путь.
- Legacy ветки formatting, no-match, silent failure, metadata, warnings, reportCursor и DLL route сохранены. Три дополнительных cases фиксируют точный прежний OCE-ответ каждого `run_*`, в том числе при неотменённом request token. Catalog проверяет обе поверхности, process/readOnly=false, defaults и ровно три параметра; measured inventory обновлён без роста числа инструментов.
- Warning/state/Not covered прослежены во всех run-ветках, включая ранний ответ. В обычном run состояние и disk-sync notes находятся перед последним блоком ограничений. Новый R1 opt-out не затрагивает пользовательские output streams.

Подтверждённой молчаливой потери тестов или нового неверного ответа в проверенном scope не найдено.

## TDD и границы validation

Прочитаны [первоначальный RED](task-09-red.md), [implementation checks](task-09-implementation-checks.md) и [отчёт исполнителя](../task-09-implementation-report.md). Initial RED: **20 expected failed / 0 passed / 0 skipped** после успешной компиляции пустого контракта. Предварительные compile/fixture failures исключены. Пять поздних guards и последующие fixture corrections раскрыты отдельно, не выдаются за первоначальный RED. Новый R1 case написан до production fix. Итоговый прирост main inventory — **24 cases** относительно 1980, ожидаемо **2004**; два существующих surface cases обновлены под контракт.

На исправленном и замороженном коде координатор сообщил: **Release solution build PASS** (3 известных предупреждения), **focused 220/220 PASS**, **SourceStructure 123/123 PASS**. SourceStructure inventory не ослаблялся. **Полный main CI suite и AnalyzerLifecycle на момент записи ещё выполняются/ожидаются**; их окончательные результаты и evidence записывает координатор. Прежние main2003/2003 и lifecycle13/13 относятся к состоянию до R1 и не подменяют финальные проверки. [Сохранённая validation до исправления](task-09-validation-before-round-2.md) явно это указывает.

## Ограничения без возврата на доработку

- Linux/macOS отдельно не исполнялись. Рецензент использовал код, тесты, captured evidence и результаты координатора; собственных запусков не проводил.
- Остановка дерева остаётся best effort при динамическом spawn во время снимка, access-denied и системных гонках. Нового stress test для этих случаев нет; обычный реальный VSTest host/child scenario покрыт timeout и MCP cancellation.
- MCP test доказывает серверную реакцию на настоящий protocol notification. Автоматическая отправка notification при одном лишь локальном client `CancelAsync` в pipe harness не подтверждена и не заявлена.
- Известный VSTest parser иногда извлекает failure names/counters без Error message. Task-09 не расширяет parsing; failed outcome при этом остаётся виден. Отдельные новые integration cases для отмены во время build, build timeout с двумя проектами и custom loaded configuration/buildArgs отсутствуют; общий путь прослежен и частично проверен unit/legacy tests.
- Предельные размеры, дополнительные adapter/runtime configurations и параллельные workspace/build races заново не исполнялись. По R-18 это ограничения проверки, а не основание нового раунда без конкретной потери тестов или неверного ответа.

**Вердикт: ACCEPT, R1-001 закрыто; открытых MUST-FIX нет.** Финальная приёмка задачи требует ещё завершающихся main/lifecycle и согласованного bookkeeping координатора.

Не выполнялись рецензентом: build/tests, другие ОС, status edits и commit.
Остающийся риск: best-effort termination и прежняя неполнота parser diagnostics; финальные main/lifecycle пока не подтверждены этим отчётом.
