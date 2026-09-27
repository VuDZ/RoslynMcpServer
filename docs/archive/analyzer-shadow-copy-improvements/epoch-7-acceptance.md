# Приёмка эпохи 7 — урезать lifecycle-набор

Дата: 2026-09-28. Вердикт: **принимается**.
Норматив: [epoch-7-lifecycle-suite-cut.md](epoch-7-lifecycle-suite-cut.md).

Первый прогон в тот же день **не принят**: E7-1 (две строки V1→V2 в одном методе)
и E7-2 (`Watcher_flush_stales_held_candidate_and_keeps_flushed_text` вместо строки
таблицы). Повтор после правки закрывает оба.

## Повторный прогон

Незакоммиченное дерево. `dotnet test` проекта `RoslynMcpServer.Tests`, Debug,
SDK из `C:\Program Files\dotnet`. Opt-in — `--settings` с
`ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.

| Проверка | Результат |
| --- | --- |
| `Category=AnalyzerLifecycle` без переменной | **13 skipped**, 0 failed, exit 0, Duration 58 ms. Среди имён есть `V1_to_V2_process_restart_executes_exact_V2`, нет `Watcher_flush_stales_held_candidate_and_keeps_flushed_text` |
| То же с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, `--no-build` | **13 passed**, 0 skipped, 2 мин 40 с |

`V1_to_V2_cached_and_reset_refuse_execution_process_restart_runs_V2` только отказывает
на cached и reset. Исполнение точного V2 новым процессом — отдельный метод
`V1_to_V2_process_restart_executes_exact_V2`. Файл `AnalyzerLifecycleHostTests.WriteBase.cs`
удалён. Тринадцать `[AnalyzerLifecycleFact]` совпадают со строками таблицы.

Остальное с первого прогона не отменялось: opt-in сравнивает загруженный путь с shadow
и с реальным output, capture требует два `ShadowCopyPath`, R3 отдельно от replay,
R1 / R2 / flush после ban на месте, инвентаризация без trait (5 passed), CI — джобы
`unit` и `lifecycle`.

| ID | Sev | Статус |
| --- | --- | --- |
| E7-1 | P1 | **закрыт** |
| E7-2 | P1 | **закрыт** |
