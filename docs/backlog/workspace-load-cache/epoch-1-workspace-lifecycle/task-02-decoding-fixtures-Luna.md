# E1/task-02 — decoding-fixtures

- Рекомендуемая модель: **Luna**.
- Рекомендуемый reasoning: **medium**.
- Примерная сложность: **2/5**.
- Риск ошибки: **средний** — Неполные byte-level fixtures могут не обнаружить изменение BOM или encoding при записи.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`, `проверка_целостности_данных`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E1/task-00](task-00-lifecycle-contract-Astra.md), [E1/task-01](task-01-semantic-dto-Sol.md).

## Цель

Закрепить decoding и round-trip контракт на небольших однозначных fixtures.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Только focused tests/test data для принятого mapper/writer; существующие WorkspaceWriteEncodingTests и fixtures Epoch 0.

## Пакет реализации до dispatch

- Producer: [E1/task-00](task-00-lifecycle-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-02.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Только fixtures/assertion helpers из packet; production types не добавляются.

### Файлы, которые можно менять

- RoslynMcpServer.Tests/Workspace/WorkspaceWriteEncodingTests.cs
- RoslynMcpServer.Tests/Workspace/SourceTextEncodingTests.cs
- Новые decoding fixture/test files по packet.

### Файлы, которые нельзя менять

- Services/
- Tools/
- Diagnostics/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- SourceTextEncoding.ResolveForWrite(SourceText? existing, SourceText? candidate, string? diskPath) → Encoding.
- PersistedPayload.FromText(string text, Encoding encoding) → byte[].
- DTO mapper/hydrate declarations и expected characters/BOM/bytes/error cases из task-00/task-01.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Проверки characters, encoding policy, BOM и bytes для ordinary и hydrate inputs; cases non-ASCII, отсутствующей/явной encoding и согласованных fallback errors.

## Проверки и evidence

V02 и decoding часть V07; тесты сверяют bytes после записи и следующий semantic result, а не только encoding label.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не менять writer policy или mapper schema ради зелёных тестов; сложные ambiguity случаи вернуть владельцу task-00.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
