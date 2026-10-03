# E0/task-04 — fresh-msbuild-equivalence

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Неполный oracle может ошибочно подтвердить эквивалентность production hydrate.
- Необходимые способности: `семантика_roslyn_msbuild`, `проектирование_проверок`, `независимая_проверка`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E0/task-01](task-01-hydrate-host-spike-Sol.md), [E0/task-02](task-02-dependency-admission-spike-Astra.md), [E0/task-03](task-03-oracle-fixtures-Luna.md), [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).

## Цель

Сравнить candidate host с независимой свежей MSBuild загрузкой.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Oracle harness и focused tests; fixtures task-03. Hydrate не использует object graph того же oracle как доказательство равенства.

## Пакет реализации до dispatch

- Producer: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-04.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Oracle comparison/result types и independent runner по packet; новых production types нет.

### Файлы, которые можно менять

- Только oracle harness/test files и reports из packet; experiment entrypoints task-01 используются через принятые interfaces.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Analyzers/
- Tools/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Independent fresh-load и hydrate runner declarations из packet: distinct host/session, compare graph/options/memberships/generated outputs/diagnostics.
- Result contract отличает positive equivalence, negative admission, failed/not-run; full generated output access + DTB counter указаны точно.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Отчёт V01–V06 с графом, options, memberships, decoding, references, полным generated set/texts/diagnostics и semantic assertions. Отдельно positive equivalence, negative admission, failed/not-run.

## Проверки и evidence

Независимые загрузки и sessions, точные inner-instance contexts; отсутствие required generated output — failed/not-run. Не ограничиваться числом documents.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не считать unsupported case positive equivalence и не записывать generated/source texts в будущий cache payload.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
