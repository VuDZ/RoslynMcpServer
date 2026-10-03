# E4/task-05 — closed-subset-reuse-contract

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Недоказанный closed subset способен разрешить reuse при скрытой evaluation зависимости.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `согласованность_компонентов`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-00](task-00-direction-gates-Astra.md), [E3/task-06](../epoch-3-live-consistency/task-06-live-consistency-acceptance-Astra.md).

## Цель

Доказать closed subset для O5 и задать отдельные content/membership rules.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

sdk-project-v1 evidence, production hydrate, shared input state и current target evaluation; design прежде реализации reuse.

## Конкретный результат

Exact supported Include/Exclude/Remove/conditions grammar и detector completeness, dependency independence, inner-instance mapping/transitive invalidation, negative imports/linked/target probes. Unknown → whole ordinary load до publication. План fresh-MSBuild oracle по каждому rule.

## Проверки и evidence

V03–V06/V10–V16/V21/V23; source byte change, membership add/delete, conditional Remove, combined evaluation roles, custom target и unknown expressions имеют точные expected outcomes.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не делать общий MSBuild interpreter, не расширять subset по одному удачному fixture и не принимать partial publication при unknown.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E4/task-08: `epoch-4-measured-optimizations/design/implementation-packet-task-08.md`.
