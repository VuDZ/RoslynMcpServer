# Response: review-ds/epoch-2-manifest-and-paths.md

## review-ds/E2-01

### Reviewer claim

Неверный тип `plugins` не пишется в `ParseFailures`. `ParseDocument` бросает `JsonException` мимо `try`, хост падает до `PluginStartup` и до строки `plugin skipped`.

### Related findings

Duplicate of `review-astra/E2-01`.

### Problem assessment

`ACCEPT`

### Problem rationale

Тот же код, что у astra: `TryReadFile` ловит только `ReadAllText`, `ReadString` бросает `JsonException`, `LoadFromDefaultLocations` в `Program.cs` не обёрнут. Обещание эпохи «как у других ключей» описывает падение, а не `ParseFailures`. Сценарий `"plugins": "C:\\..." ` и `"plugins": [10]` — ровно текст эпохи 2.

### Correction assessment

`ACCEPT`

### Correction rationale

Совпадает с ответом на `review-astra/E2-01`: ошибка типа становится `ParseFailures`, хост продолжается, `ParseDocument` внутри `try`. Починка задевает и старые ключи. Отдельный вердикт здесь не ослабляет тот: независимое совпадение повышает уверенность, но доказательство — код `RoslynMcpFileSettings`, не число ревьюеров.

### Evidence

`Config/RoslynMcpFileSettings.cs`. `Program.cs`. Эпоха 2, пункт 2. README: причина пропуска, сервер жив.

### Classification

`ARCHITECTURE DEFECT`

### Severity

Medium у ревьюера. В кластере беру High, как у `review-astra/E2-01`: падает процесс.

### Required outcome

Тот же, что у `review-astra/E2-01`.

### Proposed specification change

Одна правка эпохи 2 на оба finding. См. `P-008` в [`summary.md`](../summary.md).

### Downstream consequences

Те же: мягкий отказ на неверном типе любого известного ключа.
