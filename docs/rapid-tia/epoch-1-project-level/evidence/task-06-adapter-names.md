# E1/task-06 — фактические имена адаптеров VSTest

- Дата измерения: 2026-11-05; исходная версия сервера: `422e90b`.
- Исполнитель: Codex; точный runtime model ID средой не предоставлен (`unknown`).
- Scope: только четыре изолированных fixture-проекта, discovery и фильтрованные
  прогоны VSTest; production-код, публичные инструменты и версия сервера не менялись.
- Измерения приняты **2026-11-05**: [ревью — ACCEPT](task-06-review-round-1.md), [validation](task-06-final-validation.md).
- Статусы задачи и индексов обновлены координатором; task-07 и вся эпоха остаются вне этой приёмки.
- TDD: явное исключение task-06/R-16; искусственной RED-фазы нет. Нулевые выборки
  ниже — проверенные неработающие формы фильтра, не успешные тестовые прогоны.

## Окружение и версии

Windows x64; SDK `10.0.402`; .NET runtime `10.0.13`; конфигурация `Release`.
VSTest console из SDK: `18.7.0-release-26505-111` (оригинальный заголовок diagnostics
сохранён отдельно). Во всех проектах `Microsoft.NET.Test.Sdk=17.14.1`, target
`net10.0`. Версия console отличается от версии test SDK/testhost — они не подменены
одним номером.

| Проект | Framework | Адаптер |
| --- | --- | --- |
| `XunitV2` | `xunit=2.9.3` | `xunit.runner.visualstudio=3.1.0` |
| `XunitV3` | `xunit.v3.mtp-off=3.2.2` | `xunit.runner.visualstudio=3.1.5` |
| `NUnit` | `NUnit=4.3.2` | `NUnit3TestAdapter=5.0.0` |
| `MSTest` | `MSTest.TestFramework=3.8.3` | `MSTest.TestAdapter=3.8.3` |

Версии закреплены в `.csproj`; полные фактически resolved package inventories —
в [catalog.json](task-06-adapter-names/catalog.json). Использованы существующие
кэшированные пакеты, кроме NUnit/адаптера: они успешно восстановлены из NuGet.
Изначальное имя assembly `NUnit` создавало NuGet cycle с одноимённым пакетом;
до измерений задано `AssemblyName=AdapterNames.NUnit`. Setup/network blockers нет.

**Доказательство VSTest для xUnit v3:** полный discovery message имеет
`ExecutorUri=executor://xunit/VsTestRunner3/netcore/` и `DiscoveredExtensions` с
`Xunit.Runner.VisualStudio.VsTestRunner`, версией assembly `3.1.5.0`; stdout запуска
показывает `xUnit.net VSTest Adapter v3.1.5+1b188a7b0a`. Resolved packages содержат
`xunit.v3.mtp-off`/`xunit.v3.core.mtp-off=3.2.2` и не содержат
`Microsoft.Testing.Platform*`; collector это проверяет. MTP-прогоны не выполнялись.
См. [raw discovery](task-06-adapter-names/xunit-v3.raw-discovery.txt) и
[filter stdout](task-06-adapter-names/xunit-v3.filters.txt).

## Получение настоящего FQN

`--list-tests` печатает DisplayName: у NUnit/MSTest даже простые `Generic`/`Nested`,
у xUnit — имена с аргументами строк. Это не источник FQN.

Источник таблицы — JSON `TestDiscovery.Completed.Payload.LastDiscoveredTests[*]`
в оригинальном VSTest diagnostics message, в частности его отдельное поле
`FullyQualifiedName`. Для каждого адаптера сохранено полное исходное сообщение
без удаления сериализованных свойств, исходный путь и SHA256 diagnostics-файла.
Всего четыре завершённых discovery, каждый `TotalTests=5`, `IsAborted=false`.

TRX используется только для счётчиков и идентичностей выполнения. Collector
сопоставляет `UnitTestResult.testId` с исходным discovery `TestCase.Id`; FQN не
восстанавливается из `TRX className/name` или из DisplayName. В каталог попадают
actual FQN, DisplayName, Id, ExecutorUri, фильтры и фактически выполненные cases.

## Конструкция → фактический FQN → выбор фильтром

В столбце «фильтр» опущен общий префикс `FullyQualifiedName~`. Это буквальное
значение фильтра, без параметров строки. `N/N` означает total/passed по TRX.

