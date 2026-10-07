# E1/task-00 — Manifest значимых входов

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md), [E1/task-11 — S-002](task-11-raw-import-restore-evidence-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Определить практический набор inputs/regions, portable owners и profile boundaries для первого disk hit.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Manifest и capture evidence; переиспользовать WorkspaceInputMapBuilder, current provenance/binlog ordinary capture; без общей модели произвольных MSBuild tasks.

## Конкретный результат

- Manifest DTO/capture adapter и evidence/task-00-manifest.md
- Positive paths, standard known-absent candidates, membership regions, binary identities и version/resolution fingerprint для выбранного контроля.

## Проверки

- Source/additional/config/shared paths имеют всех owners
- actual imports/restore inputs не подменены одними csproj
- explicit obj/external inputs включены
- unsupported custom input/TFM/import имеет whole-request fallback
- минимум один реальный request поддержан.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-004, P-005, P-006, P-010, P-011, P-012**; [change ledger](../change-ledger.md).

До принятия adapter требуется S-002. Source для каждого import/restore category, instance binding/completeness boundary и incomplete→unsupported задаются по actual evidence; текущая map/snapshot не считаются closure. Captured graph и manifest имеют evidence одной consumed generation, не post-load hash binding. Profile фиксирует environment current comparison, portable health/eligibility и occurrence roles. Applicable ancestor config known absence/project-linked boundaries обязательны до первого hit.
