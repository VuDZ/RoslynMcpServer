# Review: falsification of MCP plugins

Дата: 2026-09-27. Каталог: замечания к спецификациям, не к тону документов.
Ревью выполнено навыком `falsify-architecture-review`; имя каталога `review-astra` задано пользователем.

Сверка: текущий commit `3a73d57461ceec27389e692920a10c42c50be3fd`, версия хоста `1.4.15` / сборки `1.4.15.0`; `Program.cs`, `Hosting/`, `Config/`, публичные методы `SolutionManager`, semantic-тулы, проекты и существующие тесты. Реализации `Plugins/` и шаблона пока нет: карточки относятся к предлагаемым требованиям, а не к якобы уже реализованному загрузчику.

Цель ревью — опровергнуть предложенную архитектуру, а не улучшить её изложение.

Прочитаны целиком все девять документов: README, authoring и эпохи 1–7. Найдено **12 замечаний: 2 Blocker, 6 High, 4 Medium**. Повторяющиеся причины не размножены по зависимым эпохам.

| ID | Sev | Spec | Проблема |
| --- | --- | --- | --- |
| R-01 | High | [README.md](../v1/README.md) | Рекомендованный semantic-вход не синхронизирует изменения с диска |
| C-01 | High | [authoring.md](authoring.md) | Обычный build библиотеки не доставляет её NuGet-зависимости |
| C-02 | Medium | [authoring.md](authoring.md) | Опубликованный exe — native apphost, не сборка для Reference |
| E1-01 | High | [epoch-1-registration.md](epoch-1-registration.md) | SDK-фабрика падает после границы перехвата ошибок плагина |
| E1-02 | Medium | [epoch-1-registration.md](epoch-1-registration.md) | Дедупликация на процесс конфликтует с независимыми хостами тестов |
| E2-01 | High | [epoch-2-manifest-and-paths.md](epoch-2-manifest-and-paths.md) | Ошибка типа в конфиге выходит наружу до PluginStartup |
| E3-01 | Medium | [epoch-3-shadow-copy.md](epoch-3-shadow-copy.md) | Проверка копирования не доказывает разблокированный bin при живом ALC |
| E4-01 | Blocker | [epoch-4-sample-plugin.md](epoch-4-sample-plugin.md) | Исходники шаблона попадут в хост и сломают проверку каталога |
| E5-01 | Blocker | [epoch-5-assembly-load.md](epoch-5-assembly-load.md) | Запрет resolver miss отвергает framework-сборки |
| E5-02 | High | [epoch-5-assembly-load.md](epoch-5-assembly-load.md) | Пропущенный плагин оставляет действующие регистрации DI |
| E5-03 | High | [epoch-5-assembly-load.md](epoch-5-assembly-load.md) | Копирование runtimes не задаёт загрузку native-зависимостей |
| E6-01 | Medium | [epoch-6-host-startup.md](epoch-6-host-startup.md) | Приёмка не проверяет Program, переменную окружения и первый tools/list |

Для [эпохи 7](../v1/epoch-7-documentation.md) отдельной доказанной карточки нет. Она проверена на соответствие предыдущим эпохам; исправление C-01, C-02 и R-01 необходимо вне простого обновления статуса руководства. Отсутствие отдельного файла с карточками не означает пропущенное чтение.

## Проверки и границы доказательств

Изолированные пробы выполнены вне репозитория, в `%TEMP%/roslyn-mcp-review-astra-20260927`, через `C:/Program Files/dotnet/dotnet.exe`: SDK `10.0.204`, фактический runtime `10.0.12` x64, `ModelContextProtocol 1.3.0`, `Microsoft.Extensions.Hosting 10.0.8`. Пакеты взяты из локального NuGet cache с очищенными package sources. Это пробы отдельных механизмов, не тесты ещё не написанного загрузчика.

- Class library `net10.0` с `PackageReference Newtonsoft.Json 13.0.3`: build успешен, `CopyLocalLockFileAssemblies=false`, `EnableDynamicLoading` не задан; DLL зависимости отсутствует, `AssemblyDependencyResolver` возвращает `null`.
- ALC с буквальным правилом E5 «нет пути → ошибка»: `Resolve System.Runtime: <null>`, затем `FileLoadException` при `GetType` обычного класса.
- Регистрация `McpServerTool` через DI-фабрику для открытого generic-метода: регистрация и `Host.Build()` успешны; получение `IOptions<McpServerOptions>.Value` выбрасывает `ArgumentException: Open generic methods are not supported`. Использована регистрация `AddSingleton<McpServerTool>(factory)`, как в текущем `McpToolRegistry`, а не регистрация самой функции как сервиса.
- После перехваченного исключения регистрации оставлен singleton с отсутствующей constructor dependency: `Host.Build()` в `Development` выбрасывает `AggregateException` при валидации DI.
- MSBuild evaluation текущего host csproj: `EnableDefaultCompileItems=true`, исключений для `samples/` нет. Это проверка текущих правил включения файлов; будущий шаблон в репозиторий не добавлялся.
- Чтение assembly metadata существующего `bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe`: `PE image does not have metadata`. Старый publish использован только для проверки природы apphost, не как baseline исходников.

Native-загрузка, полный MCP roundtrip и disk-sync сценарий плагина не запускались; соответствующие карточки опираются на контракт, текущий код и документацию runtime. Полная сборка и suite репозитория не запускались: изменены только файлы ревью. Исходные документы, код и чужие незакоммиченные файлы не менялись.

---

ID: R-01
Severity: High
Category: Data consistency

Target:
[README.md](../v1/README.md), строки 34–36, 94–97, 126–127; связанный рецепт [authoring.md](../v1/authoring.md), строки 161–169, 255–257.

Claim:
Плагин анализирует тот же загруженный Solution, что встроенные тулы; показанный вход для эвристики — `GetPublishedSolutionAsync`.

Evidence:
[SolutionManager.cs](../../../../Services/SolutionManager.cs):336–349 возвращает `_solution` под lock, но не вызывает `FlushDirtyDocumentsUnderLockAsync`. Дисковый watcher лишь ставит пути в очередь: `OnDiskWatcherChanged` / `QueueDiskPath`, строки 2232–2293. Синхронизацию выполняют отдельные публичные `GetPublishedSolutionAfterDiskSyncAsync` (:353–367) и `GetSanitizedPublishedSolutionAsync` (:369–387). Встроенные [NavigationTools](../../../../Tools/NavigationTools.cs):336, 590, 798 используют второй из них. Таким образом, общий экземпляр менеджера не означает одинаковую актуальность семантического входа.

Failure scenario:
1. Пользователь загружает workspace и сохраняет изменение `.cs`; watcher доставляет событие, путь остаётся в очереди.
2. Сразу вызывается impact-тул, написанный по рецепту: `GetPublishedSolutionAsync` → Roslyn-анализ. Он читает старый текст и пропускает затронутые тесты.
3. Следующий встроенный semantic-тул применяет эту же очередь и видит изменение. Результаты расходятся без перезапуска или нового workspace.

Suggested change:
Нормировать отдельный вход для анализа после disk-sync и выбор raw/sanitized snapshot; исправить основной пример и руководство. Приёмка должна доставить дисковое событие и вызвать плагин первым, без предварительного встроенного тула, который случайно очистит очередь.

Confidence:
High
