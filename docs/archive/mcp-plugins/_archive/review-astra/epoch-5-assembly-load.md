ID: E5-01
Severity: Blocker
Category: Support profile

Target:
[epoch-5-assembly-load.md](../v1/epoch-5-assembly-load.md), строки 17–29 — host allowlist и обязательный пропуск при отсутствии пути.

Claim:
Любое имя вне четырёх host-семейств разрешается через AssemblyDependencyResolver; отсутствие пути означает пропуск плагина.

Evidence:
`System.Runtime` и другие framework-сборки не входят в перечисленные семейства. Их пути не обязаны находиться в deps.json плагина. Изолированная проба на .NET `10.0.12`: resolver обычной net10.0 class library вернул `null` для System.Runtime; ALC, превращающий это в ошибку, получил `FileLoadException` уже при GetType. [Документированный ALC-пример .NET](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support#load-plugins) возвращает `null` при managed resolver miss, позволяя стандартному разрешению продолжиться.

Failure scenario:
1. Автор собирает корректный net10.0 плагин; DLL и манифест доступны, все правила discovery выполнены.
2. Загрузчик получает тип entry, runtime запрашивает System.Runtime. Resolver не находит локального пути.
3. Реализация исполняет обязательное «нет пути — пропуск» и отбрасывает даже минимальный исправный плагин.

Suggested change:
Разделить shared host contracts, framework/default fallback и действительно отсутствующую частную зависимость. Resolver miss сам по себе не должен означать отказ. Нормировать fail-closed для частной зависимости после применимых стадий разрешения; проверить минимальный плагин, BCL-вызов и отсутствующую частную DLL отдельными тестами.

Confidence:
High

---

ID: E5-02
Severity: High
Category: Lifecycle

Target:
[epoch-5-assembly-load.md](../v1/epoch-5-assembly-load.md), строки 45–53 — отказ от отката Register при статусе «пропущен».

Claim:
После исключения Register плагин пропускается, уже добавленные singleton-регистрации остаются, процесс продолжает работу с остальными тулами.

Evidence:
`RoslynMcpPluginContext.Services` — общий изменяемый IServiceCollection; AddToolsFrom также добавляет в него tool-фабрики по эпохе 1. Список дескрипторов не теряет изменения от перехвата исключения. Проба с Hosting `10.0.8`: добавление singleton с отсутствующей constructor dependency → исключение Register → catch; последующий Build в Development падает с AggregateException при валидации этой оставшейся регистрации. Независимо от режима валидации уже добавленные tool descriptors тоже остаются, если их отдельно не откатить; «пропущенный» плагин может публиковать тулы.

Failure scenario:
1. Register добавляет service descriptor и один tool, затем падает на проверке своей конфигурации.
2. Загрузчик выполняет предписанный catch и записывает outcome skipped, оставляя общий IServiceCollection изменённым.
3. Хост либо падает при Build/разрешении оставшихся сервисов, либо публикует тулы плагина со статусом skipped. Изоляция и отчёт о загрузке перестают соответствовать реальности.

Suggested change:
Сделать применение регистраций к основному контейнеру и резервирование tool names транзакционным на уровне одного Register: commit только после успеха, discard при отказе. Явно ограничить эту гарантию регистрациями, а не произвольными внешними побочными эффектами пользовательского кода. Проверить throw после AddSingleton и AddToolsFrom, отсутствие остаточных tools/services и возможность следующего исправного плагина занять освобождённое имя.

Confidence:
High

---

ID: E5-03
Severity: High
Category: Support profile

Target:
[epoch-5-assembly-load.md](../v1/epoch-5-assembly-load.md), раздел «Контекст загрузки»; обещание копировать runtimes и зависимости в [эпохе 3](../v1/epoch-3-shadow-copy.md):19–30.

Claim:
Собственные зависимости плагина разрешаются из его скопированного дерева, включая содержимое runtimes; шаблон authoring пригоден для добавления своих пакетов.

Evidence:
В эпохе 5 задан только managed assembly-name resolution. Native import не проходит через `AssemblyLoadContext.Load(AssemblyName)`. [Документированный плагинный загрузчик .NET](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support#load-plugins) имеет отдельный `LoadUnmanagedDll`, использующий `ResolveUnmanagedDllToPath` и `LoadUnmanagedDllFromPath`. Произвольная `runtimes/<rid>/native/` папка вне deps основного приложения не становится системным native search path от одного факта File.Copy. Проверка `native.bin` в эпохе 3 не вызывает native code, а sample эпохи 4 native-зависимости не имеет.

Failure scenario:
1. Плагин получает пакет с managed-обёрткой и native-библиотекой в runtimes/<rid>/native; необходимые файлы действительно лежат в output и копии.
2. Загрузчик реализует ровно описанный managed resolver; Register и список tools проходят.
3. Первый вызов обёртки делает P/Invoke по имени. Библиотека не находится стандартным поиском, возникает DllNotFoundException, несмотря на наличие файла в копии.

Suggested change:
Нормировать native resolution относительно того же скопированного entry, выбор RID и fallback. Проверить настоящий native вызов из вложенного runtime asset на поддерживаемой платформе. Если native-пакеты исключаются из первой версии, это должно быть явным ограничением authoring и критерием отказа, а не неявным следствием неполного resolver.

Confidence:
High
