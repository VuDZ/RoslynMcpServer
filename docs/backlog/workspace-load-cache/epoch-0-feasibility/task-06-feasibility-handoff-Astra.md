# E0/task-06 — feasibility-handoff

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ошибочный handoff может разрешить implementation без доказанного host/admission contract.
- Необходимые способности: `независимая_проверка`, `архитектурное_мышление`, `анализ_зависимостей`, `семантика_roslyn_msbuild`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E0/task-04](task-04-fresh-msbuild-equivalence-Sol.md), [E0/task-05](task-05-metadata-comparator-Sol.md).

## Цель

Независимо принять технические результаты feasibility и определить допустимый следующий шаг.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Diff/evidence всех заданий эпохи и handoff-template; это review и отчёт, исправления возвращаются автору соответствующего задания.

## Конкретный результат

handoff.md с capability/semantic/dependency таблицами, точными limits, пятью verdicts и списком недоказанных обязательств. Подтвердить выбранные решения и допустимость production lifecycle работ отдельно от public activation.

## Проверки и evidence

Перепроверить V01–V06 и host write evidence, provenance/generator completeness; выполнить общую финальную validation для изменённого code/test набора.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не закрывать эпоху по fixture-only go, не считать O4/O5 выполненными; U-ARB-03 оставить deferred до реализации и запретить activation без согласованных budgets.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Независимость и ограничения исполнителя

До запуска записать review session ID и design/implementation author session IDs
проверяемого scope; совпадение запрещает dispatch независимо от модели Astra.
Приёмщик не меняет production implementation или design, который принимает.
Must-fix возвращается соответствующему автору; повторный review остаётся у этой
приёмочной сессии. Координатор выполняет final validation и фиксирует verdicts.
