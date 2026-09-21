# Этап 8. Тесты на xUnit v3

Статус: не начат. От остальных этапов не зависит и их не блокирует.

Раньше это было отложено как «просто смена раннера». Смесь в репозитории уже сломана: пакет тестов `xunit` 2.9.3, а `xunit.runner.visualstudio` 3.1.0 — адаптер линейки v3. Форк это выровнял. `SelfContained=false` при обычной сборке в VuDZ уже есть, повторять ту правку не нужно.

## Цель

`RoslynMcpServer.Tests` собирается и гоняется на xUnit v3 через тот же VSTest-адаптер, что и CI. Фильтры шардов `Category=AnalyzerLifecycle` продолжают попадать в те же классы. Разбор вывода `dotnet test` не переключается на Microsoft Testing Platform.

## Пакеты

Перед правкой сверить номера в регистре NuGet, не выдумывать следующую версию. Ориентир, на котором форк уже стоит: `xunit.v3` 3.2.2 и `xunit.runner.visualstudio` 3.1.5. Пакет `xunit` 2.x из тестового проекта убрать.

`xunit.runner.visualstudio` оставить. Без него v3 по умолчанию поднимает Microsoft Testing Platform, консоль перестаёт быть VSTest, и текущие шарды CI вместе с `VstestOutputParser` перестанут узнавать прогон. Свойство `UseMicrosoftTestingPlatformRunner` не включать.

`RoslynMcpServer.LifecycleTestHost` раннер не меняет, если сам не ссылается на xUnit.

## Что поправить в тестах

`ITestOutputHelper` в v3 лежит в пространстве имён `Xunit`. Сборки `Xunit.Abstractions` больше нет. Убрать `using Xunit.Abstractions` там, где он только ради этого типа.

`[Trait("Category", "AnalyzerLifecycle")]` и `[CollectionDefinition("AnalyzerLifecycle", DisableParallelization = true)]` оставить. После перехода один прогон с фильтром CI должен находить те же жизненные тесты, а юнит-шард — не видеть их.

`AnalyzerLifecycleFactAttribute : FactAttribute` по-прежнему выставляет `Skip`, если хост MSBuild не стартовал. Пропуск по-прежнему валит джоб через `Assert-NoSkippedTests.ps1`, а не становится зелёным.

Фикстуры `TestDiscoveryHelperTests` и `TestFilterHelperTests` ссылаются на сборку атрибутов xUnit, чтобы `[Fact]` связался. После смены пакета эта ссылка — `xunit.v3.core`, не `xunit.core`. Имена `Fact` и `Theory` те же, `TestAttributeMatcher` из-за v3 не переписывать.

Анализатор v3 ругается на асинхронный тест без `TestContext.Current.CancellationToken` (xUnit1051). Если из-за этого сборка тестов падает, поправить вызовы. Если это предупреждение и CI его не делает ошибкой, не размазывать правку по всем тестам в том же коммите.

`Assert.Skip` в форке заменил тихий успех в `NuGetFallbackAssemblyResolverTests`, когда кэша NuGet нет. Имеет смысл в этом же переходе: v3 умеет `Assert.Skip`, а тихий зелёный прогон врёт. На остальной набор это не распространять.

## Чего не делать

Не переводить `run_dotnet_test` на протокол Microsoft Testing Platform. Поддержка чужих решений на v3 в `get_test_list` уже идёт по имени атрибута `Fact` / `Theory` и по базовому классу. Отдельный разбор консоли MTP — другой объём, форк его не делал.

Не трогать версии пакетов сервера и не включать самодостаточную сборку для обычного `dotnet build`.

## Проверка

Сборка тестового проекта и `run_dotnet_test` по решению. Отдельно фильтр `Category!=AnalyzerLifecycle` и один фильтр `Category=AnalyzerLifecycle`: списки не пустые и не пересекаются. Пропуск жизненного теста при мёртвом хосте по-прежнему виден как skip, не как pass.

Патч версии не обязателен, если наружу инструментов ничего не изменилось. В README «Agent tools by version» строку не добавлять. В описании этапа CI достаточно фразы, что тестовый проект на xUnit v3, адаптер VSTest сохранён.
