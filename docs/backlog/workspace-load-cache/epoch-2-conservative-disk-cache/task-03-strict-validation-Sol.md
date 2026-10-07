# E2/task-03 — strict-validation

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Пропуск changed bytes или membership разрешит hydrate по устаревшему generation.
- Необходимые способности: `реализация_по_контракту`, `анализ_зависимостей`, `проверка_целостности_данных`, `управление_ресурсами`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-01](task-01-dependency-evidence-Sol.md), [E2/task-02](task-02-payload-serialization-Luna.md), [E2/task-00](task-00-cache-protocol-Astra.md).

## Цель

Проверить неизменность полного bounded dependency cone до hydrate.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Validation/probe helpers, membership scanning, path canonicalization; без publication и public mode changes.

## Пакет реализации до dispatch

- Producer: [E2/task-00](task-00-cache-protocol-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-03.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Strict request validator, bounded region enumerator и probe result types по task-00; profile grammar не расширять.

### Файлы, которые можно менять

- Services/Workspace/InputPathCanon.cs — только согласованная canonicalization delta.
- Новые strict validation/probe/region files и V10–V16 fixtures по packet.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Models/WorkspaceInputSession.cs
- Services/Analyzers/
- Tools/
- Codec schema и store ownership files
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Evidence capture/detector declarations task-01, Decode output task-02 → strict validation; exact types и consumed-byte stability ownership задаёт task-00.
- Validator result содержит compatibility/completeness/category reason и bounded exhaustion outcome; no hydrate/publication из validator.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Compatibility + strict byte hashes, positive/absent/negative-region checks, bounded membership, explicit probes, consumed-byte stability и точные miss reasons. Exhaustion/unreadable/incomplete дают whole ordinary fallback.

## Проверки и evidence

V10–V16: same size/mtime bytes change, add/delete/Remove, new irrelevant nonmember, absent Exists, ancestor assets/SDK, linked/junction cycle/retarget. Scan остаётся внутри доказанных regions.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не засчитывать truncated scan как hit, не заменять strict stat-проверкой и не сканировать всю монорепу ради отсутствующего evidence.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
