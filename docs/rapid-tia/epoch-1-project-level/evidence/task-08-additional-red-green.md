# Task-08 — поздние регрессии RED → GREEN

- Дата: 2026-11-06 (Europe/Moscow).
- Исполнитель: Codex, `/root/task08_implementer`; точный runtime model ID средой не предоставлен.
- Это **два теста после первоначального RED**, а не часть первоначального пакета из 49 cases.
- Каждый тест добавлен до соответствующего исправления и запущен специализированным Roslyn MCP `run_specific_test`, Windows, Release, `noBuild=false`, `noRestore=true`, `timeoutSeconds=120`, `includeFullOutput=true`.
- Проект обоих вызовов: `E:/Devel/RoslynMcpServer-rapid-tia/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`.

## Полнота наследования между проектами

Причина добавления: при проверке обязательства T07-C1 после первого GREEN обнаружено, что словарь методов текущего проекта не содержит source-методы его базы из другого проекта solution. До исправления инвентарь называл себя полным, перечисляя только собственный тест.

Параметры focused RED: `className=VstestTestInventoryTests`, `methodName=Source_base_in_another_project_is_included_in_the_concrete_class_inventory`.

Точный фрагмент ответа MCP RED:

```text
## Filtered test run

**Filter:** `FullyQualifiedName~.VstestTestInventoryTests.Source_base_in_another_project_is_included_in_the_concrete_class_inventory`
**Match:** Name suffix `.VstestTestInventoryTests.Source_base_in_another_project_is_included_in_the_concrete_class_inventory`

❌ 1 Tests Failed.

Total: **1** · Passed: **0** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.Source_base_in_another_project_is_included_in_the_concrete_class_inventory`
   **Error:**

Assert.Equal() Failure: Collections differ
↓ (pos 0)
Expected: ["Inherited", "Own"]
Actual:   ["Own"]
↑ (pos 0)
```

Ошибок компиляции и фикстуры не было. Исправление получает syntax declaration source-базы из `DeclaringSyntaxReferences` и строит тот же baseline-ключ. Для metadata-only теста без source declaration полнота не утверждается.

Повторный MCP GREEN для `className=VstestTestInventoryTests`: **13/13**, 0 failed. Тест и его ожидания после RED не менялись.

## Полный список тестов проекта в подробном ответе

Причина добавления: исходный тест подробности проверял прямой выбранный тест. Проверка контракта показала, что `includeDetails` также обязан перечислить текущие тесты проекта, выбранного целиком после изменения библиотеки.

Параметры focused RED: `className=EstimateAffectedTestsTests`, `methodName=Details_of_project_expansion_list_every_current_test_in_the_selected_project`.

Точный фрагмент ответа MCP RED:

```text
## Filtered test run

**Filter:** `FullyQualifiedName~.EstimateAffectedTestsTests.Details_of_project_expansion_list_every_current_test_in_the_selected_project`
**Match:** Name suffix `.EstimateAffectedTestsTests.Details_of_project_expansion_list_every_current_test_in_the_selected_project`

❌ 1 Tests Failed.

Total: **1** · Passed: **0** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Details_of_project_expansion_list_every_current_test_in_the_selected_project`
   **Error:**

Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "Specs: Cases`0.Case"
```

Ошибок компиляции и фикстуры не было. Исправление передаёт текущий runtime-инвентарь formatter и объединяет прямые тесты с тестами выбранных целиком проектов. Подробности также показывают известные runtime-имена конкретных владельцев.

Повторный тот же focused MCP GREEN: **1/1**, 0 failed. Затем весь `className=EstimateAffectedTests` — **38/38**, 0 failed. Тест и его ожидания после RED не менялись.

## Итоговая проверка исполнителя до передачи координатору

После обоих исправлений и обновления точных каталоговых ожиданий:

- `EstimateAffectedTests`: **38/38**.
- `VstestTestInventoryTests`: **13/13**.
- `Hosting`: **80/80**, включая два уже перечисленных новых catalog cases.
- `SourceStructure`: **123/123**, прежние точные inventories limits без изменений.
- Во всех четырёх результатах 0 failed; skips, timeout и zero-test pass не заявлялись.

Это focused GREEN исполнителя. Финальные Release build, main CI suite, живые adapter-прогоны и независимое ревью выполняет координатор и сохраняет отдельно.
