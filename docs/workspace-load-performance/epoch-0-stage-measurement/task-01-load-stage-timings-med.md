# D/E0/task-01 — Время стадий загрузки в логе

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Grok 4.7.
- Рекомендуемый reasoning: GPT: medium.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — правка горячего пути загрузки; ошибка в порядке вызовов или в `finally` может изменить поведение load или потерять запись при отказе.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_производительности`.
- Статус: planned.
- Depends on: —.
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Одна структурированная запись лога на каждую физическую загрузку и одна на RAM hit,
с длительностью каждой стадии. Поведение загрузки и ответы инструментов не меняются.

## Входы и границы

[Тема](../README.md), [карта эпохи](README.md). Перед C# правкой — полный
[code style](../../code-style.md).

Seams:

- `SolutionManager.LoadAndPrepareAsync` — ожидание manager lock, overlay prepare, если выполнялся;
- `SolutionManager.LoadCoreAsync` — execution gate, публикация, `CollectDiagnostics`,
  `StartDiskWatcherUnderLock` (построение input map отдельно от старта watchers);
- `AnalyzerProvenanceCaptureService.OpenAndCaptureAsync` — MSBuild open,
  перечисление binlog, replay (уже есть `ReplayDuration`), `BuildSnapshot`.

Запись `workspace_load_stages` содержит:

- `outcome` (`success`, `failure`, `cancelled`), `cached=false`;
- `lockWaitMs`, `openMs`, `replayMs`, `snapshotMs`, `executionGateMs`, `publishMs`,
  `diagnosticsMs`, `inputMapMs`, `watcherStartMs`, `prepareMs`, `totalMs`;
- `projects`, `documents`, `additionalDocuments`, `binlogFiles`, `binlogBytes`.

Стадия, до которой загрузка не дошла, пишется как отсутствующая, а не как `0`.
Запись выпускается и при отказе или отмене. RAM hit пишет ту же запись с
`cached=true`, `lockWaitMs`, `flushMs`, `totalMs`.

Время — через инжектируемый `TimeProvider`. Новых параметров инструментов,
полей ответа, defaults и конфигурационных ключей нет. Существующая строка
`Analyzer provenance capture finished` сохраняется.

## Проверки

- Тест выполняет реальную загрузку маленького fixture с тестовым logger:
  ровно одна запись, все стадии успешной загрузки присутствуют и неотрицательны,
  `totalMs` не меньше каждой стадии.
- Отменённая загрузка даёт `outcome=cancelled` и только достигнутые стадии.
- RAM hit даёт `cached=true` и не даёт полей физической загрузки.
- Существующие regressions загрузки, RAM matcher, overlay и watcher проходят без правок.
- Проверки поведения выполняются запуском, а не разбором текста production-кода.

Validation: правила [эпохи](README.md#общие-правила-эпохи), включая AnalyzerLifecycle.
Patch bump, строка `CHANGELOG.md`; README Reference не меняется (набор инструментов прежний).
