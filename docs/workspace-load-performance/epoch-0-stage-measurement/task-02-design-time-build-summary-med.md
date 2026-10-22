# D/E0/task-02 — Сводка design-time build из binlog

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Grok 4.7.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — правка replay provenance; изменение отбора событий может изменить analyzer admission evidence.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_производительности`, `проектирование_проверок`.
- Статус: **partially implemented, 2026-10-22** — по прямому запросу владельца добавлена
  разбивка design-time build и name-based поиска по проектам; офисные прогоны
  1.5.8/1.5.9 получены, результаты записаны в task-08. Этот ограниченный scope принят 2026-10-22 после Release build, main CI
  1409/0/0, AnalyzerLifecycle 13/0/0 и end-to-end; validation —
  [отчёт](evidence/project-timings-2026-10-22.md). Полное закрытие task-02 и зависимость
  от структурированных стадий task-01 остаются pending; task-01 этим изменением не реализована.
- Depends on: [task-01](task-01-load-stage-timings-med.md).
- Модель, реализовавшая задачу: Codex (GPT-6), ограниченный scope 2026-10-22.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: [проектные тайминги, 2026-10-22](evidence/project-timings-2026-10-22.md).

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

## Офисный результат, 2026-10-22

142 проекта; в 1.5.9 replay Complete, оба binlog прочитаны.
Второй format 27 содержит 49 project starts; в сводке 122 logged evaluations.
Это не доказывает полноту root evaluation или внутреннюю разбивку open.
Первый поиск теперь виден по проектам: private_project 12–13 с.
[Результаты](task-08-source-search-and-office-results-med.md);
[дальнейший профиль поиска](task-09-first-search-io-and-private-project-profile-hi.md).
