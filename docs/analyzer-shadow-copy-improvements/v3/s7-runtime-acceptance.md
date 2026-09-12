# S7 — принять исправления v3 по runtime evidence

Статус: **выполнено; независимая приёмка
[не принята](s7-acceptance.md)** (v1.3.19 / `0115790`, блокер S7-1).
Зависимость: [S6](s6-contract-alignment.md).
Результат шага: итоговый проверяемый вердикт по R1–R4 и архитектурным гарантиям v3.

## Работа

Провести итоговый прогон на актуальном commit после всех исправлений.
Зафиксировать версию, OS, разрядность host, SDK/Roslyn, команды MCP и
длительности. Build должен соответствовать проверяемому коду; результаты
старой DLL не принимаются. Для влияющих на CLR тестов использовать отдельные
host-процессы; reset workspace не заменяет изоляцию процесса.

| Проверка | Обязательное доказательство |
| --- | --- |
| R1 / freshness | Stale same-session и stale-after-reload отклонены до writes; свежие тексты и project bytes сохранены |
| R2 / устойчивый fail-closed | После prepare failure/cancellation и последующих edit/flush/reconciliation real analyzer не появляется |
| R3 / capture gate | Missing/corrupt/incomplete capture не даёт semantic snapshot; cached calls не снимают запрет |
| Частичный prepare | Несколько references не создают смешанный shadow/confirmed-real snapshot; сводка честно отражает applied/stale/blocked |
| Успешный opt-in | Existing-correct и redirected missing fixtures исполняют exact marker из shadow path |
| Anti-lock | Real path отсутствует в process assemblies; forced rebuild успешен и меняет hash real DLL |
| Запись | Exact inverse сохраняет `.csproj`; неизвестный analyzer diff отклоняется; частичный исход не выдаётся за полный успех |
| Переходы сессии | Cached true/false/omitted, reset, graph reopen и смена load key сохраняют оговорённый допуск |
| CLR/dependencies | Restart-required и main-only остаются действующими; private helper не разрешается как fallback |
| Документация | R4 закрыт; inaccessible явно остаётся вне принятой матрицы |

Использовать regression tests S1 и дополнительные проверки S2–S5 вместе с
релевантными существующими `WorkspaceWriteBoundaryTests`,
`AnalyzerShadowGenerationPublisherTests`, `AnalyzerProvenanceBindingTests`,
`AnalyzerLoaderContractTests`, `Epoch1WritePathTests`, `Epoch3LoaderContractTests`,
`Epoch4WorkspaceWriteTests`, `F09ProductionCaptureTests`,
`Epoch5S4AcceptanceTests`, `UArb04LoadBoundaryEvidenceTests` и semantic inventory.
Выбор фильтров и непокрытые сценарии перечислить явно. Не запускать исторические
красные ожидания как неизвестную регрессию: их текущий контракт нужно учитывать.

Проверить MCP catalog/schema по существующим guard tests. Если изменены
описания tools, обновлённые ожидания должны следовать согласованному help,
а не служить способом скрыть изменение публичных параметров.

## Приёмка

- R1–R3 имеют сохранённые зелёные regression-тесты на production workflow;
  failure injection не подменяет сам manager/test oracle реализацией-заглушкой.
- В таблице выше для каждой строки есть фактический результат и ссылка на
  тест/evidence. Skip/timeout не считается pass.
- Нет новых блокирующих нарушений публикации, persistence или identity gate.
  При обнаружении дефекта вердикт остаётся «не принято» до исправления и
  повторной проверки затронутого сценария.
- Указаны commit и версия реализации. Если выпуск выполняется отдельно,
  различаются «проверено в исходниках» и «проверен опубликованный MCP binary»;
  публикация не подразумевается одним прохождением тестов.
- README v3 получает итоговый статус только по результатам этого шага.
  Открытый inaccessible не скрывается и не объявляется автоматически решённым.

## Результат

Выполнено 2026-09-12 (runtime; независимая приёмка **не принята**, см.
[s7-acceptance.md](s7-acceptance.md)).

