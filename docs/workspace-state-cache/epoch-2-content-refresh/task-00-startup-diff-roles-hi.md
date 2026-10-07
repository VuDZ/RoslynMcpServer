# E2/task-00 — Роли стартового diff и dirty owners

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E1/task-10](../epoch-1-disk-cache-mvp/task-10-mvp-benchmark-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Классифицировать offline changes как content-only, metadata-refresh или graph/membership dirty по единой карте ролей.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Manifest diff classifier и tests; live event classifier используется как основание, не создаётся независимый общий input index.

## Конкретный результат

- Startup diff decisions и portable owner mapping
- evidence/task-00-diff.md с примерами shared input/import/new file.

## Проверки

- Все owners linked/global inputs отмечены
- evaluation role имеет приоритет над text
- source bytes-dependent custom target вне content-only
- unreadable/missing/new source/config/unknown role → fallback
- existing pure source text допускает refresh.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-010, P-013, P-014**; [change ledger](../change-ledger.md).

Known Source role не достаточна: проверяемый finite content-only predicate доказывает независимость changed bytes от evaluated graph/options вне refresh. Unknown/non-admitted target/import/condition → graph dirty/fallback. Для metadata отдельный predicate сохранения relevant resolution/inventory; same path/identity недостаточны. Restored roles/owners/producers имеют тот же смысл, без Unknown→UserInput.
