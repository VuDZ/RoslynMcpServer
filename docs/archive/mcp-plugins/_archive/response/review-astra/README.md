# Response: review-astra/README.md

## review-astra/R-01

### Reviewer claim

Рекомендованный семантический вход плагина — `GetPublishedSolutionAsync` — не применяет очередь disk watcher. Встроенные semantic-тулы вызывают `GetSanitizedPublishedSolutionAsync`, поэтому плагин и хост расходятся на только что сохранённом `.cs`.

### Related findings

Нет дубликата. Дополняет обещание README «тот же загруженный Solution».

### Problem assessment

`ACCEPT`

### Problem rationale

`GetPublishedSolutionAsync` возвращает `_solution` под lock и не вызывает `FlushDirtyDocumentsUnderLockAsync` (`SolutionManager.cs`, около 336–349). Watcher только ставит путь в очередь. Сброс очереди есть у `GetPublishedSolutionAfterDiskSyncAsync` и `GetSanitizedPublishedSolutionAsync`. `NavigationTools` берёт второй. Рецепт impact-тула в README и список входов в `authoring.md` называют первый. Общий экземпляр `SolutionManager` не равен общему снимку.

### Correction assessment

`MODIFY`

### Correction rationale

Новый API не нужен. Руководство и пример должны называть уже существующий вход встроенных semantic-тулов: `GetSanitizedPublishedSolutionAsync`. Рядом явно: `GetPublishedSolutionAsync` очередь диска не применяет; `GetPublishedSolutionAfterDiskSyncAsync` применяет, но не снимает заглушки битых analyzer reference. Выбор raw/sanitized — это выбор между этими двумя методами, не третья поверхность.

Приёмка: событие диска, затем вызов плагина без предварительного встроенного тула, который сам очистит очередь. Это принимается.

### Evidence

Обсуждение: плагин анализирует тот же загруженный `Solution`, что встроенные тулы, и не вызывает их методы. Код: `Services/SolutionManager.cs` (три метода выше); `Tools/NavigationTools.cs` вызывает `GetSanitizedPublishedSolutionAsync`. Спека: README, пример `GetPublishedSolutionAsync`; `authoring.md`, раздел «Чем пользоваться».

### Classification

`ARCHITECTURE DEFECT`

### Severity

High у ревьюера. Совпадает: тихий stale-анализ — ровно обещанный сценарий test-impact.

### Required outcome

Рекомендованный анализ плагина идёт тем же снимком, что semantic-тулы хоста, и спека перестаёт называть сырой `GetPublishedSolutionAsync` этим входом.

### Proposed specification change

README (пример и «кухня») и `authoring.md` («Чем пользоваться»): анализ, который должен совпасть со встроенными тулами, вызывает `GetSanitizedPublishedSolutionAsync`. Отдельным абзацем — что делают два других метода. Эпоха 5: тест, где файл изменён на диске, очередь не сброшена чужим тулом, метод шаблона или тестового тула видит новый текст.

### Downstream consequences

API плагина: другой метод `SolutionManager`, не новый тип. Тесты эпохи 5. Совместимость: до реализации загрузчика внешних плагинов нет.
