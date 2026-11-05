# task-04 — TDD GREEN

- Дата: 2026-11-05 (Europe/Moscow).
- Roslyn MCP `run_test_by_filter`, `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, Release, `noBuild=false`, `includeFullOutput=true`.
- Фильтр: `FullyQualifiedName~ProjectImpactMapper`.
- Итог: 37 выполненных тестов, 37 passed, 0 failed. Тесты и фикстуры из RED не изменялись.
- Это focused validation; финальные build, SourceStructure и main suite проводит координатор.

```text
## Filtered test run

**Filter:** `FullyQualifiedName~ProjectImpactMapper`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **37** · Passed: **37** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped', 'RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped', 'RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped', 'RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Unmapped_ignored_output_is_neither_expanded_nor_uncovered', 'RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Unmapped_ignored_output_is_neither_expanded_nor_uncovered`
```