- **HEAD:** `0115790` (`docs: align live analyzer-shadow norms with v3 S2-S5`), поверх S5 `8d75453` (v1.3.19).
- **Версия исходников:** `RoslynMcpServer.csproj` **1.3.19**.
- **Опубликованный MCP:** process **1.3.15**
  (`C:\Repos\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`,
  63 tools). Publish/reload этого шага не делались. Проверено в исходниках,
  не в опубликованном binary 1.3.19.
- **Окружение:** Windows 10 (build 26200), host x64, SDK **10.0.204**,
  `DOTNET_ROOT=C:\Program Files\dotnet`, `DOTNET_MULTILEVEL_LOOKUP=0`.
- **Build:** MCP `run_dotnet_build` на `RoslynMcpServer.sln`, Debug,
  `--no-incremental` — success (предупреждения `CS8603` / `CS8601`, 0 errors).
- **Unit (MCP `run_specific_test`, `noBuild=true`):**
  `WorkspaceWriteBoundaryTests` 17/17; `AnalyzerShadowGenerationPublisherTests` 18/18;
  `AnalyzerProvenanceBindingTests` 3/3; `AnalyzerLoaderContractTests` 5/5;
  `AnalyzerShadowPublicationPlannerTests` 3/3; `SemanticPublicationStateTests` 5/5;
  `McpToolCatalogTests.Surface_sizes_match_recorded_release_numbers` passed
  (63 / 44,503).
- **Lifecycle (локальный `dotnet test --no-build`, один процесс xUnit,
  отдельные `LifecycleHostClient` на тест):** фильтр
  `V3RegressionBaselineTests|V3WriteBaseFreshnessTests|V3PersistentPublicationStateTests|V3ProvenanceFailureGateTests|V3PartialPrepareTransitionTests|Epoch1WritePathTests|Epoch1SemanticInventoryTests|Epoch3LoaderContractTests|Epoch4WorkspaceWriteTests|F09ProductionCaptureTests|Epoch5S4AcceptanceTests|UArb04LoadBoundaryEvidenceTests`
  → **77 passed, 2 failed, 0 skipped**, 13 м 48 с.
- **Не гонялись как неизвестная регрессия:** исторические красные ожидания v1;
  полный `Category=AnalyzerLifecycle` целиком (MCP `-32001`). Непокрыто явно:
  inaccessible open; published 1.3.19 binary.

| Строка | Факт |
| --- | --- |
| R1 / freshness | pass — `V3RegressionBaselineTests.V3_R1_*`, `V3WriteBaseFreshnessTests` 7/7 |
| R2 / fail-closed | pass — `V3_R2_*`, `V3PersistentPublicationStateTests` 7/7 |
| R3 / capture | pass — `V3_R3_*`, `V3ProvenanceFailureGateTests` 7/7 |
| Частичный prepare | pass — `V3PartialPrepareTransitionTests` 5/5 |
| Успешный opt-in | pass — `V3PersistentPublicationStateTests.Successful_opt_in_*`; `Epoch5S4AcceptanceTests.Redirected_missing_path_rewrites_and_executes_exact_V1`; U-ARB-04 overlay |
| Anti-lock | pass — `UArb04LoadBoundaryEvidenceTests` 9/9 (lazy existing-correct + rebuild hash) |
| Запись | pass — `WorkspaceWriteBoundaryTests` 17; `Epoch4WorkspaceWriteTests` 13; `Epoch1WritePathTests` 6 |
| Переходы сессии | pass — cached/reset/graph в S3/S4/U-ARB-04 |
| CLR/dependencies | **fail** — Epoch3 restart 3/3 pass; helper 2 fail (`LoadFailed` / `opt-in-prepare-not-enabled` вместо `DependencyUnsupported`) |
| Документация | R4 закрыт S6; inaccessible open |
| Catalog | pass — 63 / 44,503 |

Блокер **S7-1:** `AnalyzerShadowPublicationPlanner` при нулевом `appliedCount`
и ненулевом `blockedCount` теряет `DependencyUnsupported`. v3 не принята.