| Конструкция | Адаптер | Фактический TestCase.FullyQualifiedName | Фильтр: значение после `~` | Выбрано/прошло |
| --- | --- | --- | --- | --- |
| Вложенный класс | xUnit v2 | `AdapterNames.Xunit.Outer+Inner.Nested` | `AdapterNames.Xunit.Outer+Inner.Nested` | 1/1 |
| Вложенный класс | xUnit v3, MTP off | `AdapterNames.Xunit.Outer+Inner.Nested` | `AdapterNames.Xunit.Outer+Inner.Nested` | 1/1 |
| Вложенный класс | NUnit | `AdapterNames.NUnit.Outer+Inner.Nested` | `AdapterNames.NUnit.Outer+Inner.Nested` | 1/1 |
| Вложенный класс | MSTest | `AdapterNames.MSTest.Outer+Inner.Nested` | `AdapterNames.MSTest.Outer+Inner.Nested` | 1/1 |
| Закрытая generic-форма | xUnit v2 | `AdapterNames.Xunit.ClosedGenericTests.Generic` | `AdapterNames.Xunit.ClosedGenericTests.Generic` | 1/1 |
| Закрытая generic-форма | xUnit v3, MTP off | `AdapterNames.Xunit.ClosedGenericTests.Generic` | `AdapterNames.Xunit.ClosedGenericTests.Generic` | 1/1 |
| Generic `[TestFixture(typeof(int))]` | NUnit | `AdapterNames.NUnit.GenericTests<Int32>.Generic` | `AdapterNames.NUnit.GenericTests<Int32>.Generic` | 1/1 |
| Закрытая generic-форма | MSTest | `AdapterNames.MSTest.ClosedGenericTests.Generic` | `AdapterNames.MSTest.ClosedGenericTests.Generic` | 1/1 |
| Унаследованный метод | xUnit v2 | `AdapterNames.Xunit.InheritedTests.Inherited` | `AdapterNames.Xunit.InheritedTests.Inherited` | 1/1 |
| Унаследованный метод | xUnit v3, MTP off | `AdapterNames.Xunit.InheritedTests.Inherited` | `AdapterNames.Xunit.InheritedTests.Inherited` | 1/1 |
| Унаследованный метод | NUnit | `AdapterNames.NUnit.InheritedTests.Inherited` | `AdapterNames.NUnit.InheritedTests.Inherited` | 1/1 |
| Унаследованный метод | MSTest | `AdapterNames.MSTest.InheritedTests.Inherited` | `AdapterNames.MSTest.InheritedTests.Inherited` | 1/1 |
| Theory, две строки | xUnit v2 | `AdapterNames.Xunit.ParameterTests.Rows` (оба cases) | `AdapterNames.Xunit.ParameterTests.Rows` | 2/2 |
| Theory, две строки | xUnit v3, MTP off | `AdapterNames.Xunit.ParameterTests.Rows` (оба cases) | `AdapterNames.Xunit.ParameterTests.Rows` | 2/2 |
| TestCase, две строки | NUnit | `AdapterNames.NUnit.ParameterTests.Rows(1)`; `AdapterNames.NUnit.ParameterTests.Rows(2)` | `AdapterNames.NUnit.ParameterTests.Rows` | 2/2 |
| DataRow, две строки | MSTest | `AdapterNames.MSTest.ParameterTests.Rows` (оба cases) | `AdapterNames.MSTest.ParameterTests.Rows` | 2/2 |

У xUnit/MSTest generic-метод объявлен в абстрактном `GenericTests<T>`, выполняется
в `ClosedGenericTests : GenericTests<int>`. В discovery нет самостоятельного case
для открытого абстрактного `GenericTests<T>`: его runnable FQN отсутствует, он не
придуман по исходнику. Это измерение поддерживаемой закрытой формы, не утверждение
о поддержке произвольных открытых generic-тестов. В NUnit измерена прямая generic
fixture с явным type argument; нельзя обобщать `Int32` на универсальный formatter.

Во всех четырёх fixture-проектах inherited-метод объявлен в `InheritedBase`, а
выполняется на `InheritedTests : InheritedBase`. Assertion внутри метода проверяет
`GetType()==typeof(InheritedTests)`. У MSTest raw properties дополнительно содержат
`DeclaringClassName=...InheritedBase`, `TestClassName=...InheritedTests`: это разные
идентичности, и FQN использует вторую.

