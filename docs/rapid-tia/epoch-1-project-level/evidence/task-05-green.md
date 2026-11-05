# task-05 — focused GREEN

- Дата: **2026-11-05** (Europe/Moscow).
- Среда: Windows, Release; специализированный `run_specific_test` Roslyn MCP.
- Исполнитель: Codex; точный runtime model ID средой не предоставлен, **unknown**.
- Предшествующий [RED](task-05-red.md) выполнил координатор до разрешения реализовать поведение.

Последовательные прогоны после минимальной реализации:

1. `FullyQualifiedName~TestImpactComposerTests`: **29 total, 29 passed, 0 failed**.
2. `FullyQualifiedName~TestImpactWatcherTests`: **8 total, 8 passed, 0 failed**.
3. `FullyQualifiedName~ProjectImpactMapper`: **37 total, 37 passed, 0 failed**,
   включая прежние unit/integration проверки task-04 и реальные сборки их фикстур.

Итого focused: **74 выполнено, 74 прошло, 0 падений**. Сообщения MCP не сообщают отдельных
счётчиков skipped; полные счётчики финальной приёмки сверяет координатор по TRX.
Тесты/фикстуры task-05 после RED не менялись, новые тесты не добавлялись.

Две прежние проверки task-04 неизвестного `.cs` теперь передают
`projectGraphStale=true`, `projectGraphStaleFromGraphFile=false`. Они проверяют тот же
ближайший проект, транзитивные тестовые проекты, reload и uncovered; агрегированный stale
теперь ожидается `true`, что соответствует состоянию настоящего watcher. Остальные
прежние вызовы передают оба актуальных watcher-флага; старый unit helper с одним `true`
означает graph-file stale, сохраняя прежнюю проверку расширения до всех тестов.

Release build всего solution, все SourceStructure и main suite — отдельная финальная
валидация координатора; focused GREEN её не заменяет.
