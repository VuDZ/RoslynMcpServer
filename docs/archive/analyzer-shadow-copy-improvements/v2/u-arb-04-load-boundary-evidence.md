# U-ARB-04 — evidence границы загрузки analyzer

Дата: 2026-09-12. Статус: **evidence выполнен; новая semantic/load boundary
не выбиралась и не специфицировалась**. Inaccessible не исследовался.

Среда: Windows, x64 process, .NET SDK 10.0.204, Roslyn 5.9.0,
`OutputPathMode.SdkDefaultCorrectPath`, generator identity
`Generator, Version=0.0.0.0`.

## Измеренная последовательность

Test-only host теперь снимает `AppDomain.CurrentDomain.GetAssemblies()` для
assembly names фактических analyzer references. Это наблюдение независимо от
process-lifetime `InProcessAnalyzerAssemblyLoader`, который видит только
подготовленные shadow paths.

| Последовательность | Фактически загруженный path | Forced rebuild real output |
| --- | --- | --- |
| Physical `LoadAsync`, flag off, без semantic query | Generator assembly не загружена | success; hash DLL изменён |
| Physical `LoadAsync` + shadow prepare, без semantic query | Generator assembly не загружена | success; hash DLL изменён |
| Published `GetCurrentSolution()` без активного overlay → `GetCompilationAsync`, затем enable | Точный real `Generator.dll`; published и raw references совпадают | fail `MSB3021` при `CopyRetryCount=0`; hash не изменён |
| После предыдущей строки cached enable=true | Prepare выполняется, overlay не активируется; identity gate указывает loaded real path и требует restart | real output остаётся locked |
| Overlay semantic после enable=true | Точный content-hashed shadow `Generator.dll`; real path отсутствует в process assemblies | success; hash real DLL изменён |
| Явный test-only raw reader после уже выполненной overlay semantic | Маркер `V1`; отдельная real assembly не появляется, остаётся загруженной shadow identity | success; hash real DLL изменён |

Таким образом, конкретный production-equivalent trigger lock воспроизведён:
semantic compilation existing-correct published project без активного overlay
**до** загрузки shadow identity. Сам physical open, provenance capture и prepare
analyzer не загружают и output не блокируют.

## Три production write path

`Epoch1WritePathTests` повторно выполнен для missing и existing-correct fixtures:

1. `UpdateDocumentInMemoryAsync`;
2. overlay-derived `ApplySolutionChangesToDiskAsync` + rename;
3. real FSW delivery + `FindDocumentAsync`/getter flush.

Для всех трёх existing-correct вариантов тест теперь проверяет:

- raw workspace reference = real output;
- опубликованный overlay reference = shadow path;
- `InProcessAnalyzerAssemblyLoader` и process assembly location = тот же shadow path;
- process assemblies не содержат real output;
- после операции forced rebuild успешен и меняет hash real DLL;
- `.csproj` остаются byte-identical.

Raw test output: 6/6 passed. MCP parser ошибочно классифицировал Theory-прогон как
`no matching tests`, но его собственный raw VSTest output содержит
`Test Run Successful`, `Total tests: 6`, `Passed: 6`.

## Инвентаризация production readers

В `Tools/` и `Services/` нет явного вызова
`GetWorkspaceCurrentSolution()` с последующим semantic model/compilation.
Semantic tools получают overlay через `FindDocumentAsync`,
`GetCurrentSolutionAfterDiskSyncAsync` или `GetCurrentSolution`.
Если overlay не активирован, этот published snapshot содержит те же real analyzer
references, что raw workspace; обычная production semantic operation поэтому
может загрузить и заблокировать existing-correct real output. Это ожидаемая
граница opt-in, а не обход уже активного overlay. Evidence-тест воспроизводит
`load false → semantic → cached enable true`: enable затем отклоняется identity
gate и требует restart.
Raw `workspace.CurrentSolution` в `SolutionManager` используется как база
write boundary, reconciliation и последующего `PublishInMemorySolution`, без
прямого `GetCompilationAsync`/`GetSemanticModelAsync`.

`WorkspaceTools.LoadWorkspace` всё же выполняет physical load и shadow prepare
двумя последовательными await-вызовами с отдельными захватами workspace lock.
Evidence выше измеряет semantic use до enable как отдельный no-overlay request,
но не доказывает, что production MCP dispatch фактически вклинивает запрос в
окно одного `load_workspace(..., shadowCopyInSolutionAnalyzers=true)`.

## Граница вывода

Evidence локализует no-overlay lock и подтверждает anti-lock для штатных overlay
semantic/write путей на указанной матрице. Он **не**:

- вводит новую boundary или меняет production code;
- доказывает отсутствие dispatch race между load и enable;
- обобщает результат на другие ОС/runtime/Roslyn;
- утверждает, что write boundary сама обеспечивает load isolation.

На момент evidence U-ARB-04 оставался открытым decision gate: следующим шагом
было отдельно решить, достаточно ли измеренной production-инвентаризации или
нужен детерминированный concurrent-dispatch repro/design. Inaccessible в этот
шаг не входил.

Последующее [решение](u-arb-04-decision.md) установило по реализации MCP SDK и
двум отдельным захватам `_workspaceLock`, что concurrent interleaving разрешена,
и выбрало atomic load/prepare boundary. Позднее она
[реализована и принята](u-arb-04-implementation-acceptance.md) в v1.3.14.
