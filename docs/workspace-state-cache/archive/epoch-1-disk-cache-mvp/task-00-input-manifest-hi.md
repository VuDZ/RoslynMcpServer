# E1/task-00 — Manifest значимых входов

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md)
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
Перед C# правкой прочитать [code style](../../../code-style.md) целиком.

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
проверки выполняются по [AGENTS.md](../../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
