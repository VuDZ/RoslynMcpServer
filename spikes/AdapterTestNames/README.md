# Проба VSTest-имён для rapid TIA

Изолированные фикстуры [E1/task-06](../../docs/rapid-tia/epoch-1-project-level/task-06-adapter-test-names-probe-low.md).
Результаты, версии, raw FQN и фильтры: [evidence](../../docs/rapid-tia/epoch-1-project-level/evidence/task-06-adapter-names.md).

Проекты не включены в solution сервера и не имеют ссылок на него. `spikes/**` уже
исключён из `Compile` сервера и из `SourceSetScope`; конфигурация основного проекта
не менялась. Оба xUnit-проекта используют одни исходники из `Xunit/`.

В каждом проекте пять test cases: вложенный класс, generic/закрытая форма,
унаследованный метод в конкретном классе и два параметризованных ряда. Public
вложенный класс здесь специально нужен для проверки discovery. Абстрактные
generic-базы xUnit/MSTest не выдаются за самостоятельно исполняемые тесты.

## Повторение

Из корня репозитория, через Roslyn MCP:

1. Для каждого `XunitV2`, `XunitV3`, `NUnit`, `MSTest` вызвать `run_dotnet_build`
   с `workspacePath=spikes/AdapterTestNames/<project>/<project>.csproj`,
   `configuration=Release` (tool требует абсолютный путь).
2. В `execute_dotnet_command` задать `workingDirectory=spikes/AdapterTestNames`
   (абсолютный путь) и команду ниже, заменяя project и adapter ID.

```text
test XunitV3/XunitV3.csproj -c Release --no-build --no-restore --list-tests --diag artifacts/xunit-v3.discovery.log
```

3. Для фильтров `catalog.json` обычная проверка выполняется через
   `run_test_by_filter`, `configuration=Release`, `noBuild=true`, `noRestore=true`.
   Для сохранения TRX нужен `execute_dotnet_command`: специализированный инструмент
   не принимает logger/results-directory. Точные команды каждого прогона сохранены
   в `<adapter>.filters.txt`. Пример:

```text
test XunitV3/XunitV3.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~AdapterNames.Xunit.Outer+Inner.Nested" --logger "console;verbosity=normal" --logger "trx;LogFileName=nested.trx" --results-directory artifacts/xunit-v3
```

4. Выполнить `run_dotnet_test` для каждого проекта в Release с `noBuild=true`,
   `noRestore=true`: ожидается 5 passed, 0 failed, 0 skipped у каждого.
5. Из любой рабочей директории выполнить `Collect-Evidence.ps1` через PowerShell.
   Скрипт извлекает фактические `TestCase.FullyQualifiedName` из последнего полного
   VSTest discovery message, связывает TRX `testId` с discovery `Id`, проверяет
   точные количества и сохраняет каталог и оригинальные выдержки. Для запуска
   скрипта нужны все 28 TRX и четыре discovery diagnostics из предыдущих шагов.
   stdout `list-tests`/прогонов сохраняется отдельно при вызове MCP.

Скрипт рассчитан на этот фиксированный snapshot: SDK 10.0.402, runtime 10.0.13,
VSTest console 18.7.0-release-26505-111 и закреплённые версии пакетов. Смена
окружения требует обновить metadata и заново проверить результаты. Он не запускает
`dotnet` и не реализует production-форматирование имён.

Диагностика, TRX, `bin/` и `obj/` остаются под существующими ignore-правилами.
В репозитории сохраняются только ограниченные `.txt`-выдержки и `catalog.json`.
