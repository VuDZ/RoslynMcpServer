# Task-09 — реализация `run` для VSTest

Дата: **2026-11-06**. Исполнитель: **Codex**; точный runtime model ID средой не предоставлен.

Статус: **accepted, 2026-11-06**. Приняты VSTest `run`, общий исполнитель, свежая сборка, отдельные исходы/таймауты и MCP-отмена; **2 раунда / 1 исправление**, R1-001 закрыто тем же исполнителем. [Раунд 2 — ACCEPT](evidence/task-09-review-round-2.md), [финальная validation](evidence/task-09-final-validation.md): Release solution build PASS, focused **220/220**, SourceStructure **123/123**, main **2004/2004**, AnalyzerLifecycle **13/13**. Версия **1.6.1** не повышалась; push/release не выполнялись. Task-10/task-11 вне scope, эпоха целиком не принята.

## Scope

`estimate_affected_tests(run: true, timeoutSeconds: 300)` последовательно исполняет существующие планы task-07. Для каждого проекта — incremental `dotnet build`, затем один VSTest `dotnet test --no-build --no-restore` с фильтром плана либо без фильтра для whole-project. Конфигурация, платформа и buildArgs берутся из `load_workspace`; `run` не наследует историческое пропускание сборки из `run_test_by_filter`. Таймаут применяется отдельно к каждому реальному процессу build/test, 0 отключает его. Ошибка сборки, failing tests или timeout не останавливают следующие проекты.

`DotNetTestExecutor` возвращает `DotNetTestResult`: самостоятельный исход, результаты build/test и разобранные VSTest-счётчики. Исходы: `passed`, `failed`, `build failed`, `timed out`, `cancelled`, `not run`. Нулевой/неподтверждённый запуск не выдаётся за успешную проверку; `not run` означает отсутствие старта по отмене или невозможности подготовки. Таймаут/отмена не превращают код убитого процесса в failing tests.

Обычная ветка всех трёх `run_*` использует выделенный исполнитель; прежние Markdown-ветки, метаданные, build warnings, no-match, silent failure, output budgets и reportCursor оставлены снаружи. Исторический общий бюджет build+test у `run_*` сохранён. DLL-ветка остаётся у существующего структурированного `TestDllEnsure` с прежней политикой freshness/solution target; она не дублируется для TIA. Новый DI-контракт не понадобился: исполнитель получает существующий `TestDllEnsure.Runner` и использует тот же `RunCliAsync`.

Ответ `run` начинается прежним предупреждением, показывает исход и имеющиеся счётчики по каждому проекту, форматирует failing tests/build diagnostics в существующих бюджетах и заканчивается блоком `Not covered`. Строка project-level state и disk-sync notes находятся перед этим блоком. Ранние ответы `run` также имеют предупреждение, state и завершающее конкретное ограничение.

При отмене настоящего MCP `tools/call` текущая операция останавливает дерево процессов, последующие проекты получают `not run`; все исходы попадают в одну строку `Rapid TIA run:` серверного лога. Доставка ответа на отменённый запрос не считается обязательной.

В Windows реальная проверка выявила гонку прежнего `Kill(entireProcessTree:true)`: VSTest host завершался при остановке родителя, оставляя собственный `ping` без родителя. `DotNetCliRunner` теперь до остановки снимает потомков через уже существующий `WindowsParentProcess`, сохраняет process handles и проверяет время старта относительно каждого родителя, затем завершает снимок от листьев к корню и вызывает прежний root tree kill. Выход процесса во время снимка допустим; при недоступном снимке root kill сохраняется. На других платформах прежний tree-kill путь сохранён. Это изменение общего runner требует дополнительной AnalyzerLifecycle validation у координатора.

Публичных инструментов осталось **55/full и 16/lite**. `estimate_affected_tests` теперь имеет process/readOnly=false descriptor. Измеренный minified `tools/list`: **46 902/full, 20 506/lite**, прирост **328 байт** у каждой поверхности. Все записанные варианты каталога обновлены по фактическому измерению; README и help описывают `run`.

MTP, task-10, task-11, граф вызовов, дополнительные параметры и зависимости не реализовывались. Планировщик task-07 и требования specification не менялись.

## TDD и проверки

Все обязательные сценарии написаны до реализации. [RED](evidence/task-09-red.md): компилируемый пустой контракт исполнителя и игнорируемые параметры `run`/timeout; **20 обнаруженных тестов, 0 passed / 20 expected failed / 0 skipped** через Roslyn MCP, Release. Это 11 executor checks, 6 real-workspace run checks, 1 настоящий MCP cancellation check, 2 обновлённые проверки schema/descriptor. Ошибки предварительной настройки fixture исправлены до записанного RED и RED не считаются.

