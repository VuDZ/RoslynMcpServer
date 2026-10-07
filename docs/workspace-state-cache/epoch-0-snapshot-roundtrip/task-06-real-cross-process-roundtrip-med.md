# E0/task-06 — Реальный round-trip и .cs edit

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E0/task-05](task-05-isolated-hydrate-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Проверить ordinary capture → disk DTO → новый PID → hydrate → ожидаемый semantic result на выбранном реальном контроле.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Integration harness и tests; использовать public workspace operations, не создавать synthetic expected DTO вместо ordinary capture; production cache не включать.

## Конкретный результат

- Cross-process integration tests и evidence/task-06-roundtrip.md
- Отдельный scoped edit существующего .cs с encoding/shared-path assertions.

## Проверки

- Сверка project/document inventories, symbol definition и значимого compile option
- UTF/BOM policy и linked membership сохраняются после edit
- unsupported generator/TFM cases честно отказаны
- zero-test/not-run не pass.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-001, P-002, P-010**; [change ledger](../change-ledger.md).

Полный cross-process round-trip не заменён capability inspection. Сверяются public options/reference properties в accepted U-001 границе, три уровня properties и actual role/owner/producer parity. XML-doc исключение H-001 проверяется отдельно по принятому contract; strong-name exclusions пока не выбраны.

## Решение владельца H-001

[H-001](../human-decisions.md): XML documentation provider/text metadata/DLL references
вне MVP equivalence; source comments загруженных проектов и остальные обязательные
metadata/options facts сохраняются. Это объявленное исключение, не silent loss.
Documentation-dependent consumers получают ordinary route либо явный отказ до execution/
side effects. Применимые controls/compare key отражают эту границу; strong-name
решение U-001 всё ещё требуется, execution/review statistics остаются незаполненными.
