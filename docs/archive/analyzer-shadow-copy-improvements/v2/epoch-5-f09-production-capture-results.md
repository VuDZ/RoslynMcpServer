# F-09 — production capture/snapshot

Дата: 2026-09-12. Статус: **реализовано; независимая приёмка
[принята](epoch-5-f09-production-capture-acceptance.md)**.
Matcher и E5-S2 rollout не изменены.

Норматив:
[capture design](epoch-5-f09-capture-design.md),
[P0 acceptance](epoch-5-f09-p0-acceptance.md),
[P0 results](epoch-5-f09-p0-spike-results.md).

## Реализация

- В production project добавлен `Microsoft.Build` 18.6.3 с
  `PrivateAssets="all"` / `ExcludeAssets="runtime"` (F09-04).
- `AnalyzerProvenanceCaptureService` открывает solution/project через
  `BinaryLogger` с `ProjectImports=None` и `LoggerVerbosity.Normal`.
- Replay выполняется после `Open*Async`; snapshot immutable и привязан к
  `(loadSessionId, loaded path, requested globals, registered/runtime MSBuild)`.
- Captured `Analyzer` items сохраняют event context и edge metadata. Join
  использует exact project file, analyzer identity и exact output paths; он не
  использует filename, path segments, first match или unevaluated TFM.
- Отсутствующий/corrupt binlog даёт `Failed`, смесь valid+corrupt —
  `Incomplete`; workspace load при этом остаётся успешным. Частичный результат
  не становится confirmed выбором для failed context.
- Временный случайный process-scoped каталог удаляется в `finally` с bounded
  retry. Event payload, properties и metadata не логируются.
- Телеметрия: status, files, bytes, replay wall, project contexts, analyzer
  items и confirmed bindings.

## Lifetime (P0-7)

Capture вызывается в `SolutionManager.LoadCoreAsync` только после cache guard,
в physical-open ветке. Snapshot публикуется одной ссылкой после завершения
open/replay и сбрасывается в `ClearWorkspaceAsync`.

Production lifecycle tests подтверждают:

- первый physical load: `captureCount 0 -> 1`;
- cached load, document edit, semantic query и artifact refresh: без прироста;
- смена Configuration и graph-stale: по одному новому capture;
- reset удаляет snapshot; следующий load создаёт новый;
- cleanup завершён до ответа (`tempDirectoryCount == 0`).

## Проверка

- `run_dotnet_build`, Debug, `--no-incremental`: success.
- `F09ProductionCaptureTests`: **3 passed / 0 failed**.
- Функциональные P0-регрессии отдельно:
  exact project joins, два inner TFM/foreign same-name и fail-closed replay —
  **3 passed / 0 failed**.

## Граница следующего шага

Этот этап не читает snapshot в `AnalyzerReferenceShadowCopier` и не меняет
matcher. E5-S2/marker acceptance допускаются только отдельным следующим этапом
после независимой приёмки production snapshot.
