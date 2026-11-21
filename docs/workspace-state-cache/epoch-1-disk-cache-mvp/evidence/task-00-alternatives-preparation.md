# Подготовка альтернатив E1/task-00

Дата: **2026-10-09**, Europe/Moscow. Scope: docs-only оформление трёх задач
по запросу владельца после accepted partial S-002. Исполнитель: Codex;
точный runtime model ID/reasoning не подтверждены инструментами.
Статус подготовки: **prepared / planned tasks; независимое ревью новых задач не проведено**.

Execution update 2026-10-09: [task-00.1 accepted, round 2](task-00.1-validation.md)
для bounded public collector и retention/ABA counterexamples, source partial /
reusable capture insufficient. [Task-00.3 accepted, round 1](task-00.3-validation.md)
2026-10-09 для docs-only отрицательного contract audit; draft not-viable на текущем
evidence и не применён. Task-00 остаётся blocked. [Task-00.4 accepted, round 2](task-00.4-validation.md),
2026-10-09 — bounded ordinary analysis/profiling, honest no-go; оптимизация
и отсрочка disk restore не приняты.
Ниже сохранён исходный отчёт подготовки до выполнения исследования.

- [Task-00.1](../task-00.1-supplemental-manifest-source-hi.md): один isolated
  supplemental collector/capture scenario через public API, с отдельным source
  и consumed-generation proof и honest partial/no-go outcome.
- [Task-00.3](../task-00.3-manifest-contract-scope-hi.md): evidence-backed draft
  границы freshness/restore inputs для выбора владельцем; канон не применяется автоматически.
- [Task-00.4](../task-00.4-ordinary-load-alternative-med.md): один bounded ordinary
  optimization proposal и последствия возможной отсрочки disk restore.

Вариант .2 с изменением/собственным fork Roslyn BuildHost исключён владельцем;
задача .2 не создавалась. Номера .1/.3/.4 соответствуют обсуждённым вариантам.
Все три исследования зависят от accepted E0/task-07 и E1/task-11, а не от
реализации blocked task-00 или друг от друга. У каждой свои scope/report/checks.
Новые задачи имеют planned status, recommended model/reasoning/risk/capabilities
и незаполненный учёт будущего исполнения/ревью как остальные задачи эпохи.

Обновлены [task-00](../task-00-input-manifest-hi.md),
[индекс E1](../README.md), [сводка серии](../../README.md) и
[индекс docs](../../../README.md). Текущее число task files: **38**, в E1 **15**;
исторические 35-task preparation/review measurements не переписаны.
Task-00 остаётся blocked; accepted S-002 partial outcome и исходные requirements,
dependencies остальных задач, production/public activation сохранены.

Docs validation: **8 документов / 207 local links, 0 missing links**;
task headers/index membership и planned statuses проверены. Depends on graph:
**38 tasks / 59 edges, 0 cycles**; в E1 **15 tasks**, .2 не создана.
`git diff --check` выполнен; в новых документах нет trailing whitespace.
Это consistency check автора, не независимый review round и не исполнение задач.
Production C#/test/config не менялись: build/main/SourceStructure/AnalyzerLifecycle
для этого docs-only изменения **not-run / not required** по execution rules.
Pre-existing dirty files и локальный task-00-manifest.md сохранены.
Коммитов в этой подготовке нет.
