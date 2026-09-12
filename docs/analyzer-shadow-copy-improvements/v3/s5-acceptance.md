# Приёмка v3 S5 — частичный prepare и переходы

Дата: 2026-09-12. Вердикт: **принимается в v1.3.19.**
Смешанный prepare публикует только доказанно безопасный snapshot:
успешные confirmed — shadow, неуспешные — исключение или разрешённый stale,
иначе вся публикация banned. Чужой успех не возвращает real output.
Самоотчёт в `s5-partial-prepare-transitions.md` не заменяет этот прогон.
Норматив: [s5-partial-prepare-transitions.md](s5-partial-prepare-transitions.md).

Независимый прогон: MCP `run_dotnet_build` Debug `--no-incremental` success
(SDK 10.0.204). Unit-тесты через MCP `run_specific_test` (`noBuild=true`).
`Category=AnalyzerLifecycle` — локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`.

| Проверка | Результат |
| --- | --- |
| `AnalyzerShadowPublicationPlannerTests` | **3/3** passed |
| `WorkspaceWriteBoundaryTests` | **17/17** passed (inverse exclusions + unknown diff) |
| `SemanticPublicationStateTests` | **5/5** passed |
| `AnalyzerReferenceShadowCopierTests` | **11/11** passed |
| `McpToolCatalogTests.Surface_sizes_match_recorded_release_numbers` | **passed** — 63 / 44,503 |
| `V3PartialPrepareTransitionTests` + R1–R3 + S3 + S4 + Epoch2 | **25/25** passed (~2 м 55 с) |
| Mixed 1 success + 1 file fail | **passed** — нет real A/B; allowed marker exact; blocked marker нет; rebuild меняет hash |
| Edit / flush / reconciliation | **passed** — тот же admission, без analyzer I/O; `.csproj` byte-identical |
| File-failed refresh | **passed** — stale только у failed; другая applied fresh |
| Active → restart-required → edit/cached | **passed** — нет V1/V2; stale V1 не исполняется |
| Summary none-applied / restart | **passed** — ban не пишет Applied из файлов; partial ≠ полный refresh |
| R1 / R2 / R3 / S3 / S4 / Epoch2 | **passed** — не ослаблены |

Код: допуск по всем confirmed overlay references, не по `HasAnyApplied`.
Allowed overlay + `ApplyExcludedReferences`; BanRestart не поднимает
`CompleteFailedPrepare`. Exact inverse восстанавливает exclusions.
Fixture `Generator` + `GeneratorB` (разные assembly identities).
Oracle по-прежнему exact constant; второй тип — opt-in параметр host.
Публичная MCP-схема не менялась. Inaccessible не выбирался.
Два генератора в fixture — не продукт «два поколения одной сборки».

HEAD git: этот коммит поверх `6fe0665` (v1.3.18). csproj **1.3.19**.
MCP process всё ещё **1.3.15** (publish/reload этого шага не делались).

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| V3-R1 / A4-09 | P1 | stale candidate перезаписывает свежий текст | **закрыт** (S2) |
| V3-R2 | P1 | fail-closed сбрасывается text edit | **закрыт** (S3) |
| V3-R3 | P1 | corrupt capture публикует real | **закрыт** (S4) |
| S3-1 | Low | `LoadCore` cache hit `_solution ?? CurrentSolution` | открыт |
| S4-1 | Low | `PublishInMemorySolution(Unavailable)` возвращает raw | открыт |
| S5-1 | Low | `IsRestartBanLatched` ищет `"restart"` в BanReason | открыт |

Следующий шаг — S6 (согласование документации).
