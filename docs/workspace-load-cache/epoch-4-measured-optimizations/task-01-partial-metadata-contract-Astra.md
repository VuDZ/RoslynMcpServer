# E4/task-01 — partial-metadata-contract

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Неверный partial/tool contract может представить неполный ответ как полный или разрешить unsafe refactoring.
- Необходимые способности: `архитектурное_мышление`, `семантика_roslyn_msbuild`, `согласованность_компонентов`, `анализ_зависимостей`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-00](task-00-direction-gates-Astra.md), [E1/task-07](../epoch-1-workspace-lifecycle/task-07-lifecycle-acceptance-Astra.md).

## Цель

Определить canonical scope и честную tool coverage для 4A.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Документ design + inventory semantic/refactoring/test/CLI tools; полный production lifecycle как вход.

## Конкретный результат

Canonical roots и отдельные RAM/disk identities для partial/metadata, explicit expand. Для каждого tool: partial result + coverage либо refuse/full-scope required; get_test_list syntax coverage, CLI disk target и ambiguity rules. Metadata comparator отдельный.

## Проверки и evidence

V24/V24a/V24b/V25 план проверок для full/partial A/partial B, двусмысленного project/binary и stale/corrupt DLL; все public compatibility изменения названы.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не допускать auto-expand, first-match ambiguity или completeness refactoring по одному metadata hint.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E4/task-02: `epoch-4-measured-optimizations/design/implementation-packet-task-02.md`.
