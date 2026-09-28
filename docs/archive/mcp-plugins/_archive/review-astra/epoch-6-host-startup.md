ID: E6-01
Severity: Medium
Category: Measurement

Target:
[epoch-6-host-startup.md](../v1/epoch-6-host-startup.md), строки 7–9, 17–20, 74–87; [README](../v1/README.md):200–204 и [authoring](../v1/authoring.md):8–9.

Claim:
Первый tools/list реального MCP-процесса содержит плагины; общий sample проверяется через ROSLYN_MCP_PLUGINS, включая discovery и DI.

Evidence:
Все перечисленные startup-тесты вызывают PluginStartup непосредственно, с временным baseDirectory и drop-in. Тест эпохи 2 передаёт строку переменной окружения аргументом discovery, а не запускает Program. Ни один gate не отправляет initialize / tools/list / tools/call запущенному host. Поэтому проверка пропускает сам wiring [Program.cs](../../../../../Program.cs), чтение environment и реальную материализацию SDK. Это отдельная граница от E3-01: здесь проверяется процесс и протокол, а там удержание файлов при загруженном ALC.

Failure scenario:
1. PluginStartup корректен и проходит in-process тесты; в Program ошибочно передана пустая строка вместо ROSLYN_MCP_PLUGINS либо вызов startup не попал в нужную ветку запуска.
2. Unit discovery получает корректную строку напрямую и тоже проходит; тесты drop-in не обнаруживают ошибку dev-пути.
3. Пользователь запускает stdio-host с единственным dev-плагином через environment. Первый tools/list не содержит sample, хотя эпоха принята.

Suggested change:
Добавить обязательный subprocess smoke-test настоящего entry point: только environment dev-путь, initialize, самый первый tools/list и tools/call sample. Проверить stderr при битом соседе и отсутствие не-протокольного вывода в stdout. In-process тесты оставить для локализации причин, но не считать доказательством process-level контракта.

Confidence:
High
