# Приёмка эпохи 3

Дата: 2026-09-11. Вердикт: **принимается** (A3-01…A3-08 закрыты независимым повторным прогоном).
Норматив: [epoch-3-loader-contract-and-dependencies.md](epoch-3-loader-contract-and-dependencies.md) E3-S1–S5,
U-ARB-02 restart-required, U-ARB-03 main-only.
Прогон: [epoch-3-results.md](epoch-3-results.md).

Первый независимый прогон **не принимал** эпоху: cached refresh оставлял
`marker=V1` при `RestartRequired` (A3-01). Ниже — повтор после правок.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| A3-01 | Blocker | Cached V1→V2 исполняет V1 | **закрыт** — overlay снимается, oracle не компилирует |
| A3-02 | High | Тесты не ловят stale marker | **закрыт** — `OracleSuccess=false`, Marker пустой |
| A3-03 | Medium | `DependencyUnsupported` Action = restart | **закрыт** — Action = main-only, не restart |
| A3-04 | Medium | Conflicting helpers — мёртвый reason | **закрыт** — честный main-only отказ, reason удалён |
| A3-05 | Medium | First-use missing: Status=`None` | **закрыт** — `LoadFailed` |
| A3-06 | Low | Working set с testhost | **закрыт** — `env.WorkingSetBytes` host |
| A3-07 | Low | PublicKeyToken каталога не используется | **закрыт** — сверка PKT |
| A3-08 | Medium | Description / oracle / ARCHITECTURE | **закрыт** |
| A3-09 | — | Process restart exact V2 | **закрыт** |
| A3-10 | — | A→B не маркер A | **закрыт** |
| A3-11 | — | Helper / helper-only отказ | **закрыт** |
| A3-12 | — | Unit catalog / no first-match / collision | **закрыт** (5/5) |
| A3-13 | — | Failed refresh эпохи 2 (stale V1) | **закрыт** |

---

## Повторный прогон после A3-01…A3-08

x64 `C:\Program Files\dotnet`, `DOTNET_MULTILEVEL_LOOKUP=0`.

Независимый повтор (этот проход), x64 `C:\Program Files\dotnet`:

```text
AnalyzerLoaderContractTests | Epoch3 | V1_to_V2 | Solution_A | Failed_refresh
→ первый прогон 15 passed / 2 failed (host-response-timeout 45 с на *первом*
  `dotnet build` двух тестов при параллельной нагрузке, не oracle)
→ повтор V1_to_V2_cached_and_reset + Private_helper: 2/2 pass, 30 с
```

Unit 5/5. Cached oracle: `RestartRequired`, Marker пустой, overlay пустой
(`execution-not-permitted:RestartRequired`). Helper Action = main-only, не restart.
`Failed_refresh` эпохи 2 по-прежнему держит V1.

### Lifecycle после правки

| Случай | Status | Oracle |
| --- | --- | --- |
| V1→V2 **cached** | RestartRequired | `OracleSuccess=false`, Marker пустой (не V1 и не V2) |
| V1→V2 **reset+load** | RestartRequired | то же |
| V1→V2 **process restart** | ExecutionObserved | exact **V2** |
| A→B overlay off | RestartRequired | no-type, не A и не B |
| Private helper | DependencyUnsupported | отказ; Action без restart |
| Helper-only / conflicting | DependencyUnsupported | оба main-only отказ |
| First-use missing | **LoadFailed** | не success |
| Epoch 2 failed refresh | LoadFailed + stale mapping | **V1** сохраняется |

---

ID: A3-01 … A3-08
Закрыты в дереве:

- `CompletePrepare`: restart-required не публикует previous mapping в compilation;
  `PublishInMemorySolution` снимает in-solution analyzer refs.
- `OracleAsync`: не вызывает `GetCompilationAsync`, если `RequiresRestart` или
  `DependencyUnsupported`.
- `AssertRestartRequired`: требует пустой marker и `OracleSuccess=false`.
- Helper Action = `use a main-only generator…`; `ConflictingHelperReason` удалён.
- Missing output → `LoadFailed`.
- Host `env` отдаёт `WorkingSetBytes` / `PrivateMemoryBytes`.
- Catalog сверяет PublicKeyToken; forged `Microsoft.CodeAnalysis` + чужой PKT — не host.
- `load_workspace` Description, README Reference, ARCHITECTURE
  (`PublishInMemorySolution`), AGENTS.md.sample: restart MCP, reset ≠ unload.

---

## Что не чинить в этой эпохе

- ALC, in-process V2, production helper discovery.
- U-ARB-01 matcher, U-ARB-05 sticky flag.
- Exact inverse write-boundary (эпоха 4).
