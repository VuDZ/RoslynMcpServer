# Приёмка эпохи 1

Дата: 2026-09-11. Вердикт: **принимается как измеренный красный baseline**.
Норматив: [epoch-1-lifecycle-verification.md](epoch-1-lifecycle-verification.md) E1-S1–S5,
[epoch-1-results.md](epoch-1-results.md), [epoch-1-semantic-entry-points.md](epoch-1-semantic-entry-points.md).
Полный `dotnet test --filter Category=AnalyzerLifecycle` выполнен вне MCP-timeout
(~14 мин, 24 теста: 12 pass / 12 fail на первом прогоне; foreign + multi-consumer
дозаписаны отдельным прогоном 3/3). Failures сохранены с oracle/path/identity и gate.
Эпоху 2 можно начинать. Patch bump и изоляция test seams — checklist мержа (A1-10),
не условие закрытия baseline.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| A1-01 | Blocker | Сборка тестового проекта | **закрыт** |
| A1-02 | Blocker | Непрогнанные обязательные строки | **закрыт** |
| A1-03 | High | Write-path 3×2 | **закрыт** (ячейки записаны) |
| A1-04 | High | U-ARB-01 evidence | **закрыт** (оба foreign прогнаны) |
| A1-05 | Medium | Inject seam ≠ File.Copy fail | **закрыт** (два случая в results) |
| A1-06 | Medium | Foreign asserts фиксируют unsafe matcher | **закрыт** |
| A1-07 | Medium | Helper-only не измеряет helper | **закрыт** (убран из матрицы) |
| A1-08 | Medium | Rename re-get без assert | **закрыт** |
| A1-09 | Low | Fallback/reconciliation E1-S3 | **закрыт** (deferred эпоха 4) |
| A1-10 | Low | Version / прод-шовы | **открыт**, checklist мержа |

---

ID: A1-01
Severity: Blocker
Category: Reproducibility
Status: **закрыт**

Target:
RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj
RoslynMcpServer.csproj Compile Remove LifecycleTestHost

Claim:
Эпоха завершается воспроизводимыми тестами. Команда запуска: `dotnet test … --filter Category=AnalyzerLifecycle`. Недоступное окружение — skip, не passed.

Evidence (было):
Явный `<Compile Include="AnalyzerLifecycle\AnalyzerLifecycleCollection.cs" />` дублировал SDK glob → NETSDK1022. В `RoslynMcpServer.csproj` ошибочный Compile Include хоста давал CS8802.

Resolution:
Оба ItemGroup Include удалены. Остаются только `Compile/None Remove` для Tests и LifecycleTestHost. `dotnet build` Tests: 0 Error(s). Filter стартует.

---

ID: A1-02
Severity: Blocker
Category: MatrixCoverage
Status: **закрыт**

Target:
epoch-1-lifecycle-verification.md E1-S2
epoch-1-results.md

Claim:
Эпоха завершается результатами всех обязательных строк, включая failures. Непрогон не заменяет измеренный failure. Assertions не ослабляются.

Evidence (было):
Частичный MCP-прогон: B broken path, multi-consumer, foreign, класс целиком — «не доведён» / timeout.

Resolution:
Полный filter вне MCP. Каждая строка E1-S2 в [epoch-1-results.md](epoch-1-results.md): pass или fail с cacheHit/reopen/prepare/generation/oracle/path/identity и gate. `Assert.Equal(V2)` на cached/reset не ослаблялся. Multi-consumer: первый прогон — host-response-timeout 4 мин на build (записан как fail); после timeout 8 мин и без zombie host — **pass** за 21 с.

---

ID: A1-03
Severity: High
Category: WritePath
Status: **закрыт** (ячейки записаны; продукт на missing-path красный)

Target:
epoch-1-lifecycle-verification.md E1-S3
Epoch1WritePathTests.cs
epoch-1-results.md

Claim:
Три пути записи независимо, на missing-path и existing-correct-path. После каждого — маркер, новый текст, `.csproj` bytes, forced rebuild с записью DLL. Watcher: dirty event timeout vs flush failure.

Resolution:
Шесть ячеек:

| Путь | Redirected missing | SDK default |
| --- | --- | --- |
| text-edit | fail, oracle `no-type` после edit; hash DLL не достигнут | pass, V1, hash DLL сменился, `<Analyzer Include>` нет |
| overlay apply/rename | fail, oracle `no-type` после apply; rename не достигнут | pass, `SameSnapshotAfterSymbol=True`, hash сменился |
| FSW | fail: dirty доставлен, flush опубликовал текст, затем oracle `no-type` | pass, V1, hash сменился |

На missing-path это тот же overlay-drop, что A1-05 / эпоха 2. На correct-path маркер жив, потому что original path существует; overlay после write = workspace original, не shadow.

---

ID: A1-04
Severity: High
Category: ProvenanceEvidence
Status: **закрыт** как evidence, не как политика

Target:
epoch-1-lifecycle-verification.md / внешний analyzer
UNRESOLVED-v2.md U-ARB-01

