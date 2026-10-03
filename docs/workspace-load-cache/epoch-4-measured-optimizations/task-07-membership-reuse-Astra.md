# E4/task-07 — membership-reuse

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Ошибка membership/TFM/transitive invalidation может опубликовать неполный или несогласованный граф.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `анализ_конкурентности`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-06](task-06-content-reuse-Astra.md).

## Цель

Реализовать closed-subset membership reuse и точную instance invalidation.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Bounded membership evaluator/reuse planner и production integration; grammar/evidence из task-05, content path task-06.

## Конкретный результат

Поддержанные add/delete/Include/Exclude/Remove/conditions, linked membership/inner-TFM edges, negative dependencies и transitive invalidation. Unknown/unsupported/request-incomplete целиком fallback до publication.

## Проверки и evidence

V03/V06/V11a–V11d/V12/V16/V21: новые/удалённые файлы, условный Remove, linked/shared paths, absent import present, irrelevant nonmember; oracle сравнивает весь affected graph.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не считать file list checksum полной моделью, не публиковать поддержанный кусок request и не сканировать всю монорепу.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