## Проверенные неработающие имена

Для **каждого** из четырёх адаптеров выполнены три отрицательных пробы:

| Кандидат после `FullyQualifiedName~` | Фактический результат | Исправление |
| --- | --- | --- |
| `<namespace>.Outer.Inner.Nested` | 0 total, 0 executed, 0 passed | `Outer+Inner.Nested` → 1/1 |
| `<namespace>.GenericTests<T>.Generic` | 0 total, 0 executed, 0 passed | Известное закрытое имя из таблицы → 1/1 |
| `<namespace>.InheritedBase.Inherited` | 0 total, 0 executed, 0 passed | Конкретный класс `InheritedTests.Inherited` → 1/1 |

Это формы, которые получаются при прямом использовании source-имени владельца
объявления (текущий helper generic/nested имена не переводит). VSTest завершает
нулевой выбор с exit 0; MCP сообщает `partial`, без summary. Эти 12 запусков —
**не passes**. Их TRX counters равны нулю; исправленные 16 запусков исполнили
20 cases, все passed. Дополнительно полный `run_dotnet_test` каждого fixture-проекта
дал 5/5, всего 20/20; ошибок, пропусков и timeouts в положительных прогонах нет.

## Передача в task-07 и границы

Точечное правило интегрировано координатором в
[«Ответ инструмента» программы](../../README.md#ответ-инструмента) до task-07:
вложенные типы используют `+`; параметризованный метод фильтруется без аргументов
строк; inherited-тест использует конкретный runtime-класс; generic-тест — известное
закрытое adapter-имя. Если конкретная generic/inherited идентичность неизвестна,
план выбирает проект целиком с явной причиной. Подстановка `<T>`/declaring base и
молчаливый нулевой выбор не допускаются. Method-only fallback не вводился.

Данные для unit-тестов task-07 без живого runner:

- [catalog.json](task-06-adapter-names/catalog.json): четыре адаптера, 20 actual cases,
  28 фильтров и 20 исполненных идентичностей; `Probe` отличает nested, generic,
  inherited, parameters и отрицательные формы.
- `*.raw-discovery.txt`: оригинальный FQN и adapter provenance.
- `*.list-tests.txt`: отдельно оригинальный stdout `--list-tests`, не FQN-каталог.
- `*.filters.txt`: точные команды, original MCP stdout/stderr и полные fixture totals.
- `*.trx-excerpts.txt`: оригинальные XML counters/results/TestMethod fragments с
  исходными путями и SHA256. `TestMethod` хранится как свидетельство выполнения;
  канонический FQN берётся исключительно из discovery.

Исходники и повторение: [spikes/AdapterTestNames](../../../../spikes/AdapterTestNames/README.md).
Collector проверил точные totals/passed/executed, пустые results отрицательных
проб и соответствие каждого положительного testId discovery Id. Полные diagnostics
и TRX остаются в игнорируемом `artifacts/`; raw сообщения и выдержки ограничены
этими четырьмя discovery и 28 запусками. Новых зависимостей сервера, fixture refs в
solution или main suite, production-правок и версии нет. Финальные проверки
основного solution/main выполняет координатор; этот отчёт их не заменяет.

Проба не покрывает иные версии адаптеров, комбинации nested+generic, generic-методы,
custom TestName/DisplayName, multiple derived classes и MTP. Это границы измерения,
не основание подставлять непроверенные runtime-имена.

## Источники setup

- [xUnit v3 migration](https://xunit.net/docs/getting-started/v3/migration): executable
  проект и Visual Studio adapter версии 3+ для VSTest.
- [xUnit 3.2.2 release notes](https://xunit.net/releases/v3/3.2.2): явный выбор runner
  в шаблонах; использован закреплённый `mtp-off`, а не меняющийся default шаблона.
- [NUnit TestFixture](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testfixture.html):
  явные generic type arguments и унаследованные fixture-методы.
- Пины [NUnit 4.3.2](https://www.nuget.org/packages/NUnit/4.3.2) и
  [NUnit3TestAdapter 5.0.0](https://www.nuget.org/packages/NUnit3TestAdapter/5.0.0).

Имена и результаты в этом отчёте получены локально из адаптеров, а не из примеров
в документации.
