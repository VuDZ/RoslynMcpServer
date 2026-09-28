# Response: review-astra/epoch-2-manifest-and-paths.md

## review-astra/E2-01

### Reviewer claim

Неверный тип ключа `plugins` не становится `ParseFailures`. `ParseDocument` бросает `JsonException` вне `try`, и процесс умирает до `PluginStartup`.

### Related findings

Duplicate of `review-ds/E2-01`.

### Problem assessment

`ACCEPT`

### Problem rationale

`TryReadFile` ловит только ошибку чтения текста. `ParseDocument` вызывается после `try`. `ReadString` / `ReadPositiveInt` / `ReadBool` бросают `JsonException` (`RoslynMcpFileSettings.cs`). `Program.cs` вызывает `LoadFromDefaultLocations` до регистрации MCP и без catch. Эпоха 2 говорит «как у других ключей с неверным типом» и одновременно обещает список пропусков, а не падение. Существующий механизм как раз падает. Тесты эпохи покрывают успешный массив и замену массива из cwd, не неверный тип.

### Correction assessment

`ACCEPT`

### Correction rationale

Граница «тип не сошёлся → запись в `ParseFailures`, хост стартует» нужна именно потому, что эпоха обещает разбор, а не исключение. Обернуть `ParseDocument` — единственный способ не изобретать второй парсер. Последствие шире ключа `plugins`: неверный `max-results` тоже перестанет ронять процесс. Это следует из починки того же `try`, и это согласовано с уже существующим каналом `ParseFailures` для битого JSONC. Fallback: файл с ошибкой типа не отдаёт свои ключи; второй файл настроек (exe или cwd) по-прежнему сливается по нынешним правилам для успешно прочитанного соседа. Оба файла битые — пустые настройки и строки `ParseFailures`, не `Environment.Exit`.

### Evidence

`Config/RoslynMcpFileSettings.cs`, `TryReadFile` и `ReadString`. `Program.cs` читает настройки до try каталога тулов. README: битый плагин не роняет сервер. Эпоха 2, пункт про тип массива.

### Classification

`ARCHITECTURE DEFECT`

### Severity

High у ревьюера. У `review-ds/E2-01` — Medium. Беру High: падает весь процесс, не один плагин. Medium занижена, это не косметика контракта.

### Required outcome

`{"plugins":[42]}` и `"plugins": "path"` не роняют хост. Причина видна как ошибка разбора файла. Остальные тулы стартуют.

### Proposed specification change

Эпоха 2: `JsonException` из типизированного разбора попадает в `ParseFailures`; в список файлов входит эта граница `RoslynMcpFileSettings`, не только новое свойство. Тесты: не массив, элемент не строка, хост (или метод загрузки настроек) возвращает управление.

### Downstream consequences

Поведение уже существующих ключей при неверном типе становится мягким. Ленивая загрузка workspace и сегодня отключается при любом `ParseFailures` (`EnsureWorkspaceFromConfigAsync`). Это сохраняется.
