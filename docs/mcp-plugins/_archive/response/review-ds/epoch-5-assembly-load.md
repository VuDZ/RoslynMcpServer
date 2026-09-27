# Response: review-ds/epoch-5-assembly-load.md

## review-ds/E5-01

### Reviewer claim

Исключение из `Register` помечает плагин пропущенным, но фабрики уже сидят в `IServiceCollection`. После `Build` SDK публикует их в `tools/list`. Третьего состояния в результате нет, отчёты info и список тулов расходятся.

### Related findings

Duplicate of `review-astra/E5-02` по проблеме. Correction расходится: здесь допускается «загружен частично», у astra — только commit после успеха.

### Problem assessment

`ACCEPT`

### Problem rationale

`AddToolsFrom` регистрирует фабрики в коллекции сервисов, из которой `GetServices<McpServerTool>()` наполняет runtime-коллекцию. Эпоха 5 передаёт в загрузчик живой `IServiceCollection` и отказывается откатывать. Сценарий «успел добавить тулы, потом бросил» оставляет `impact_*` в списке при статусе skipped. Это ломает и изоляцию, и `get_mcp_server_info`.

### Correction assessment

`MODIFY`

### Correction rationale

Двухфазный перенос принимается: контекст копит регистрации, в DI хоста они попадают после нормального возврата из `Register` и после успешного создания SDK-тулов.

Третье состояние «загружен частично» отклоняется. Оно сохраняет тулы в `tools/list` и лишь честнее подписывает аварию. Требование серии — битый плагин не публикует тулы и не роняет остальных. Частичная публикация этому не удовлетворяет. Имя тула после отката свободно.

Побочные эффекты кода плагина вне DI по-прежнему не откатываются. Это предел, не повод оставлять дескрипторы.

### Evidence

Эпоха 1, запись фабрик в `Services`. Эпоха 5, «откатывать нечем» и бинарный результат загружен/пропущен. `McpToolCatalogTests.MeasureSurface`: коллекция читается из сервисов `McpServerTool` после резолва options. README: один битый плагин не роняет сервер.

### Classification

`ARCHITECTURE DEFECT`

### Severity

Medium у ревьюера. В кластере с `review-astra/E5-02` беру High.

### Required outcome

Статус skipped означает: тулов этого плагина в `tools/list` нет.

### Proposed specification change

Эпоха 5, одна транзакция с `review-astra/E5-02`. Формулировку «загружен частично» не вводить.

### Downstream consequences

Контракт `Register`: до успешного возврата общий контейнер не изменён этим плагином.
