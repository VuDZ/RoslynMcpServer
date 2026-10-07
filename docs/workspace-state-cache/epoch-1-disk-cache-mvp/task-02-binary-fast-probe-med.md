# E1/task-02 — Облегчённая проверка бинарей

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: высокий — ошибка проверки может разрешить reuse изменённой зависимости сверх явно принятого blind spot эвристики.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E1/task-00](task-00-input-manifest-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Добавить bounded binary validation с timestamp/size и хешированием по размеру.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Только binary probe и tests; initial threshold 16 MiB, head/tail 64 KiB — proposed defaults, результаты benchmark могут обосновать последующий отдельный пересмотр.

## Конкретный результат

- Binary stamp/probe и focused edge cases
- evidence/task-02-binary-policy.md с фактическими bytes read и признанным blind spot.

## Проверки

- Small binary full hash
- large binary timestamp+size+head/tail hash
- physical file проверяется один раз
- overlaps/short file корректны
- stat/read/stat нестабильность даёт miss
- mutation середины с preserved metadata является documented undetected case, не доказательством неизменности
- текущие analyzer execution hashes не ослабляются.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
