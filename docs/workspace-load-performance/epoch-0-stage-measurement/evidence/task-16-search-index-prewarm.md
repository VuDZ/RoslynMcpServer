# D/E0/task-16 — реализация и validation

Дата: **2026-10-23**. Сервер **1.5.15**, ветка `research/workspace-load-performance`.
Реализация — Claude Opus 5.5. Независимого ревью не было. Офисный замер по порогам
задачи pending, вердикта go/no-go пока нет.

## Проверки

| проверка | результат |
|---|---|
| `SearchIndexPrewarmTests` (5 тестов) | PASS — см. ниже |
| `scripts/Test-MeasureWorkspaceLoad.ps1` | PASS, строка `Search prewarm` в окне загрузки и в окне поиска, без записи |
| `scripts/Test-FirstSearchDiagnostics.ps1` | PASS, сравнение вариантов, базовая строка «прогрев выключен» при той же паузе, учёт исходов прогрева |
| Release build решения | PASS, только прежние предупреждения CS8603, CS8601, xUnit1031 |
| main CI `Category!=AnalyzerLifecycle&Category!=RequiresWindows`, Release, Linux (SourceStructure входит) | 1393 passed / 0 failed / 0 skipped |
| AnalyzerLifecycle, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, Release, Linux | 13 passed / 0 failed / 0 skipped |

Что проверяют тесты `SearchIndexPrewarmTests`:
- прогрев обходит все проекты и пишет `success`;
- отменённый прогрев пишет `cancelled` и не бросает исключение;
- свежая загрузка запускает прогрев, после него поиск находит то же объявление;
- выключенный прогрев не запускается и ничего не пишет в лог;
- очистка workspace завершает прогрев без исключения.

## Не проверено

- Время: пороги заданы для офисного решения и Roslyn.slnx (через 30 с после загрузки
  и сразу после неё). Нужен офисный прогон
  `Invoke-FirstSearchDiagnostics.ps1` со значениями по умолчанию: server GC,
  прогрев on/off, пауза 0/30 с.
- Работает ли общий кеш Roslyn между прогревом и параллельным поиском без двойной
  работы, покажет вариант «пауза 0»: порог там ≤ 1,10.
- Нагрузка на CPU, если пользователь не ищет вовсе. Прогрев однократный, после загрузки.
