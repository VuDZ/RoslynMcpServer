# Task-12 — GREEN

- Дата: **2026-11-07**. Linux, .NET SDK 10.0.114, Release. Исполнитель: Claude Opus 5.5 (`claude-opus-5-5`).
- Изменение: [ProjectImpactMapper](../../../../Services/Testing/ProjectImpactMapper.cs) — путь, у которого в карте входов только
  роли `Output`/`MetadataDependency` с производителем из загруженного solution, выбирает лишь потребителей вне графа
  проектов производителя; без таких потребителей путь пропускается (не в изменённых путях, не в «Not covered»).

## Новые тесты

Тот же фильтр, что в [RED](task-12-red.md): **6/6 passed**.

## Группа TIA и SourceStructure

Фильтр `RoslynMcpServer.Tests.Testing.*`, `EstimateAffectedTestsCatalog*`, `RoslynMcpServer.Tests.SourceStructure.*`,
`DotNetTestExecutorTests` с исключением `AnalyzerLifecycle`, `RequiresPinnedSdk`, `RequiresWindows`:
**660/660 passed**, 0 skipped, 7 мин 30 с. В том числе все тесты SourceStructure; inventory известных ограничений не
менялся.

Результаты сборки solution и полного main suite — в [отчёте](../task-12-implementation-report.md#validation).
