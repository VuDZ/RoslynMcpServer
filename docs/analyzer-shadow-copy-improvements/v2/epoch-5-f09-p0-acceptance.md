# Приёмка F-09 P0-spike

Дата: 2026-09-12. Вердикт: **P0 принимается. Канал binlog — GO на измеренной
матрице. Matcher и E5-S2 rollout не открыты.**
Норматив: [epoch-5-f09-capture-design.md](epoch-5-f09-capture-design.md) § P0,
[приёмка эскиза](epoch-5-f09-acceptance.md).
Прогон: [epoch-5-f09-p0-spike-results.md](epoch-5-f09-p0-spike-results.md).
Независимый повтор: `F09CaptureSpikeTests` **4 passed / 0 failed** (MCP
`run_specific_test`, x64 `C:\Program Files\dotnet`).

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| P0-1/2 | — | Open* + BinaryLogger, metadata в replay | **закрыт** |
| P0-3 | — | Debug/Release + два inner TFM, один ProjectId | **закрыт** |
| P0-4 | — | Foreign same-name не foreign | **закрыт** |
| P0-5 | — | Compiler output не менялся | **закрыт** |
| P0-6 | — | bytes / wall на fixture и repo sln | **закрыт** — наблюдение, не SLA |
| P0-7 | Medium | Lifecycle counters в `LoadCoreAsync` | **открыт** — spike вне `SolutionManager` |
| P0-8 | — | Fail-closed + delete temp | **закрыт** |
| F09-01 | Medium | Join bin vs obj | **закрыт** — redirected fixture + `Identity == OutputFilePath` |
| F09-02 | Medium | Always-on | **измерен** — ~50–75 ms median; telemetry в production |
| F09-04 | Low | `Microsoft.Build` в server csproj | **открыт** — есть только в Tests |

`SolutionManager` / matcher / публичный API не менялись.

---

ID: P0-7
Severity: Medium
Depends: F-09 lifetime

Target:
SolutionManager.LoadCoreAsync

Claim:
Чеклист эскиза п.7: один capture на physical open, ноль на cache/edit/query.
Spike это вывел из существующего cache guard, не счётчиком. Production
capture без тестов на этой границе может уехать в cache hit или в refresh.

Suggested change:
Сервис snapshot + counters в реализации capture. Не считать P0 разрешением
менять matcher.

---

## Что дальше

1. Production capture: `Microsoft.Build` в server csproj (F09-04), immutable
   snapshot, join как в results (не требовать `TargetPath`/`IntermediateAssembly`),
   fail-closed replay, cleanup, **не** `LoggerVerbosity.Diagnostic`.
2. Lifecycle tests на `LoadCoreAsync` (P0-7).
3. Только после atomic snapshot — E5-S2 / смена matcher и marker acceptance.

Не делать: Alt-2/Alt-3, inaccessible, rollout matcher в этом шаге.
