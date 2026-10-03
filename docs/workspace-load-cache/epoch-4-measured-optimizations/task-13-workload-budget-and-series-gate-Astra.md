# E4/task-13 — workload-budget-and-series-gate

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Ошибочный series verdict может разрешить activation без утверждённых budgets или закрытия обязательств.
- Необходимые способности: `независимая_проверка`, `архитектурное_мышление`, `анализ_зависимостей`, `измерение_производительности`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Acceptance scope: основной маршрут E0–E3 и 4C; 4A/4B/4D не входят.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-16](task-16-workload-budget-and-performance-Sol.md), [E4/task-11](task-11-o5-reuse-acceptance-Astra.md), [E3/task-06](../epoch-3-live-consistency/task-06-live-consistency-acceptance-Astra.md).

## Цель

Независимо проверить U-ARB-03 evidence и готовность серии/activation после workload/performance task-16.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Все основные handoffs, UNRESOLVED, принятые workload-budget.md и performance-report.md из task-16, подтверждение владельцем budgets до измерений и verification. Исполнитель пишет только series-handoff.md/статус; не проектирует workload и не исправляет production код. До task-16 задача deferred.

## Конкретный результат

series-handoff.md: independent verdict по U-ARB-03 и O1–O8, пять отдельных decision verdicts, limits/deferred пункты. Проверить даты и authority budget approval, воспроизводимость workload и raw результатов task-16. Activation verdict этой задачи относится только к указанному в шапке основному scope. Optional 4A/4B/4D получают собственные verdicts в task-12/14/15; их готовность или activation здесь не утверждаются.

## Проверки и evidence

Новый PID + first useful semantic, disabled/miss capture/disk hit/RAM/edit restart/no-op build/edit build restart/live; raw attempts, median/p95, DTB/stages/bytes/memory/hit-rate/miss overhead. Main CI/build на final code перед acceptance выполняет координатор; task-13 проверяет outputs. Must-fix возвращаются автору task-16/соответствующей реализации.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

OrchardCore/Roslyn не обязательны; BTCPayServer не объявлять positive supported автоматически. Если текущий profile его отвергает, сохранить negative case и предложить совместимый corpus/явное scope решение. Не выбирать thresholds после измерений и не активировать feature одной записью handoff.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Независимость и ограничения исполнителя

До запуска записать review session ID и design/implementation author session IDs
проверяемого scope; совпадение запрещает dispatch независимо от модели Astra.
Приёмщик не меняет production implementation или design, который принимает.
Must-fix возвращается соответствующему автору; повторный review остаётся у этой
приёмочной сессии. Координатор выполняет final validation и фиксирует verdicts.
