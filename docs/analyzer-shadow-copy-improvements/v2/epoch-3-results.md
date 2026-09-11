# Эпоха 3 — результаты

Дата: 2026-09-11. Статус: **принят повторный прогон** после A3-01…A3-08.
Приёмка: [epoch-3-acceptance.md](epoch-3-acceptance.md).
Окружение: Windows 10 (build 26200), .NET SDK 10.0.204, Roslyn 5.9.0,
x64 `C:\Program Files\dotnet`. Режим: **restart-required**. Dependencies: **main-only**.

Первый прогон реализации оставлял cached oracle `marker=V1` при `RestartRequired`
и был отклонён приёмкой. Повтор после снятия stale overlay и oracle-gate:

```text
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
& 'C:\Program Files\dotnet\dotnet.exe' test `
  c:\Repos\RoslynMcpServer\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj `
  --filter 'FullyQualifiedName~Epoch4_surface|FullyQualifiedName~AnalyzerLoaderContractTests|FullyQualifiedName~Epoch3LoaderContractTests|FullyQualifiedName~V1_to_V2|FullyQualifiedName~Solution_A_opt_in|FullyQualifiedName~Failed_refresh_keeps_stale'
```

**18 passed / 0 failed**, 3 m 13 s.

## Unit

`AnalyzerLoaderContractTests` — **5 passed**. Catalog exact names + PKT;
forged `Microsoft.CodeAnalysis` с чужим token — не host contract.
Helper AssemblyRef → отказ без restart action; same-identity collision → restart;
first-match probing не биндит.

## Lifecycle

| Случай | Результат |
| --- | --- |
| V1→V2 cached | **pass** — RestartRequired; `OracleSuccess=false`; Marker пустой |
| V1→V2 reset+load | **pass** — то же; reset не выгружает CLR |
| V1→V2 process restart | **pass** — exact V2; ExecutionObserved; forced rebuild пишет bytes |
| A→B same identity, B overlay off | **pass** — отказ; не маркер A и не успешный B |
| Private helper | **pass** — DependencyUnsupported; Action main-only, не restart |
| Helper-only / conflicting versions | **pass** — тот же main-only отказ |
| First-use missing output | **pass** — Status=`LoadFailed` |
| Repeat 3× same V1 | **pass** — host `WorkingSetBytes` из `env`, root reuse |
| Epoch 2 failed refresh | **pass** — stale mapping, маркер V1 |

Catalog: full 63 / 44165; lite 19 / 16579.

## Ограничения поддержанной матрицы

- In-process V2 при той же assembly identity не поддерживается.
- Private helpers, конфликтующие helpers, ALC, native deps — вне поддержки.
- `reset_workspace` не unload и не снимает `AssemblyResolve`.
- Restart cost = новый процесс MCP; частота — после каждой смены bytes генератора
  с той же identity. Численный SLA не назначается.
- Anti-lock: process, держащий только shadow, позволяет overwrite original main/helper.
- Failed file refresh (inject / disk) по-прежнему держит stale V1 — это эпоха 2,
  не unsupported in-process refresh.
