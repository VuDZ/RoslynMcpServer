# 10 — `get_test_list` VSTest FQN

Pri **P1** (дешёвый). SemVer: **patch**. Зависимостей нет.
Разбор: [../port-candidates-1-2-3-4-7.md](../port-candidates-1-2-3-4-7.md#10-get_test_list-vstest-fqn).

## Цель

JSON `fullyQualifiedName` = `Ns.Type.Method` без `global::` и `()`, тот же
хелпер, что фильтр `nameContains`.

## Файлы

- `Services/TestDiscoveryHelper.cs` — payload, строка ~97
- `RoslynMcpServer.Tests/TestDiscoveryHelperTests.cs`

Не трогать `TestAttributeMatcher`.

## Правка

Заменить `symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` на
`TestFilterHelper.FormatVstestFullyQualifiedName(symbol)`.

Nested/generic adapter-кейсы — **follow-up в этом же пункте, не блокер** базового
`Ns.Type.Method`. Не блокировать ship, если нет testhost-фикстуры nested type;
зафиксировать TODO-тест или отдельный commit.

## Тесты

- без namespace → `MyTests.DoesThing`
- с namespace → `Acme.Tests.WidgetTests.Parses`
- нет `global::`, нет `()`
- по возможности nested class — сверка с тем, что VSTest adapter принимает в
  `FullyQualifiedName~`

## Acceptance

Значение из `get_test_list` можно скормить `run_specific_test` без зачистки
для обычных классов.

## Не копировать

`EndsWith("Fact")` / плоский набор атрибутов форка.
