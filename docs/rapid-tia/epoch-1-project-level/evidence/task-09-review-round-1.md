# Task-09 — независимое ревью, раунд 1

Дата: **2026-11-06**. Рецензент: **Codex**; точный runtime model ID средой не предоставлен. Результат: **MUST-FIX**, одно замечание по контракту ответа в реальном сценарии. Это не приёмка задачи или эпохи.

## Scope и основания

Проверены task-09, нормативные R-13/R-16/R-18 и разделы run/build/outcomes/output программы, diff относительно `2c16c9e` и новые implementation/test files task-09. Полностью прочитаны `AGENTS.md` и `docs/code-style.md`; style рассматривался только для новых/изменённых строк. Применены epoch-subagent-loop и ponytail-review с ограничением возврата R-18: только потеря тестов или неверный ответ в конкретном сценарии. Task-10/11, прежние ограничения планировщика и посторонний untracked `.agents/skills/prepare-github-release/` не входят в scope.

Прочитаны executor/result/outcome, изменённые `TestTools`, runner, formatter, tool catalog/help/README; прослежены callers и общие `DotNetTestArguments`, `TestBuildRoute`, `CliProgressStep`, `VstestOutputParser`, `BuildOutputReport`, `DiagnosticReportAttachment`/store, `WindowsParentProcess`, estimator и VSTest planner. Предполагаемая нагрузка — последовательный запуск выбранных проектов одного загруженного workspace; параллельные независимые builds не добавлялись в контракт.

## Обязательное исправление

### R1-001 — ответ предлагает параметры, которых у инструмента нет

**Место:** `Services/Testing/TestImpactFormatter.cs:109` и `:114`.

**Реальный сценарий:** исходник выбранного тестового проекта содержит compiler error, затем вызывается `estimate_affected_tests(run: true)`. Это ровно сценарий существующего `Build_failure_does_not_start_old_tests_and_the_next_project_runs`: `MissingType.Run()` даёт непустой build log и `build failed`.

`AppendRun` вызывает `BuildOutputReport.Format`. Для failed build этот formatter безусловно вызывает `DiagnosticReportAttachment.AttachToResponse(..., shouldStore: true)`; store сохраняет любой непустой лог. В ответ добавляется строка `Full report: pass reportCursor=... to this same tool.` Однако метод `EstimateAffectedTests` и schema имеют только `includeDetails`, `run`, `timeoutSeconds`: `reportCursor` этим инструментом не принимается и не обрабатывается. Следование инструкции не возвращает обещанный отчёт без нового процесса. Это неверная actionable-инструкция уже при обычной ошибке сборки, а не гипотетический предел selection/performance.

Та же причина есть у длинного Standard Output/Error упавшего теста: вызов `VstestOutputParser.BuildMarkdownReport` с default budgets добавляет предложение `Pass includeFullOutput=true or raise maxOutputChars`, но оба параметра тоже отсутствуют у `estimate_affected_tests`. Прямой отчёт о failed outcome и счётчики корректны; продолжение диагностики описано некорректно.

**Минимальный результат исправления:** сохранить минимальную публичную schema задачи и бюджеты, но выдавать только выполнимые инструкции — например, направить за продолжением к существующему подходящему `run_*` инструменту либо убрать неподдерживаемые подсказки. Добавить regression-проверку именно текста follow-up для failed build и truncated failure output. Изменять прежние ответы `run_*` для устранения этого дефекта не требуется.

## Аудит сценариев тестирования

