ID: E1-01
Severity: High
Category: Lifecycle

Target:
[epoch-1-registration.md](../v1/epoch-1-registration.md), строки 21–24, 28–41; граница обработки ошибок в [эпохе 5](../v1/epoch-5-assembly-load.md):45–47 и [эпохе 6](../v1/epoch-6-host-startup.md):22–26.

Claim:
Прошедшие проверку имён методы регистрируются через фабрики как встроенные; после Build доступны в коллекции. Ошибки одного плагина не препятствуют запуску остальных.

Evidence:
[McpToolRegistry.cs](../../../../Hosting/McpToolRegistry.cs):16–21 добавляет отложенные DI-фабрики, а [McpToolDescriptor.cs](../../../../Hosting/McpToolDescriptor.cs):33–50 вызывает `McpServerTool.Create` внутри фабрики. Проверок только имени недостаточно для поддерживаемой SDK сигнатуры. Проба с MCP `1.3.0` и открытым generic-методом с допустимым именем показала: Register и Build успешны, разрешение `IOptions<McpServerOptions>.Value` падает с `ArgumentException: Open generic methods are not supported`. Именно `McpServerOptionsSetup` потребляет `IEnumerable<McpServerTool>`; текущий [Program.cs](../../../../Program.cs):87–92 не оборачивает SDK startup в обработку отдельного плагина.

Failure scenario:
1. Плагин добавляет атрибутированный generic-метод с правильным префиксом. AddToolsFrom сохраняет фабрику и возвращается успешно.
2. PluginAssemblyLoad фиксирует успешную загрузку; граница catch вокруг Register завершена.
3. При материализации MCP tools SDK отклоняет метод. Ошибка выходит в общий startup, остальные тулы не становятся доступными клиенту.

Suggested change:
Задать фазу проверки/материализации SDK tool definitions с привязкой ошибки к плагину до публикации успешного результата; отдельно нормировать ошибки вызова. Не считать Build доказательством создания tools. Обязательный отрицательный тест: допустимое имя, неподдерживаемая сигнатура, затем успешная инициализация MCP с остальными тулами.

Confidence:
High

---

ID: E1-02
Severity: Medium
Category: Lifecycle

Target:
[epoch-1-registration.md](../v1/epoch-1-registration.md), строка 33; повторное создание хостов в тестах эпох 1, 5 и 6.

Claim:
Имя отвергается, если уже зарегистрировано другим вызовом AddToolsFrom в этом процессе.

Evidence:
Текущий [RoslynMcpServiceCollectionExtensions.cs](../../../../Hosting/RoslynMcpServiceCollectionExtensions.cs):33–37 создаёт новую runtime-коллекцию на каждый вызов AddRoslynMcpServerTools. [McpToolActivationTests.cs](../../../../RoslynMcpServer.Tests/McpToolActivationTests.cs), `BuildHost`, создаёт независимый host для теста. Новые эпохи тоже многократно регистрируют `sample_loaded_workspace`, включая отдельный lite-host. Глобальный набор имён на процесс переживёт dispose одного контейнера; ни правило сброса, ни исключение для независимого IServiceCollection не заданы.

Failure scenario:
1. Первый тест регистрирует sample и успешно завершает работу хоста.
2. Второй тест в том же xUnit-процессе создаёт новый IServiceCollection и регистрирует тот же sample.
3. Буквальная дедупликация «в этом процессе» отбрасывает имя. Новый host лишается тула; результат зависит от порядка тестов.

Suggested change:
Ограничить уникальность одним host / IServiceCollection и его runtime-коллекцией. Проверять дубли внутри одного контейнера и независимое повторное подключение в двух контейнерах одного процесса.

Confidence:
High
