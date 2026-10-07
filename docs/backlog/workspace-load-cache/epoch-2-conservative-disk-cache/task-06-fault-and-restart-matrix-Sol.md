# E2/task-06 — fault-and-restart-matrix

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **3/5**.
- Риск ошибки: **высокий** — Неполная fault matrix оставит crash, paused-reader или restart ошибки незамеченными.
- Необходимые способности: `проектирование_проверок`, `анализ_конкурентности`, `управление_ресурсами`, `независимая_проверка`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-05](task-05-load-capture-integration-Astra.md), [E2/task-00](task-00-cache-protocol-Astra.md).

## Цель

Проверить реальные store/load сценарии независимо от unit happy path.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Integration harness и fixtures; production paths task-05. Не менять контракт fallback или manager ради упрощения harness.

## Пакет реализации до dispatch

- Producer: [E2/task-00](task-00-cache-protocol-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-06.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Integration process/fault fixture helpers и report types по packet; production types не добавляются.

### Файлы, которые можно менять

- Новые restart/store fault integration test files и fixture host controls из packet; reports.

### Файлы, которые нельзя менять

- Services/
- Tools/
- Diagnostics/
- Store/load schemas
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Accepted store writer/reader lease и load result declarations из task-00/task-04/task-05.
- Process harness entrypoints должны запускать независимые PID, управлять kill/pause/failed I/O и проверять actual base/DTB; expected reasons и inject seams задаёт task-00, production seam changes возвращаются его owner.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

V10–V18/V23 evidence: новый PID positive hit vs fallback, source/membership edit, no-op build/build/edit+build+restart, process kill, paused reader, replaced DLL и filesystem failures. Raw attempts и reasons.

## Проверки и evidence

Каждый intended positive показывает base=disk и zero DTB; unsupported/unknown — отдельные negative cases. Fault cases сохраняют допустимую old session/store и позволяют обычный load.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не выдавать zero-test/not-run за pass; workload performance verdict отложить до утверждения U-ARB-03 budgets.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
