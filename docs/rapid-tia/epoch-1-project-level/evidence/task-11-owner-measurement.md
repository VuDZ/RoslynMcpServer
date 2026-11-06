# Task-11 — ограниченный замер владельца по R-18 / T09-C1

- Дата: **2026-11-06**, Windows; исполнитель Codex (точный runtime model ID средой не предоставлен).
- Настоящий owner repository: текущий `RoslynMcpServer-rapid-tia`, baseline `0e4d3fa` с документацией/version task-11; отдельный stdio MCP из собранного `bin/Release/net10.0/RoslynMcpServer.dll`, **1.7.0.0**, full, 55 tools, protocol `2025-11-25`.
- Координатор до пробы подтвердил Release solution build на 1.7.0 и передал исполнителю исключительный runner slot. Прочие builds/tests не выполнялись параллельно.
- Все полные tool responses, monotonic timings и доказательство восстановления — [JSON](task-11-owner-measurement.json). Это один реальный owner-сценарий, отдельно от двухпроектной fixture [второго рецензента task-09](task-09-review-round-2-claude.md#t09-c1--после-собственного-run-проект-до-конца-baseline-идёт-целиком).

## Последовательность и наблюдения

1. `get_mcp_server_info` подтвердил 1.7.0.0 и отдельный процесс. `load_workspace` на настоящий `RoslynMcpServer.sln`, Release: **5 проектов**, restore assets 5/5, без buildArgs. После capture исходный estimate: **0 members / 0 files / 0 projects**, `Not covered: none`.
2. Единственная обратимая правка существующего проходящего теста `VstestRunPlannerTests.Saved_adapter_names_select_every_measured_case`: выражение `probe == "parameters" ? 2 : 1` заменено на `probe == "parameters" ? (1 + 1) : 1`. Байты и mtime сохранены до записи. Следующий estimate: **1 member / 1 file / 1 project**, только фильтр `FullyQualifiedName~RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Saved_adapter_names_select_every_measured_case`. Перед запуском проверено отсутствие whole-project и посторонних планов.
3. Один `estimate_affected_tests(run: true, timeoutSeconds: 300)` исполнил этот фильтр со сборкой текущих исходников: **passed, Total 16 / Passed 16 / Failed 0 / Skipped 0**. Полное время MCP call по monotonic clock: **5.164 s**, включая estimate, incremental build, запуск и форматирование; это не чистое время тестов.
4. Повторный estimate без новых правок: **1 member / 2 files / 1 project**, `RoslynMcpServer.Tests.csproj whole project`. Причина — `RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.dll`; цена запроса **0.747 s**. Прогон whole-project **не выполнялся**.
5. Настоящий `reset_workspace` и `load_workspace` того же solution (не cached reuse после reset): опять 5 проектов. Estimate остаётся whole-project по тому же DLL, **1 member / 2 files**; добавлена обычная оговорка non-C# reload window. Reset **0.006 s**, load **1.104 s**, estimate **1.355 s**.
6. Исходные source bytes и mtime точно восстановлены. Последний estimate: **0 members / 1 file / 1 project**, всё ещё whole-project по DLL; запрос **0.533 s**. Нового `run` не было.

Итог первой метрики R-18: post-run widening наблюдалось **1/1** в одной ограниченной последовательности на этом owner repository. Это не оценка частоты в длительных рабочих сессиях или на других проектах. Тесты не потеряны; следующий план шире.

## Цена полного прогона и границы

Нефильтрованный whole-project через TIA намеренно не запускался: он включил бы `AnalyzerLifecycle` и `RequiresPinnedSdk`, которые main CI исключает. Стоимость такого whole-project **не измерена**. Координатор после восстановления source/rebuild выполнил полный Windows main CI: **2004/2004 PASS за202.731s** wallclock полного MCP call, включая накладные расходы инструмента; Release, noBuild/noRestore, `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` ([финальная validation](task-11-final-validation.md)). Это отдельный ориентир CI, не цена нефильтрованного плана TIA. Измеренные **5.164 s** относятся только к первому выбранному методу/16 cases.

Проба доказывает T09-C1 для одного incremental build тестового проекта и сохранение состояния после reset/reload/отката. Она не измеряет стоимость расширения на каждом проекте владельца, долгую частоту gaps, полноту выбора на истории или другие generic/adapter формы. Решение о production-исправлении механизма по-прежнему принимает владелец после замеров; task-11 механизм не меняет.

## Восстановление и среда

- SHA-256 target до/после: `25ab0649527040144545c5843921cf209d87efe6b02cd333b1856090010a7677`.
- `mtime_ns` до/после: `1793961299408103400`.
- Восстановление выполнено в `finally` с assert bytes/hash/mtime; `git diff --exit-code -- RoslynMcpServer.Tests/Testing/VstestRunPlannerTests.cs` — PASS. Временный driver и резервные source bytes удалены; новых source helpers не было. Остались только ignored raw results и tracked evidence.
- Отдельный MCP завершился по EOF с **exit 0**, shutdown подтверждён. Runner slot возвращён координатору; перед финальной validation он пересобирает восстановленные исходники, поскольку после пробы test DLL ещё содержала эквивалентное выражение.
- Первый sandboxed запуск не подключился к BuildHost named pipe (exit -1) ещё на initial load; правок исходника и тестовых запусков не было, восстановление/завершение подтверждены. Повтор вне sandbox успешно выполнил всю пробу. Неуспешная попытка сохранена в JSON, не посчитана как вторая измеренная последовательность или как product failure.
