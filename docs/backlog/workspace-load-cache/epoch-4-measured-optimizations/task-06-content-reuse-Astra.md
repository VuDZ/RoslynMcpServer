# E4/task-06 — content-reuse

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Неправильный content reuse изменяет semantic результат или пропускает invalidation affected instances.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `анализ_конкурентности`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-05](task-05-closed-subset-reuse-contract-Astra.md).

## Цель

Реализовать без полного DTB только доказанный content-change маршрут.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Reuse planner/hydrate/update helpers и production cache integration; один владелец общих load/publication paths.

## Конкретный результат

Reuse валидного graph/evidence при допустимых изменениях source bytes, обновление всех memberships/options contexts, strict revalidation остальных inputs и prepare/gate. Cache capture остаётся по принятому schedule.

## Проверки и evidence

Content rules task-05 + V01–V10/V14/V21/V23; fresh oracle generated set/text/diagnostics, изменённые defines/context, unknown evaluation dependency → ordinary fallback до publish.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не переносить content rule на membership и не считать unchanged capture доказательством нового profile.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
