# Response: review-ds/epoch-1-registration.md

## review-ds/E1-01

### Reviewer claim

Коллекция тулов наполняется не в `Build`, а при первом `IOptions<McpServerOptions>.Value`, из `GetServices<McpServerTool>()`. Буквальный тест эпохи 1 видит пустую коллекцию. `CurrentToolCount` при пустой коллекции берёт число каталога, без плагинов.

### Related findings

Overlaps with `review-astra/E1-01`: оба про момент, когда фабрика становится тулом SDK. Astra — про исключение в этот момент, эта карточка — про то, что момент не `Build` и счётчик врёт.

### Problem assessment

`ACCEPT`

### Problem rationale

Существующие тесты перед чтением коллекции резолвят `IOptions<McpServerOptions>.Value` (`McpToolCatalogTests.MeasureSurface`, `McpToolGroupEnablementTests.Resolve`). Эпоха 1 пишет «после `Build` фабрики видны в коллекции» и велит читать коллекцию сразу. `McpToolActivationService.CurrentToolCount`: если `_tools.Count == 0`, возвращается `_surface.RegisteredToolCount`. Плагинов в surface нет. `Program.cs` печатает `registered tools` после `Build` через этот счётчик, до `RunAsync`. Если options к этому моменту не резолвились, строка и `get_mcp_server_info` не видят плагины. Исполнитель, чтобы зазеленить тест, может начать пихать имена в каталог — это запрещено эпохой 1.

### Correction assessment

`MODIFY`

### Correction rationale

Принимается обязанность тестов резолвить `IOptions<McpServerOptions>.Value` до чтения коллекции, как `MeasureSurface`. Принимается запрет класть плагины в `McpToolCatalog` ради счётчика.

Вторая половина рецепта («эпоха 6 сама наполняет коллекцию из отчёта») не нужна, если старт один раз резолвит options до чтения `CurrentToolCount` и до `get_mcp_server_info`. Иначе fallback на каталог остаётся ловушкой при любом раннем чтении. Спека должна назвать этот резолв явно, а не завести второй способ наполнения.

### Evidence

`Hosting/McpToolActivationService.cs`, `CurrentToolCount` и `IsToolActive` (пустая коллекция → каталог). `RoslynMcpServer.Tests/McpToolCatalogTests.cs`, `MeasureSurface`. Эпоха 1, тесты. Эпоха 6, «число включает тулы плагинов». `Program.cs` печатает счётчик после `Build`.

### Classification

`DOCUMENTATION DEFECT`

### Severity

Medium у ревьюера. Совпадает, пока fallback закрыт явным резолвом. Стало бы High, если счётчик в info остался на каталоге при уже загруженных плагинах.

### Required outcome

Тест эпохи 1 не путает пустую коллекцию с провалом регистрации. Счётчик после старта включает тулы плагина и не требует записи в каталог.

### Proposed specification change

Эпохи 1, 5 и 6: коллекция наполняется при первом `IOptions<McpServerOptions>.Value`; тесты делают этот шаг. Эпоха 6: перед чтением счётчика и перед info тот же резолв. Fallback «пустая коллекция = только каталог» не используется, когда отчёт старта содержит плагинные тулы.

### Downstream consequences

Порядок в `Program` после `Build`. `IsToolActive` до резолва options тоже смотрит только каталог; тот же резолв закрывает и help.
