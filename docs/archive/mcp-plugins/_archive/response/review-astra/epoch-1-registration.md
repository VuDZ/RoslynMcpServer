# Response: review-astra/epoch-1-registration.md

## review-astra/E1-01

### Reviewer claim

Проверка имени и регистрация фабрики не равны принятию метода SDK. Ошибка материализации (`McpServerTool.Create` / `McpServerOptionsSetup`) случается после `Register` и роняет общий старт, а не один плагин.

### Related findings

Overlaps with `review-ds/E1-01` (когда коллекция вообще наполняется). Overlaps with `review-astra/E5-02` и `review-ds/E5-01`: фабрики уже лежат в общем `IServiceCollection`, когда SDK их отвергает.

### Problem assessment

`ACCEPT`

### Problem rationale

`McpToolRegistry` кладёт отложенную фабрику. `McpServerTool.Create` выполняется внутри неё. Проба ревьюера на MCP 1.3.0: открытый generic с допустимым именем проходит `Register` и `Build`, а `IOptions<McpServerOptions>.Value` бросает `ArgumentException: Open generic methods are not supported`. `Program.cs` не ловит это как ошибку одного плагина. Эпоха 5 считает границу закрытой, когда `Register` вернулся. Это неверно: публикация тула в список SDK ещё впереди.

### Correction assessment

`MODIFY`

### Correction rationale

Отдельный глобальный catch вокруг всего старта MCP спрячет поломку каталога хоста. Нужна узкая граница: фабрики плагина вызываются до того, как плагин объявлен загруженным; исключение SDK отбрасывает только его тулы и не публикует их в общую коллекцию. Это та же транзакция, что в `review-astra/E5-02`, а не второй механизм. Отрицательный тест с неподдерживаемой сигнатурой и успешным стартом остальных тулов принимается. «Не считать Build доказательством» принимается: доказательство — успешное создание `McpServerTool` этого плагина.

### Evidence

`Hosting/McpToolRegistry.cs`, `Hosting/McpToolDescriptor.cs`. Эпоха 1, «после Build фабрики видны». Эпоха 5, catch вокруг `Register`. Проба ревьюера, MCP 1.3.0. Контракт README: один битый плагин не роняет сервер.

### Classification

`ARCHITECTURE DEFECT`

### Severity

High у ревьюера. Совпадает: один метод сходится на отказе всего `tools/list`.

### Required outcome

Неподдерживаемый метод плагина не мешает старту хоста и не остаётся в списке тулов.

### Proposed specification change

Эпохи 1 и 5: создание SDK-тула входит в границу одного плагина и происходит до фиксации успеха. Эпоха 6 не оборачивает весь `Build` в «пропуск плагина».

### Downstream consequences

Порядок старта. Тест на неподдерживаемую сигнатуру. Наблюдаемость: причина пропуска называет метод.

---

## review-astra/E1-02

### Reviewer claim

Дедупликация «имя уже зарегистрировано в этом процессе» переживает dispose хоста и ломает второй тестовый контейнер с тем же `sample_loaded_workspace`.

### Related findings

Нет.

### Problem assessment

`ACCEPT`

### Problem rationale

Каждый `AddRoslynMcpServerTools` создаёт свою `McpRuntimeToolCollection` (`RoslynMcpServiceCollectionExtensions`). Тесты эпох 1, 5 и 6 поднимают несколько хостов в одном xUnit-процессе. Фраза эпохи 1 «в этом процессе» не говорит, что множество имён живёт на `IServiceCollection`, и не запрещает static. Статический набор даст порядок-зависимый провал, который спека сама породит своими тестами.

### Correction assessment

`ACCEPT`

### Correction rationale

Уникальность — один host: его `IServiceCollection` и его runtime-коллекция. Тест двух контейнеров в одном процессе, оба успешно регистрируют одно и то же имя, принимается. Процесс-глобальный запрет не следует из требования «не столкнуться с другим плагином этого запуска».

### Evidence

Эпоха 1, строка про «этот процесс». `RoslynMcpServiceCollectionExtensions` создаёт коллекцию на вызов. `McpToolActivationTests.BuildHost`.

### Classification

`DOCUMENTATION DEFECT`

### Severity

Medium у ревьюера. Совпадает: на один процесс MCP это не влияет, на приёмку серии — да.

### Required outcome

Повторная регистрация того же имени в новом контейнере того же процесса успешна. Повтор внутри одного контейнера по-прежнему пропускается.

### Proposed specification change

Эпоха 1: заменить «в этом процессе» на «в этом `IServiceCollection` / его коллекции тулов». Добавить тест двух хостов.

### Downstream consequences

Только тесты и формулировка. Поведение одного процесса MCP не расширяется.
