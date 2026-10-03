# E1/task-05 — overlay-admission

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Неверное provenance/admission сопоставление может исполнить неподтверждённый analyzer или обойти запрет.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `управление_ресурсами`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E1/task-03](task-03-production-hydrate-Sol.md).

## Цель

Обеспечить base/overlay separation и новый session-bound admission при hydrate.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Services/Analyzers и согласованное prepare wiring; task-04 и task-05 не правят общий manager одновременно.

## Конкретный результат

Recheck original DLL/content hash/SDK/pack identity, новый LoadSessionId и mapping в текущие ProjectId перед существующим gate. Sticky on/off/omitted/reset/restart и banned/unavailable semantics сохранены.

## Проверки и evidence

V08/V14/V23, полный generator oracle; unreadable/mismatch/ambiguous identity даёт выбранный base-without-overlay результат. Exact inverse до persistence удаляет shadow refs.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не подавать disk snapshot в gate, не считать AssemblyVersion checksum и не обещать unload на reset.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