- **Общий структурированный исполнитель:** 11 cases `DotNetTestExecutorTests` проверяют passed/failed/build failed, build/test timeout, build/test cancellation через OCE, выполненный процесс с no matches, отдельный и прежний совокупный timeout budgets, pre-cancelled/no-start. Timeout case сохраняет разобранные счётчики. Unit tests не запускают реальные дочерние процессы; реальный путь проверяется ниже.
- **Фильтр против run:** реальный MSBuild workspace с двумя xUnit/VSTest проектами, MCP editing path и baseline. Возвращённый фильтр исполняется вручную через прежний `RunTestByFilter`; маркеры доказывают `First1`, `First2`, допустимый substring extra `FirstExtra`, отсутствие `Other`. Затем `run` проверяет тот же инвентарь и отсутствие второго проекта. Это проверка выполненных тестов, а не только текста плана.
- **Свежесть DLL и продолжение после failed:** до `run` ручной исторический no-build действительно проходит на старом инвентаре. После `run` новый failing body оставляет ровно два `NewSource`, ответ имеет `Failed: 2`, второй проект выполняется. Старую успешную DLL нельзя принять за проверку новой правки.
- **Build failure:** непустая compiler diagnostic `CS0103`, отсутствие старых тестовых маркеров первого проекта, выполнение второго проекта. Этот test проверяет выполнение, но не обнаруживает неверную follow-up строку R1-001.
- **Whole project / empty plan / ранние ответы:** реальное расширение на весь проект даёт четыре маркера без постороннего проекта; пустой план не запускает тесты. Два поздних cases проверяют no-workspace/pre-cancelled ответы, предупреждение, state и завершающий `Not covered`.
- **Реальный timeout:** hanging VSTest testhost создаёт дочерний `ping`/`sleep`; timeout возвращает `timed out`, не `Outcome: failed`, второй проект выполняется, оба записанных PID остановлены. Build timeout отдельно проверен unit case и прежним CLI hang test; отдельного нового двухпроектного integration case для build timeout нет.
- **Настоящий MCP cancellation:** client/server transport из текущей production assembly, входящий настоящий `tools/call`, наблюдённый типизированный request ID и `notifications/cancelled` через клиент. Проверяются остановка host/child, отсутствие второго проекта, единичный server log со всеми `cancelled`/`not run`, отсутствие переноса строки и рабочий следующий запрос. Это не прямой вызов handler. Доставка ответа правильно не объявляется обязательной.
- **Сохранность run_*:** прежние expectations `TestToolsDllBuildTests`/`TestToolsRunTestByFilterTests` не правились; три новых поздних cases дополнительно проверяют точный прежний cancellation response при OCE без signalled request token. Diff сохраняет существующие formatting branches, build warnings, no-match/silent-failure поведение, metadata и reportCursor у прежних инструментов. DLL route остаётся у `TestDllEnsure`.
- **Поверхность:** оба профиля проверяют core/process/readOnly=false, ровно три параметра и defaults; все прежние measured byte literals обновлены без изменения числа инструментов. Именно строгая проверка schema подтверждает отсутствие предлагаемых R1-001 параметров. Отдельного integration assert наследования custom configuration/platform/buildArgs новым run нет; фактический fixture использует Release/Any CPU, код использует прежние argument builders и loaded values.
- **Output:** warning/state/final Not covered проверяются integration cases и прослежены в formatter. Default failed-test и build-diagnostic budgets переиспользованы. Нового assert для truncated failure output/follow-up нет; этот пробел материализуется в R1-001.

Итого по новым execution tests: 11 executor + 8 run-response/integration + 1 MCP cancellation + 3 legacy cancellation cases = **23 новых cases**, плюс **2 обновлённых** surface cases. Это согласуется с inventory исполнителя.

## TDD и validation evidence

Прочитаны `task-09-red.md`, `task-09-implementation-checks.md` и implementation report. RED фиксирует успешную компиляцию и **20 expected failed / 0 passed / 0 skipped**, пустой executor contract и прежний estimate-only handler. В captured excerpt показаны итог и первые пять failures; compile/fixture errors предварительной настройки явно исключены из RED. Поздние пять regression cases (три cancellation-output и два ранних ответа) честно выделены как написанные после реализации. Коррекции manual replay noBuild, freshness marker вместо необязательной parser Error строки и no-match outcome раскрыты отдельно.

По сообщению координатора на проверенном текущем коде: **Release solution build PASS; focused 201/201; SourceStructure 123/123; complete main 2003/2003; opted-in AnalyzerLifecycle 13/13**. Рецензент не запускал build/tests повторно, не менял code/tests и не создавал commit. Эти результаты подтверждают текущую validation, но не отменяют воспроизводимый дефект ответа R1-001; validation после исправления и статусы принадлежат координатору.

## Ограничения без возврата на доработку

- Linux/macOS этим review не исполнялись. Новый snapshot потомков применяется только на Windows; другие платформы используют прежний tree-kill путь.
- Windows snapshot прослежен по коду: parent IDs читаются через process handles; start time проверяется относительно каждого родителя; handles удерживаются до kill/dispose; обход убивает листья раньше корня, затем вызывает root tree kill. Это защищает от обычного orphan/PID-reuse сценария. Динамический spawn во время снимка, access-denied и системные гонки не имеют отдельного нового stress test; termination остаётся best effort.
- MCP integration доказывает реакцию сервера на реальный protocol notification. Автоматическая отправка notification при одном лишь local client `CancelAsync` в pipe harness не подтверждена и исполнителем не заявлена. Отдельного protocol integration case отмены именно во время build нет.
- Прежний parser иногда возвращает имена/counters без Error message; это записано исполнителем и не скрывает failed outcome. Parser и его прежняя интерпретация соседних no-match summaries не расширялись в task-09.
- Предельные output sizes, дополнительные adapters/runtime configurations и многопроцессный stress заново не исполнялись; прочитаны соответствующие shared paths и существующие тесты. Подтверждённой молчаливой потери тестов в scope task-09 не найдено.

Не проверено отдельным запуском рецензента: build/tests и другие ОС; использованы результаты координатора и статический аудит.
Остающийся риск до исправления: ответ при ошибке может отправить клиента по неподдерживаемому пути получения диагностики.
