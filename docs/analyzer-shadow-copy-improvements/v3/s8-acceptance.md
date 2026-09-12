# Приёмка v3 S8 — сохранить execution gate

Дата: 2026-09-12. Вердикт: **принимается в v1.3.20.**
Полный ban публикации сохраняет первое не-restart observation гейта.
Private helper снова даёт `DependencyUnsupported` / main-only; overlay
не публикуется. S7-1 закрыт. Исторический вердикт S7 на 1.3.19 / `4e8a85e`
не переписывается. Повтор S7 на 1.3.20 принят в исходниках; серия v1/v2
не завершена. Самоотчёт [implementer](7c458d06-91af-4ab6-86c7-cfacacdcb3d1)
не заменяет этот прогон.
Норматив: [s8-preserve-execution-gate.md](s8-preserve-execution-gate.md).

Независимый прогон незакоммиченного дерева 1.3.20. MCP `run_dotnet_build`
Debug `--no-incremental` success (SDK 10.0.204). Unit — MCP
`run_specific_test` (`noBuild=true`). Epoch3 — локальный
`C:\Program Files\dotnet\dotnet.exe`, `DOTNET_MULTILEVEL_LOOKUP=0`.

| Проверка | Результат |
| --- | --- |
| `AnalyzerShadowPublicationPlannerTests` | **4/4** — в т.ч. `Confirmed_private_helper_entry_bans_with_dependency_unsupported_gate` |
| `AnalyzerLoaderContractTests` | **5/5** — гейт не ослаблен |
| `Epoch3LoaderContractTests` | **7/7**, 0 skipped, 2 м 1 с |
| Helper / conflicting helpers | **passed** — Status `DependencyUnsupported` (как написано в тестах) |
| Restart / missing file | **passed** — остальные 5 Epoch3 |
| `McpToolCatalogTests.Surface_sizes_*` | **passed** — 63 / 44,503 |
| S7 на 1.3.19 | остаётся **не принято** |

Код: `firstNonRestartGate` на полном ban; `BanRestart` без изменений;
`Applied=false` / `OverlayEnabled=false`. Когда гейт не вызывался
(`Applied=false`), `FailureSummary` сохранён. Schema не менялась.
Inaccessible не выбирался. Опубликованный MCP process всё ещё **1.3.15**.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| S7-1 | P1 | helper → `LoadFailed` / `opt-in-prepare-not-enabled` | **закрыт** |
| S3-1 / S4-1 / S5-1 / S6-1…S6-3 | Low | прежние leftover | открыты, не чинились |
