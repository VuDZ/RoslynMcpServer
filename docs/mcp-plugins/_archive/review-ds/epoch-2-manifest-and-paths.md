ID: E2-01
Severity: Medium
Category: Contract

Target:
epoch-2-manifest-and-paths.md / «Откуда пути» / пункт 2 (строки 37–40)

Claim:
Элемент ключа `plugins` не строка или значение не массив — «ошибка разбора этого файла, как у других ключей с неверным типом»; результат эпохи — список планов и список пропусков с причинами.

Evidence:
`RoslynMcpFileSettings.TryReadFile` держит `try` только вокруг `File.ReadAllText`, а `ParseDocument` вызывает `ReadString`/`ReadPositiveInt`/`ReadBool`, которые бросают `JsonException` (строки 221–356); это исключение не ловится ни в `TryReadFile`, ни в `LoadFromDirectories`, ни в `LoadFromDefaultLocations`. В `Program.cs` вызов `RoslynMcpFileSettings.LoadFromDefaultLocations()` (строка 55) не обёрнут в try/catch. То есть «поведение других ключей с неверным типом» — не запись в `ParseFailures`, а необработанное исключение до старта хоста.

Failure scenario:
1. Пользователь пишет `"plugins": "C:\\p\\bin\\Debug\\net10.0"` (строку вместо массива) или `"plugins": [10]` — ровно те два случая, которые описывает эпоха 2.
2. Хост падает на чтении настроек с необработанным `JsonException` до `PluginStartup` и до `RunAsync`; в логе нет ни строки `plugin skipped`, ни записи `RoslynMcp.jsonc parse failed`.
3. Контракт темы обещает «причина в результате» и «битый плагин не роняет сервер», а получается падение всего процесса из-за типа элемента в конфиге — при этом `ParseFailures` (штатный канал ошибок этого файла) не задействован.

Suggested change:
Зафиксировать в эпохе 2 явно: ошибка типа ключа `plugins` (и других ключей) — запись в `ParseFailures` и продолжение без плагинов, а не исключение; при необходимости починить `TryReadFile` (обернуть `ParseDocument` в try) и внести это в список файлов эпохи.

Confidence:
High