Claim:
Внешний одноимённый analyzer: существующий foreign path и missing foreign path. Результат missing — evidence U-ARB-01.

Resolution:
Оба теста прогнаны (Consumer `dotnet build` не требуется: existing даёт CS0101 двух генераторов, missing — CS0006 на `..\missing\Generator.dll`; собираются только generator DLL).

- Existing: oracle `no-constant`, marker пустой, overlay = `external\Generator.dll`, shadow не Applied.
- Missing: oracle `no-constant`, marker пустой, overlay = `..\missing\Generator.dll`.

Политика matcher не выбрана. Эпоха 5 / U-ARB-01 получают эти oracle/path.

---

ID: A1-05
Severity: Medium
Category: TestSeam
Status: **закрыт**

Target:
Services/SolutionManager.cs `FailNextOverlayPrepare`
Services/AnalyzerReferenceShadowCopier.cs `RemainingForcedCopyFailures`
Epoch1LifecycleMatrixTests overlay reapply tests

Claim:
Loaded shadow → edit → reapply с внедрённой ошибкой подготовки; нельзя молча заменить активные ссылки broken original.

Resolution:
Два случая, оба **fail** с одинаковым продуктовым эффектом (overlay → `artifacts\Generator\Generator.dll`, oracle `no-type`):

1. Inject seam: copier не вызывается (`FailNextOverlayPrepare`).
2. Forced copy: `CopyToShadowDirectory` бросает `IOException` до `File.Copy`; SkipReason `shadow copy failed`.

Эпоха 2 чинит оба: ноль prepare на edit + сохранение mapping при copy fail.

---

ID: A1-06
Severity: Medium
Category: OracleContract
Status: **закрыт**

Target:
Epoch1LifecycleMatrixTests.cs Foreign_*

Claim:
Проверить существующий foreign path; результат — наблюдение для U-ARB-01, не выбранная политика matcher.

Resolution:
`Assert.Equal(V1)` / `Assert.NotEqual(FOREIGN)` сняты. Остаётся: oracle определён (marker или `OracleFailure`) и пути записаны. Наблюдение: оба случая `no-constant`, overlay = foreign/missing path.

---

ID: A1-07
Severity: Medium
Category: Scope
Status: **закрыт**

Target:
epoch-1-lifecycle-verification.md E1-S4 helper-only (optional)

Claim:
Helper-only может измеряться после oracle; отдельно preparation, binding, execution; поддержку не объявлять.

Resolution:
Тест удалён из `Category=AnalyzerLifecycle`. Optional E1-S4 / U-ARB-03 отложены в эпоху 3: без ProjectReference/copy helper рядом с analyzer тест не разделял стадии.

---

ID: A1-08
Severity: Medium
Category: SnapshotBase
Status: **закрыт**

Target:
Epoch1WritePathTests overlay-apply rename
epoch-1-semantic-entry-points.md

Claim:
Rename с flush всё ещё требует проверки повторного чтения базы после вычисления symbol.

Resolution:
`Assert.True(rename.SameSnapshotAfterSymbol)` плюс запись в results. SDK-ячейка: **True**. Redirected-ячейка rename не достигла (oracle после apply). Inventory-тест по-прежнему статический.

---

ID: A1-09
Severity: Low
Category: Scope
Status: **закрыт** (явный defer)

Target:
epoch-1-lifecycle-verification.md E1-S3 fallback/reconciliation

Claim:
Для каждого write path добавить fallback/reconciliation эпохи 4.

Resolution:
В [epoch-1-results.md](epoch-1-results.md): fallback / rejected `TryApplyChanges` / per-file I/O / cancellation / reconciliation **не покрыты** прогоном эпохи 1, deferred E4-S2/S4. Отдельный fault-injection `ApplySolutionChangesToDiskAsync` в эпоху 1 не добавлялся.

---

ID: A1-10
Severity: Low
Category: ProductSurface
Status: **открыт** — checklist мержа, baseline не блокирует

Target:
RoslynMcpServer.csproj Version 1.3.5
Services/SolutionManager.cs FailNextOverlayPrepare, LastPrepare*, OverlayPrepareCount
Services/AnalyzerReferenceShadowCopier.cs RemainingForcedCopyFailures
Services/InProcessAnalyzerAssemblyLoader.cs SnapshotLoadedAssemblies
InternalsVisibleTo LifecycleTestHost

Claim:
Эпоха 1 — измерение, не выпуск новой функции. Новые публичные MCP параметры не вводятся.

Evidence:
Публичный MCP schema не менялся. Internal test seams и InternalsVisibleTo хоста в production assembly. Версия пакета не bump.

При мерже:
1. Patch bump по правилу репозитория, **или**
2. Удержать шовы так, чтобы `FailNextOverlayPrepare` / `RemainingForcedCopyFailures` не оставались бессмысленными в ship-бинарнике.

Не блокирует закрытие baseline и старт эпохи 2.
