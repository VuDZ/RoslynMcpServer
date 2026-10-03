# E4/task-08 — reuse-oracle-matrix

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Неполная повторная oracle matrix способна ошибочно подтвердить O5 для нового profile.
- Необходимые способности: `проектирование_проверок`, `семантика_roslyn_msbuild`, `анализ_зависимостей`, `независимая_проверка`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-07](task-07-membership-reuse-Astra.md), [E4/task-05](task-05-closed-subset-reuse-contract-Astra.md).

## Цель

Независимо проверить correctness нового O5 profile на content и membership изменениях.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Oracle/integration harness и fixtures; production paths task-06/task-07, original full equivalence/admission matrix.

## Пакет реализации до dispatch

- Producer: [E4/task-05](task-05-closed-subset-reuse-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-08.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Oracle fixture builders/results и reuse matrix reports; новых production types нет.

### Файлы, которые можно менять

- Новые/существующие oracle harness/test files — конечный manifest task-05.
- Fixture project/source/membership data и correctness reports по packet.

### Файлы, которые нельзя менять

- Services/
- Tools/
- Diagnostics/
- Profile grammar и cache schema
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Exact content/membership reuse entrypoints и observation results из task-05/task-06/task-07; independent fresh MSBuild runner из E0/task-04.
- Каждый fixture имеет before/after bytes/memberships/options/generator expected data и distinct reuse-vs-fallback/DTB assertions в packet.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Повторная full semantic/generated/admission matrix с отдельными reasons content/membership, positive zero full-DTB и unknown ordinary fallback. New PID и live sequence, affected instances/transitive graph, writes/build capability regressions.

## Проверки и evidence

V01–V18/V21/V23 применительно к новому subset; intended positives действительно reuse, negative cases не маскируются успешным ordinary load.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не объявлять функциональные fixtures public activation или performance успехом; numeric evaluation только после task-13 budget этапа.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