Проверки исполнения используют два настоящих xUnit/VSTest проекта, реальный MSBuild workspace, baseline и MCP editing path. Вручную исполняется возвращённый фильтр; ожидаемые First1/First2 и допустимый substring extra FirstExtra запускаются, Other не запускается. Для проверки старых DLL ручной `--no-build` реально проходит и даёт прежний инвентарь, затем `run` исполняет новый failing body, оставляет два маркера NewSource, возвращает Failed:2 и продолжает второй проект. Также проверены build failed без запуска старых тестов, whole-project без фильтра, timeout с остановкой testhost/дочернего ping, пустой план.

Настоящий MCP client/server transport вызывается из текущей скомпилированной production assembly, не из внешнего установленного MCP-сервера. В test-only incoming filter фиксируется типизированный ID реального `tools/call`; `McpClient.SendNotificationAsync` отправляет `notifications/cancelled` именно для этого ID, затем отменяется локальное ожидание. Проверены завершение обоих PID, отсутствие второго проекта, единичная строка лога с cancelled/not run и живой следующий запрос. Автоматическая отправка notification при одном лишь client `CancelAsync` в этом pipe-harness не наблюдалась; она здесь **не заявляется проверенной**. Production SDK/protocol не менялись.

После первоначальной реализации добавлены **5 поздних regression checks**: 3 точных прежних cancellation-ответа `run_*` при OCE без signalled request token и 2 ранних ответа `run` (нет workspace / pre-cancelled). Причина — проверка координатором сохранности внешнего ответа и завершающего `Not covered`; эти проверки не объявляются написанными в RED. До R1-001 новых тест-кейсов для main suite — **23**; после R1-001 — **24**; 2 существующих catalog cases обновлены под новый контракт.

Коррекции fixtures после первых GREEN-проб записаны отдельно: ручной replay фильтра использует исторический noBuild=true, чтобы не создавать известное проектное расширение T04-C3 до второго estimate; freshness доказывается исполненным маркером, а не необязательной строкой Error существующего parser. Unit fixture с выполненным CLI и нулём matches исправлен с `not run` на `failed`: процесс действительно стартовал; настоящий pre-cancelled/no-start case остаётся отдельно.

Неизменённые ожидания `TestToolsDllBuildTests` и `TestToolsRunTestByFilterTests` дали **17/17** до реализации и проходят после выделения исполнителя. Дополнительно проверены весь Hosting namespace, DotNetCliRunner и VstestOutputParser. Фактические промежуточные итоги: 37→31/6 (реальные tree kill failures и первоначальные fixture/schema проблемы), 43→41/2, 105→103/2, затем **182→181 passed / 1 failed / 0 skipped**. Последнее падение — второй сохранённый literal прежнего размера lite+files 25234 на строке497; исправлен на измеренный 25562. После этого полного прогона добавлены явные asserts прежнего PASS/инвентаря в stale-DLL тесте. После первоначальной freeze, до R1-001, координатор сообщил Release solution build PASS и расширенный focused 201/201 PASS на том коде, включая stale-DLL assertions и все catalog numbers; [сводка и точный фильтр](evidence/task-09-implementation-checks.md).

Tracked diff до staging прошёл `git -c core.whitespace=cr-at-eol diff --check`; после staging обнаружена дополнительная пустая строка EOF нового `DotNetTestExecutor.cs`, записанная как formatting nit в финальном отчёте без изменения проверенного кода. `cr-at-eol` учитывает уже существующее CRLF в git index для Tools/TestTools.cs, не нормализует весь файл. Инвентарь SourceStructure не менялся и не ослаблялся: финальные 123/123 подтверждают 16 известных limits с прежними identities/kinds/paths.

## Файлы

Production: `Tools/TestTools.cs`; `Services/Build/DotNetTestExecutor.cs`; `Services/Build/Models/DotNetTestResult.cs`; `Services/Build/Models/TestProcessOutcome.cs`; `Services/Build/DotNetCliRunner.cs`; `Services/Testing/TestImpactFormatter.cs`; `Hosting/McpToolCatalog.cs`; `Hosting/McpToolHelpCatalog.cs`.

