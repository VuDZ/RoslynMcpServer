# D/E0/task-02 — Сводка design-time build из binlog

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Grok 4.7.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — правка replay provenance; изменение отбора событий может изменить analyzer admission evidence.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_производительности`, `проектирование_проверок`.
- Статус: planned.
- Depends on: [task-01](task-01-load-stage-timings-med.md).
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Разделить `openMs` из task-01 на то, что видно в binlog design-time build, и остаток.
Binlog уже снимается при каждой загрузке для analyzer provenance и уже
воспроизводится; сводка собирается в том же проходе.

## Входы и границы

[Тема](../README.md), [карта эпохи](README.md), [task-01](task-01-load-stage-timings-med.md).
Факты о binlog — [S-002](../../workspace-state-cache/epoch-1-disk-cache-mvp/evidence/task-11-raw-evidence.md):
root evaluation в BuildHost не логируется (ProjectCollection без логгеров),
логируются evaluations внутри build и сам design-time build.

Запись `workspace_load_designtime` (одна на загрузку):

- `loggedEvaluations`, `loggedEvaluationMs` (сумма и максимум);
- `projectBuilds` и top-10 project contexts по длительности ProjectStarted→ProjectFinished;
- top-10 targets и top-10 tasks по суммарной длительности, с числом вызовов;
- `unattributedOpenMs` = `openMs` − длительность от первого до последнего события binlog.
  Остаток включает старт BuildHost, нелогируемые root evaluations, IPC и сборку
  ProjectInfo в Roslyn. В записи он так и называется: остаток, а не измеренная стадия.

Длительности вложенных targets и tasks пересекаются; суммы не выдаются за wall time.
Отбор событий для provenance не меняется. Память ограничена числом contexts и имён
targets и tasks; второй проход по файлу не добавляется.

## Проверки

- `AnalyzerProvenanceSnapshot` на существующих fixtures совпадает до и после изменения
  (статус, bindings, analyzer items, project contexts).
- Тест на реальной загрузке fixture: запись есть, top-списки непусты и ограничены
  десятью, `unattributedOpenMs` неотрицателен либо отсутствует с причиной.
- Повреждённый или пустой binlog не роняет загрузку: сводка отсутствует с причиной,
  provenance ведёт себя как раньше.

Validation: правила [эпохи](README.md#общие-правила-эпохи), включая AnalyzerLifecycle.
Patch bump, строка `CHANGELOG.md`.
