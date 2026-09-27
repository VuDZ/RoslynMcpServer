ID: E2-01
Severity: High
Category: Lifecycle

Target:
[epoch-2-manifest-and-paths.md](../v1/epoch-2-manifest-and-paths.md), строки 38–41, 63–66, 85 — ошибки типов нового ключа plugins.

Claim:
Неверный тип plugins или элемента массива обрабатывается как ошибка разбора этого файла, по существующему механизму остальных ключей.

Evidence:
В текущем [RoslynMcpFileSettings.cs](../../../../Config/RoslynMcpFileSettings.cs):217–242 catch покрывает чтение текста, но `ParseDocument(document)` вызывается после него. Проверки типов, например `ReadString` (:313–315), выбрасывают `JsonException`; она не преобразуется в `ParseFailures`. [Program.cs](../../../../Program.cs):54 читает настройки до try регистрации MCP и до будущего PluginStartup. Значит, перенести паттерн типизированного чтения на plugins недостаточно для заявленного безопасного пропуска. Тесты эпохи требуют только успешный массив и merge, не неверные типы.

Failure scenario:
1. Исполнитель добавляет чтение массива и выбрасывает JsonException при неверном типе, аналогично существующим ключам.
2. Пользователь записывает синтаксически правильное `{"plugins":[42]}`.
3. Исключение проходит из ParseDocument через LoadFromDefaultLocations наружу до запуска PluginStartup. Встроенные тулы и исправные drop-in плагины не запускаются.

Suggested change:
Включить в эпоху границу преобразования ошибок типизированного разбора в ParseFailures и явно определить fallback к другому файлу настроек. Проверить non-array, non-string element и сохранение работоспособности хоста. Это исправление текущего механизма, необходимое для нового обещания, а не уже имеющееся поведение.

Confidence:
High