Тесты: новые `TestRunner/DotNetTestExecutorTests.cs`, `TestRunner/TestToolsCancellationOutputTests.cs`, `Testing/TestImpactRunFixture.cs`, `Testing/EstimateAffectedTestsRunTests.cs`, `Testing/EstimateAffectedTestsCancellationTests.cs`; обновлены `Hosting/EstimateAffectedTestsCatalogTests.cs` и `Hosting/McpToolCatalogTests.cs` (пути относительно RoslynMcpServer.Tests).

Дополнительно R1-001 затронул `Diagnostics/BuildOutputReport.cs`, `Diagnostics/TestOutputReportOptions.cs`, `Diagnostics/VstestOutputParser.cs`; task/index/status docs и review/final-validation evidence оформлены координатором.

Документация: корневой `README.md` (существующие EN/RU записи инструмента и измеренные размеры), этот отчёт, RED и implementation-checks evidence. Untracked `.agents/skills/prepare-github-release/` не относится к задаче и не изменялся.

## Ограничения и итоговая приёмка

Существующий VstestOutputParser иногда извлекает failing test names и счётчики без сообщения Error из настоящего xUnit вывода. Извлечение данных parser и формат `run_*` сохранены; R1-001 меняет только показ follow-up-подсказки в renderer для TIA. Это не скрывает failed outcome или потерю тестов, но диагностика может быть беднее.

После исправления R1-001 координатор повторил все проверки на окончательном коде: Release solution build PASS (3 прежних предупреждения), focused220/220, SourceStructure123/123, main2004/2004, opted-in AnalyzerLifecycle13/13. [Независимый раунд 2 — ACCEPT](evidence/task-09-review-round-2.md); [финальный отчёт](evidence/task-09-final-validation.md). Linux/macOS не запускались; автоматическая отмена клиента без фактического protocol notification не подтверждена. До исправления [зелёная validation](evidence/task-09-validation-before-round-2.md) сохранена как история. Accepted scope/date отражены также в task file, README эпохи, программы и индексе docs.

## Исправление после раунда 1 — R1-001

[Независимый раунд1 — MUST-FIX](evidence/task-09-review-round-1.md) обнаружил реальную неверную инструкцию: build failure направлял к `reportCursor` этого же инструмента, а длинный failed StdOut предлагал `includeFullOutput`/`maxOutputChars`; ни одного из этих параметров в минимальной schema нет.

Сначала записан [RED:2/2 expected failed](evidence/task-09-review-round-1-red.md) на настоящих MSBuild/xUnit запусках. Guard существующего build-failure case запрещает reportCursor; новый case печатает 6000 символов через ITestOutputHelper, падает и проверяет границы бюджета2500/head+tail, BEGIN/END и отсутствие неподдерживаемых параметров. Новый case написан до R1 production fix; это **1 дополнительный** main case поверх первоначальных23, ожидаемый main inventory2004.

Исправление минимально и касается представления: BuildOutputReport получает opt-out attachment (по умолчанию true, прежние callers не меняются); внутренний TestOutputReportOptions.SuppressOutputOptionHints по умолчанию false, TIA задаёт true. TestImpactFormatter отключает только attachment/action-подсказки, а общий VSTest renderer оставляет сообщение о truncation и прежние бюджеты. Parsing и raw пользовательские потоки не переписываются; Markdown другого инструмента не разбирается. Публичная schema и прежний `run_*` вывод сохранены, новых MCP параметров нет.

Изменённые после раунда файлы: Diagnostics/BuildOutputReport.cs, Diagnostics/TestOutputReportOptions.cs, Diagnostics/VstestOutputParser.cs, Services/Testing/TestImpactFormatter.cs; tests/Testing/TestImpactRunFixture.cs (обычный ITestOutputHelper constructor), tests/Testing/EstimateAffectedTestsRunTests.cs; этот отчёт и RED/GREEN evidence (tests paths относительно RoslynMcpServer.Tests).

[GREEN:93/93 PASS](evidence/task-09-review-round-1-green.md) включает реальные R1 cases, все run integration/early responses, BuildOutputReportTests (прежние cursors), VstestOutputParserTests (прежние output hints/budgets), неизменённые legacy tests и schema. Код/тесты после freeze не менялись. R1-001 независимо закрыто [раундом 2 — ACCEPT](evidence/task-09-review-round-2.md), финальные build/Source/main/lifecycle выполнены координатором ([validation](evidence/task-09-final-validation.md)). Счётчики task-09: **2 раунда / 1 исправление**; версия не менялась. Финальные status/index edits и commit принадлежат координатору.