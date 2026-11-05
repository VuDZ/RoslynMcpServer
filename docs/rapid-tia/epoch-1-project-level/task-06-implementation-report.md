# E1/task-06 — отчёт реализации

- Дата: 2026-11-05; база `422e90b`.
- Scope: эмпирическая проба имён VSTest в xUnit v2, xUnit v3 MTP-off, NUnit и MSTest;
  fixture-проекты, bounded evidence и каталог для task-07.
- Исполнитель: Codex; точный runtime model ID неизвестен, средой не предоставлен.
- Статус исполнения: **accepted, 2026-11-05**; [ревью — ACCEPT](evidence/task-06-review-round-1.md), [validation](evidence/task-06-final-validation.md).
- Принятый scope — только task-06; task-07 и вся эпоха не объявляются принятыми.
- Результаты: [actual FQN, версии и фильтры](evidence/task-06-adapter-names.md).
- Финальная validation координатора: [отчёт](evidence/task-06-final-validation.md).

## Изменения

Четыре маленьких проекта в [spikes/AdapterTestNames](../../../spikes/AdapterTestNames/README.md)
не включены в server solution и не ссылаются на сервер или main test project.
Существующие `Compile Remove="spikes/**"` и исключение `SourceSetScope` оставлены
без изменений. Оба xUnit-проекта разделяют шесть исходников; всего 17 `.cs`, четыре
`.csproj`, README и collector. Отдельная dependency/configuration abstraction не
добавлялась.

В каждом проекте пять discovered cases: nested, generic/закрытая форма, inherited и
два параметризованных ряда. Абстрактная открытая generic-база xUnit/MSTest не имеет
самостоятельного runnable FQN; измерен конкретный закрытый derived-класс. NUnit
использует `[TestFixture(typeof(int))]`.

Collector извлекает настоящий `TestCase.FullyQualifiedName` из исходного JSON
VSTest discovery; сохраняет отдельный DisplayName, Id и ExecutorUri. Он проверяет
TRX counters и связывает execution `testId` с discovery `Id`. Ни list-tests, ни
TRX `className/name` не используются для вывода FQN. Сохранены 17 bounded data-файлов
общим размером 143284 bytes: четыре list-tests stdout, четыре filter stdout,
четыре полных discovery-message excerpts, четыре TRX excerpts и `catalog.json`.
Полные diagnostics/TRX и build outputs остаются в игнорируемых каталогах.

По результату координатор точечно обновил
[«Ответ инструмента» программы](../README.md#ответ-инструмента) до начала task-07:
`+` для nested, concrete runtime identity для inherited, известная закрытая форма
для generic, method prefix без аргументов для всех параметризованных строк.
Если concrete generic/inherited имя неизвестно, выбирается проект целиком с
объяснением. Production `TestFilterHelper` не менялся.

## Проверки

- Все четыре fixture-проекта собраны через `run_dotnet_build`, Release.
- Первичные filtered-пробы выполнены через `run_test_by_filter`; полный запуск
  каждого проекта через `run_dotnet_test` дал **5/5**, без ошибок и пропусков.
- `execute_dotnet_command` использован для `--list-tests --diag` и повторных
  filtered-прогонов с `--logger trx`/`--results-directory`, которых нет в интерфейсе
  специализированного test tool. Во всех таких прогонах `--no-build --no-restore`.
- 16 положительных TRX-запусков: **20/20 cases**. По каждому адаптеру выбор
  nested/generic/inherited = **1/1**, parameters = **2/2**, оба row identities
  сопоставлены с discovery.
- 12 отрицательных TRX-запусков: строго **0 total/executed/passed**; source-формы
  `Outer.Inner`, `GenericTests<T>` и declaring `InheritedBase` не выбирают тесты.
  VSTest exit 0 и MCP `partial` не засчитаны как pass.
- `Collect-Evidence.ps1` выполнен успешно: четыре полных discovery, 20 cases,
  28 TRX, точные counters и Id соответствия; xUnit v3 assets не содержат MTP.
- Отдельная офлайн-перепроверка сохранённых `catalog.json` и raw `.txt`, без
  `dotnet` и игнорируемых artifacts: все 20 cases совпали по Id/FQN/DisplayName/
  ExecutorUri, 28 filters и execution identities согласованы, totals 16/20/12
  (положительные запуски / passed cases / отрицательные запуски).
- Координатор после freeze C#/csproj независимо подтвердил fixture **20/20**,
  Release solution build и Windows main CI suite **1874/1874**. Три прежних build
  warnings сохранены в [validation](evidence/task-06-final-validation.md).

Task-06/R-16 явно исключает эту пробу из TDD. Искусственной RED-фазы нет; отрицательные
фильтры фиксируют фактическую несовместимость имён. Из setup потребовалось задать
NUnit assembly имя, отличное от NuGet package ID, до измерений; package/network
blockers нет.

## Границы и передача

Task-07 использует программу как норму и
[catalog.json](evidence/task-06-adapter-names/catalog.json) как replay data для
unit-тестов без живого runner. Каталог не становится альтернативной спецификацией.

Production-код, публичная активация, task-07, MTP и версия сервера не менялись.
Коммит и принятые статусы исполнитель не делал. Не проверялись другие версии
адаптеров, nested+generic комбинации, generic-методы, custom test names, несколько
derived-классов и Linux. AnalyzerLifecycle не запускался: его пути и конфигурация
не затронуты. Координатор обновил статусы задачи и индексов после приёмки.

## Приёмка

- Дата: **2026-11-05** (Europe/Moscow).
- Принятый scope: проба фактических имён четырёх VSTest-адаптеров, 28 фильтров, replay-каталог и нормативная передача в task-07. Production-код, публичная активация и task-07 вне scope.
- Исполнитель: Codex, сессия `/root/task06_implementer`; рецензент: Codex, независимая сессия `/root/task06_review_round1`. Точные runtime model ID средой не предоставлены.
- [Ревью](evidence/task-06-review-round-1.md): **1 раунд, 0 исправлений**, ACCEPT; MUST-FIX нет, ограничения T06-L01–L03 сохранены.
- [Validation координатора](evidence/task-06-final-validation.md): четыре независимых fixture-прогона по **5/5**, Release solution build PASS, main **1874/1874**; collector и offline raw/catalog replay PASS. В packet 16 positive selections/20 passed cases и 12 ожидаемых zero-match, не засчитанных как pass.
- Точечная нормативная правка «Ответа инструмента» принята до task-07: runtime-имена из measured evidence и проект целиком с причиной при неизвестной concrete generic/inherited identity.
- После freeze C#/csproj не менялись. Версия сохранена; Linux, MTP и новые версии адаптеров не проверялись.
