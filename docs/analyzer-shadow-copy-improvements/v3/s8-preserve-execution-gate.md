# S8 — сохранить execution gate при полном ban публикации

Статус: **выполнено; независимая приёмка
[принята](s8-acceptance.md)** (v1.3.20, S7-1 закрыт).
Зависимость: [S7](s7-runtime-acceptance.md).
Результат шага: S7-1 закрыт; private helper снова даёт
`DependencyUnsupported` / main-only, overlay не публикуется.

## Основание

S7 на v1.3.19 / `0115790` не принят: два Epoch3 helper-теста красные.
`AnalyzerExecutionGate` по-прежнему ставит `DependencyUnsupported` и
Action main-only, но `AnalyzerShadowPublicationPlanner.Evaluate` при
`appliedCount == 0` и `blockedCount > 0` отбрасывает это observation и
подставляет `LoadFailed` / `FailureSummary ?? restart?.Reason ??
"opt-in-prepare-not-enabled"`. Host видит пустой `dependency` и не видит
main-only. Исполнения helper нет — ломается контракт U-ARB-03 / Epoch3,
не fail-closed.

Норматив: [s7-acceptance.md](s7-acceptance.md) (S7-1).
Targets: `AnalyzerShadowPublicationPlanner`, `AnalyzerShadowPublicationPlan.Gate`,
unit-тесты планировщика. `SolutionManager.ApplyPublicationPlan` уже копирует
`plan.Gate` в `_lastExecutionObservation` — отдельный обход manager не нужен,
если Gate честный.

## Работа

Когда все confirmed overlay references заблокированы и ни одна не applied,
`plan.Gate` должен быть первым не-restart observation гейта
(`DependencyUnsupported`, `LoadFailed` с реальной причиной missing file,
`IdentityCollision` только если это сам гейт), а не синтетический
`LoadFailed` с fallback `opt-in-prepare-not-enabled`.

Сохранить `Status`, `Reason`, `Action`, `DependencyName`. Restart-required
по-прежнему идёт через существующий `BanRestart`. Если гейт не вызывался
(все entries `Applied=false`) — оставить `FailureSummary` / skip reason,
как в текущем `All_failed_confirmed_entries_ban_without_claiming_full_refresh`.
Не публиковать overlay (`Applied=false`, `OverlayEnabled=false`,
`Admission=Banned`). Не ослаблять oracles, не выбирать inaccessible,
не добавлять MCP параметры, ALC, helper discovery.

Добавить unit-тест планировщика: подготовленный confirmed entry с private
helper (тот же emit-приём, что `AnalyzerLoaderContractTests`) даёт
`Kind=Banned`, `AppliedCount=0`, `Gate.Status=DependencyUnsupported`,
Action содержит main-only, Reason содержит имя helper. Существующие 3
теста планировщика не ломать.

Версия: bump patch **1.3.19 → 1.3.20** (`Version`, `AssemblyVersion`,
`FileVersion` вместе). `PackageVersion` не трогать. README «Agent tools
by version» (+ RU pointer / expected `get_mcp_server_info`) — строка 1.3.20
про сохранение gate status. `AGENTS.md.sample` не менять, если session
policy не меняется. Schema/catalog не менять.

Не писать `s8-acceptance.md`. Исторический вердикт S7 на 1.3.19 не
переписывать как принятый. v3 целиком не объявлять принятой в этом шаге.

## Приёмка

- `Epoch3LoaderContractTests.Private_helper_is_refused_and_helper_only_change_does_not_become_main_only_success`
  и `Two_generators_with_conflicting_helper_versions_are_refused` зелёные:
  Status `DependencyUnsupported`, Action без restart, с main-only,
  `Applied=false`, oracle без marker.
- Остальные `Epoch3LoaderContractTests` (restart-required, missing file)
  зелёные.
- Новый planner unit + прежние 3/3 зелёные.
- `AnalyzerLoaderContractTests` 5/5 без ослабления гейта.
- Нет новых MCP параметров; inaccessible не выбран.

## Результат

**выполнено** (реализация в исходниках; независимая приёмка не заявлена; v3 целиком не принята).

- **Commit:** N/A (uncommitted)
- **Версия csproj:** `1.3.20` (`Version` / `AssemblyVersion` / `FileVersion`; `PackageVersion` 0.1.0-beta без изменений)
- **HEAD docs:** `4e8a85e` (S7 не принят)
- **SDK:** `10.0.204`

### Команды

1. MCP `load_workspace` → `c:\Repos\RoslynMcpServer\RoslynMcpServer.sln` Debug
2. MCP `run_dotnet_build` Debug `--no-incremental` → success
3. MCP `run_specific_test` `noBuild=true` → `AnalyzerShadowPublicationPlannerTests`
4. MCP `run_specific_test` `noBuild=true` → `AnalyzerLoaderContractTests`
5. Local `dotnet test` `--no-build` `--filter FullyQualifiedName~Epoch3LoaderContractTests` (`DOTNET_ROOT=C:\Program Files\dotnet`, `DOTNET_MULTILEVEL_LOOKUP=0`)

### Фактические результаты

| Проверка | Результат |
| --- | --- |
| `AnalyzerShadowPublicationPlannerTests` | **4/4 passed** (3 прежних + `Confirmed_private_helper_entry_bans_with_dependency_unsupported_gate`) |
| `AnalyzerLoaderContractTests` | **5/5 passed** (гейт не ослаблялся) |
| `Epoch3LoaderContractTests.Private_helper_is_refused_*` | **passed** — `DependencyUnsupported`, Action main-only, `Applied=false` |
| `Epoch3LoaderContractTests.Two_generators_with_conflicting_helper_versions_are_refused` | **passed** — `DependencyUnsupported` |
| Остальные `Epoch3LoaderContractTests` | **passed** — класс **7/7**, 0 skipped, 1 м 17 с |
| Catalog / schema | не менялись: 63 / 44,503 |

Полный `Category=AnalyzerLifecycle` через MCP не гонялся (`-32001`). Publish/reload не делались. Inaccessible и leftover Lows S3-1 / S4-1 / S5-1 / S6-1..3 не чинились.
