# Проектные тайминги загрузки и поиска — 2026-10-22

Статус: **accepted in the requested scope, 2026-10-22**. Scope: запрос владельца добавить
разбивку design-time build и первого поиска по проектам для следующего офисного
прогона; сервер 1.5.8 и `scripts/Measure-WorkspaceLoad.ps1`. Полная task-02 и task-01
не объявляются завершёнными. Независимое ревью не проводилось. Сохранение серии запрошено владельцем
позднее 2026-10-22; [checkpoint](commit-2026-10-22.md).

## Что изменено

- В том же проходе provenance replay наблюдаются интервалы Project, Evaluation,
  Target и Task. Отбор provenance contexts/items/bindings, строгий reader и admission
  не меняются. Ключи pending-интервалов включают ordinal binlog и event context.
- `workspace_load_designtime` содержит все завершённые проектные интервалы,
  агрегированные по пути: число сборок/неуспешных сборок, inclusive sum и максимум.
  Дополнительно: число и длительность logged evaluations, top-10 targets/tasks,
  точное время MSBuild open через существующий TimeProvider, диапазон меток binlog
  и неотрицательный остаток либо причина его недоступности.
- При частичном/повреждённом replay сохраняются доступные завершённые интервалы,
  список replay failures, непарные starts/finishes и некорректные интервалы.
  Сводка не доказывает полноту root evaluations. Суммы вложенных/параллельных
  интервалов не являются wall time; configs/TFM одного пути объединены.
- `workspace_search_project` измеряет существующий последовательный
  `SymbolFinder.FindDeclarationsAsync` для каждого проекта: путь, project id,
  имя, число документов, elapsed, кандидаты и outcome. `workspace_search_summary`
  содержит общий итог цикла, включая failure/cancellation. Исходное исключение
  пробрасывается. Новые параметры MCP и иной алгоритм поиска не добавлены.
- `searchId` отделяет повторные попытки sanitizer. Скрипт связывает записи с
  request id и session id, редактирует пути до помещения в JSON и выводит top-10.
  Handler time вне циклов поиска показывается отдельно. Старый сервер даёт явную
  надпись об отсутствии новой разбивки.

## Validation

- Release build всего `RoslynMcpServer.sln` через `run_dotnet_build`: PASS.
- Новые behavioral tests: **7 passed / 0 failed**. Проверены пересечение вложенных
  интервалов, одинаковые contexts в разных binlog, partial/empty/negative intervals,
  top-10 targets/tasks, эквивалентность результата поиска и проброс cancellation.
- Весь SourceStructure: **123 passed**, точные inventories лимитов не менялись.
- Полный main CI `Category!=AnalyzerLifecycle`, Release, финальный C#:
  **1409 passed / 0 failed / 0 skipped**. Receipt:
  `artifacts/project-timings-20261022/main-ci-receipt.json`.
- AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`:
  **13 passed / 0 failed / 0 skipped**, 50 с.
  `artifacts/project-timings-20261022/lifecycle-validated.trx` и одноимённый `.log`.
  Первая попытка в sandbox завершилась abort: VSTest не соединился с testhost за
  90 с, тесты не выполнялись. Проверенный повтор вне sandbox прошёл; исходный
  `lifecycle.log`/`.trx` сохранён отдельно.
- `scripts/Test-MeasureWorkspaceLoad.ps1`: PASS в Windows PowerShell 5.1 и PowerShell 7.
  Проверены request/session isolation, partial replay, редактирование путей до
  записи JSON, остаток времени поиска, отсутствие наследования строк на RAM hit.
- End-to-end, финальный скрипт и сервер **1.5.8**, Windows PowerShell 5.1,
  `.github/smoke/SmokeApp/SmokeApp.csproj`, два новых процесса + поиск + RAM hit:
  PASS. В обоих прогонах есть проектные строки, непустые bounded top-10
  targets/tasks, search summary и остаток времени, пути `<repo>` в JSON;
  физическая сводка отсутствует на RAM hit. Артефакты:
  `artifacts/project-timings-20261022/smoke-final/` и `smoke-final.log`.
- Diff whitespace check: PASS с `core.whitespace=cr-at-eol` для сохранённых CRLF.

На этапе разработки прямые synthetic-event tests требовали регистрации MSBuild
до JIT методов, использующих Framework types. Регистрация добавлена в constructor
fixture; повторные focused tests и полный main CI прошли.

## Границы приёмки

Этот scope принят как диагностическое изменение. Публичные ответы инструментов,
алгоритм поиска и provenance admission не менялись. Формальная приёмка всей
task-02 зависит от task-01 и её собственного контракта; она остаётся pending.
Офисный прогон 1.5.8 ещё не получен. На baseline 1.5.7 от 20:50:57 загружены
142 проекта; load 48,40 с, первый поиск 36,87 с (медианы трёх измерений), replay
частичный. Новый отчёт явно показывает эту неполноту вместо обещания покрытия
всех проектов. Скрипт по-прежнему измеряет внешний вызов, а не суммирует
inclusive project durations как wall time.

Следующий офисный прогон выполняется прежней командой из
[протокола](task-00-protocol.md), с пересобранным сервером 1.5.8 и обновлённым скриптом.
Текущий офисный baseline 1.5.7 в `artifacts` не переписывается и не коммитится.

## Полученные офисные результаты, 2026-10-22

Описанное выше ожидание нового прогона завершено: 1.5.8 от 21:46:17 и
1.5.9 от 22:15:22 разобраны. См. [task-08](../task-08-source-search-and-office-results-med.md)
и [исследование оставшегося поиска](first-search-investigation-2026-10-22.md).
Прежние validation receipts и scope принятой диагностики не изменены.
