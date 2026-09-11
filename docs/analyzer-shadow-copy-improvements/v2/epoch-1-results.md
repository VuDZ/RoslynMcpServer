# Эпоха 1 — результаты baseline

Статус: **измеренный красный baseline**. Дата прогона: 2026-09-11.
Окружение: Windows 10 (build 26200), .NET SDK 10.0.204, Roslyn 5.9.0,
тест-хост 32-bit (`win-x86`), MSBuild `C:\Program Files (x86)\dotnet\sdk\10.0.204`,
`dotnet` host из x86 SDK. Запуск:

```text
dotnet test c:\Repos\RoslynMcpServer\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj --filter Category=AnalyzerLifecycle
```

Хост: `RoslynMcpServer.LifecycleTestHost` (изолированный процесс, production
`SolutionManager` + MSBuild bootstrap). Fixture: `%TEMP%\RoslynMcpServer.Epoch1`.
Shadow root: `%TEMP%\RoslynMcpServer.AnalyzerShadowCopy`.

Oracle: `Project.GetCompilationAsync` → тип `GeneratedMarker` → поле `Version` →
`IFieldSymbol.ConstantValue`. Пустые diagnostics не являются oracle версии.
Assertions целевого oracle не ослаблялись.

Повторный прогон только foreign + multi-consumer (после сборки generator DLL
вместо solution `dotnet build` Consumer): все три **pass** как запись oracle/path.

## Матрица E1-S2

| Случай | Результат | Evidence / следующая эпоха |
| --- | --- | --- |
| Базовая генерация, opt-in, точный V1, повтор без изменений | **pass** | Shadow path под temp; identity `Generator, Version=0.0.0.0`; `.csproj` неизменен |
| Отрицательный контроль, генератор недоступен (flag off, missing path) | **pass** | Oracle `no-type`, не V1; overlay = `artifacts\Generator\Generator.dll` |
| V1→V2 cached load | **fail** | cacheHit=true, reopen=false, prepare attempted, новый generation path; overlay = новый shadow, loaded = **старый** shadow; oracle `no-type`. Timestamp path ≠ content V2. U-ARB-02 / эпоха 3 |
| V1→V2 reset+load в том же процессе | **fail** | cacheHit=false, reopen=true, prepare attempted, новый shadow path; oracle `no-type`. Loader/CLR не сбрасывается reset. U-ARB-02 / эпоха 3 |
| V1→V2 process restart | **pass** | Точный V2; loaded = новый shadow |
| true→false / true→omitted sticky, затем reset | **pass** | Sticky v1.3.5: cached false/omitted оставляет ShadowEnabled; после reset overlay выключен. U-ARB-05 |
| false→true на cached load | **fail** | cacheHit=true, prepare+новый shadow path; oracle `no-type` до reset. Эпохи 2/3 |
| A opt-in → B same identity, overlay off, correct path | **fail** | Oracle маркера B вернул **A**. loaded = shadow B, identity `Generator, 0.0.0.0`. Коллизия CLR identity. Эпоха 3 / E1-03 |
| B broken path, flag off | **pass** | Oracle `no-type`, маркер не A и не B |
| Нет output, затем build и reload | **pass** | Первая prepare не Applied, overlay = broken artifacts path; после build+reset — V1 |
| Несколько Consumer | **pass** | Три rewrite Applied, общий shadow path, все Consumer видят V1. Первый полный прогон упёрся в 4-минутный host-response-timeout на `dotnet build`; после увеличения timeout до 8 мин и без зомби-хоста — 21 с |
| Foreign existing same filename | **pass (запись)** | Oracle определён: `no-constant`, marker пустой. overlay = `external\Generator.dll` (foreign), не shadow; generation path пуст (rewrite не Applied). Не инвариант matcher. U-ARB-01 / эпоха 5 |
| Foreign missing path | **pass (запись)** | Oracle определён: `no-constant`, marker пустой. overlay = `..\missing\Generator.dll`, workspace тот же relative path; generation path пуст. Evidence U-ARB-01: missing original не доказал in-solution исполнение |
| Overlay reapply + injected prepare failure | **fail** | `FailNextOverlayPrepare`: copier не вызывается; overlay заменён на `artifacts\Generator\Generator.dll`; oracle `no-type`; loaded остаётся прежний shadow. Эпоха 2 |
| Overlay reapply + forced `File.Copy` IOException | **fail** | Copier вызван, SkipReason `shadow copy failed`; overlay = `artifacts\Generator\Generator.dll`; oracle `no-type`. Иной код-путь, тот же продуктовый провал. Эпоха 2 |

Concurrent load+enable на одной сессии: **pass**, маркер V1.

## Запись E1-S3

| Путь | Redirected missing path | SDK default correct path |
| --- | --- | --- |
| `UpdateDocumentInMemoryAsync` + запись файла | **fail** — после edit oracle `no-type` (overlay сброшен на broken original). `.csproj` hash / forced rebuild не достигнуты. Эпоха 2 | **pass** — marker V1; overlay = workspace original `bin\...\Generator.dll`; loaded = shadow; `<Analyzer Include>` нет; DLL hash изменился (`91C4A98E…` → `2A534A6A…`) |
| Overlay apply / rename | **fail** — после `applyOverlayEdit` oracle `no-type`; rename не достигнут. Эпоха 2 | **pass** — `SameSnapshotAfterSymbol=True`; marker V1; overlay = original bin; loaded = shadow; `<Analyzer Include>` нет; DLL hash изменился (`00169414…` → `DC44B060…`) |
| FSW dirty wait + `FindDocumentAsync` flush | **fail** — dirty **доставлен**, flush **опубликовал** текст `fsw-`; затем oracle `no-type` (не timeout dirty, не flush-miss). Эпоха 2 | **pass** — marker V1; overlay = original bin; loaded = shadow; `<Analyzer Include>` нет; DLL hash изменился (`081E8454…` → `3A37C769…`) |

На correct-path успешные ячейки держат маркер, потому что original path существует;
overlay после write совпадает с workspace original, не с shadow. Это не доказательство
сохранения overlay mapping.

Fallback / rejected `TryApplyChanges` / per-file I/O fail / cancellation /
reconciliation **не покрыты** этим прогоном — deferred в эпоху 4 (E4-S2/S4). E1-S3
«добавить fallback» не выполнялось отдельным fault-injection.

## Согласованность E1-S4

Инвентаризация читателей: `epoch-1-semantic-entry-points.md` +
`Epoch1SemanticInventoryTests` (**pass**). Getter callers не исключены.

Rename: на SDK-ячейке write-path `SameSnapshotAfterSymbol=True` (document solution
совпал с `GetCurrentSolution()` после вычисления symbol). Redirected-ячейка rename
не достигла. Inventory остаётся статическим дополнением.

Helper-only change **не в обязательной матрице**. Optional E1-S4 / U-ARB-03 / эпоха 3:
тест без ссылки helper из Generator не измеряет prepare/binding/execution и удалён.

## Как гонять

```text
dotnet test c:\Repos\RoslynMcpServer\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj --filter Category=AnalyzerLifecycle
```

Перед rebuild убить leftover `RoslynMcpServer.LifecycleTestHost` (MSB3027 / hung build).
Недоступный MSBuild bootstrap → Skip с причиной, не passed.
Не запускать полный filter через MCP: протокол `-32001` обрывает suite раньше записи строк.
