# E1/task-00.4 — Ordinary load при отложенном disk restore

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — план может обещать неподтверждённое ускорение или незаметно изменить lifecycle/freshness.
- Необходимые способности: `анализ_производительности`, `анализ_зависимостей`, `планирование_реализации`.
- Статус: **accepted, round 2, 2026-10-09 — bounded ordinary analysis/profiling, honest no-go для выбора оптимизации; отсрочка disk restore не принята**. [Review/validation](evidence/task-00.4-validation.md).
- Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md), [E1/task-11 — S-002](task-11-raw-import-restore-evidence-hi.md)
- Модель, реализовавшая задачу: Codex implementer subagent, inherited runtime; точный model ID/reasoning not confirmed.
- Модели, проводившие ревью: Codex independent reviewer subagents, две отдельные сессии; точный runtime model ID/reasoning not confirmed.
- Количество раундов ревью: 2.
- Количество исправлений после ревью: 1 документальное уточнение membership warning, подтверждено round 2; 0 must-fix fixes / 0 открытых must-fix.
- Отчёт о реализации, ревью и validation: [Analysis/profiling](evidence/task-00.4-ordinary-alternative.md), [round 1](evidence/task-00.4-review-round-1.md), [accepted round 2](evidence/task-00.4-review-round-2.md), [final validation](evidence/task-00.4-validation.md).

## Цель

Предложить один небольшой следующий шаг улучшения ordinary load, если владелец
решит отложить disk restore из-за стоимости source/generation proof.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md), [README серии](../README.md),
[правила исполнения](../execution.md), [benchmark protocol](../benchmark.md),
[task-00](task-00-input-manifest-hi.md),
[accepted S-002 / validation](evidence/task-11-recheck-validation.md) и outputs
Depends on обязательны. Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Bounded analysis/measurement и предложение следующей implementation задачи.
Использовать current ordinary pipeline и пригодные E0 baseline measurements;
чётко отделить new-process load, OS file cache и existing RAM reuse. Если данных
недостаточно, снять focused profiling одного заранее выбранного реального request
без изменения его source/references ради результата.

Выбрать один подтверждённый hotspot и описать небольшой способ его уменьшения,
измеряемый expected outcome и correctness regressions. Числа без замера не
выдавать за эффект оптимизации. Production implementation выбранной оптимизации
в эту задачу не входит; её scope будет отдельной задачей после выбора владельцем.

Изменение/собственный fork Roslyn BuildHost исключён владельцем 2026-10-09.
Не добавлять disk hit, обход freshness/write/analyzer bans или новые defaults.
Не удалять E0 artifacts и не возобновлять большую backlog-программу исследований.
Отсрочка E1/E2 и её downstream последствия пока только предложение, не новые статусы.

## Конкретный результат

- `evidence/task-00.4-ordinary-alternative.md`: наблюдаемые ordinary stages/hotspot,
  версии/контроль/режимы и ссылки на raw measurements либо явные not-measured поля.
- Один bounded implementation proposal с затрагиваемыми seams, метрикой приёмки,
  рисками и нужными regressions; если hotspot не подтверждён — honest no-go.
- Последствия возможной отсрочки disk restore: сохраняемые E0 результаты,
  что доступно независимо от manifest, какие E1/E2/E3 outcomes останутся недоступны
  и какие task/index статусы потребуется изменить после решения владельца.

## Проверки

- Метрика — time-to-first-useful с заранее выбранным semantic result и compare key;
  startup/load/query/capture затраты не смешиваются и не скрываются.
- Существующие SamplePlugin ordinary numbers и isolated restore numbers различаются;
  restore benchmark не выдаётся за production disk hit или эффект ordinary оптимизации.
- Новые измерения указывают n, successes/failures/timeouts и limitations; прежние
  acceptance evidence не переписываются под новую гипотезу.
- RAM omitted matcher, merged globals, sticky overlay, diagnostics, cancellation,
  resource ownership и write boundaries отражены в validation scope предложения.
- Task-04 host seam, если рассматривается, не объявляется ускорением без evidence
  и не получает автоматическую production activation.
- Отсрочка disk cache не засчитывается выполнением manifest/E1; task-00 остаётся
  blocked до отдельного решения и дальнейшей применимой приёмки.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Docs-only план требует consistency/link/dependency/diff checks; изменения profiling
harness/tests требуют полной применимой validation. После ревью/validation обновить
дату, принятый analysis scope, evidence и фактические модели в шапке и README эпохи.
Принятие исследования не означает выбор ordinary маршрута или приёмку оптимизации.
