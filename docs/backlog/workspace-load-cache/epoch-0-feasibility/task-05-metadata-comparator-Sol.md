# E0/task-05 — metadata-comparator

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **medium**.
- Примерная сложность: **3/5**.
- Риск ошибки: **средний** — Нечестный metadata comparator исказит coverage или вывод о пользе отдельного режима.
- Необходимые способности: `семантика_roslyn_msbuild`, `проектирование_проверок`, `измерение_производительности`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md), [E0/task-04](task-04-fresh-msbuild-equivalence-Sol.md).

## Цель

Проверить metadata fast-open как отдельный O7 comparator.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Изолированный comparator/benchmark harness и документация evidence; не менять full-source profile или public tools.

## Пакет реализации до dispatch

- Producer: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-05.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Изолированный metadata comparator/result types по packet; production tool modes не вводятся.

### Файлы, которые можно менять

- Comparator experiment/test files, measurement manifest и reports по packet.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Tools/
- Services/Analyzers/
- docs/backlog/workspace-load-cache/cache-contract.md
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Full-source oracle entrypoint из task-04 и отдельный metadata runner/readiness/coverage outcome из packet task-00.
- Benchmark runner arguments/budget authority и stale/missing/corrupt DLL fallback contract задаёт producer.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Отдельные режим, scope, readiness/coverage, fallback для stale/corrupt DLL и протокол сравнения с full-source oracle. Числа только при заранее утверждённом budget соответствующего эксперимента.

## Проверки и evidence

Проверить fresh/stale/missing/corrupt references, доступность требуемого semantic call; отделить metadata результат от full-source equivalence.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не объявлять metadata успех cache go; недоступный workload/budget отмечать deferred, не выдумывать порог.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
