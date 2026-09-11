# Приёмка эпохи 2

Дата: 2026-09-11. Вердикт: **принимается** (A2-09 открыт, не блокер mapping).
Норматив: [epoch-2-immutable-shadow-copies.md](epoch-2-immutable-shadow-copies.md) E2-S1–S5.
Прогон: [epoch-2-results.md](epoch-2-results.md).

Повторная независимая приёмка после правок A2-01…A2-08 (битность testhost, containment,
kill-mid-publish, disk-full по стадиям, DOC-EARLY). Самоприёмка не засчитывалась;
этот прогон воспроизвёл E2-S5.

CLR / V2 execution по-прежнему эпоха 3. Inverse write-boundary — эпоха 4.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| A2-01 | Blocker | Lifecycle suite не discoverится | **закрыт** |
| A2-02 | High | E2-S5 lifecycle не прогнан | **закрыт** |
| A2-03 | High | Самоприёмка vs epoch-1-results | **закрыт** |
| A2-04 | High | ARCHITECTURE / historical README лгут про overlay | **закрыт** (DOC-EARLY) |
| A2-05 | Medium | `TryDeleteRoot` без containment | **закрыт** |
| A2-06 | Medium | Two-process не kill mid-publish | **закрыт** |
| A2-07 | Low | Disk-full inject только staging-create | **закрыт** |
| A2-08 | Low | Измерения роста без записанных байт | **закрыт** |
| A2-09 | Low | Requested/prepared/active не раздельные stores | **открыт**, не блокер mapping |
| A2-10 | — | Publisher unit E2-S3 | **закрыт** (18/18) |
| A2-11 | — | ApplyMapping без analyzer I/O | **закрыт** (unit + production write-path) |
| A2-12 | Low | Catalog snapshot bytes | **закрыт** |

Не в скоупе эпохи 2: U-ARB-02/03, A→B, V1→V2 marker, matcher U-ARB-01, inverse write-boundary (эпоха 4).

---

## Что лежит в дереве

- `AnalyzerShadowGenerationPublisher` + `AnalyzerShadowMapping`: prepare отдельно от `Solution` transform.
- `ApplyShadowCopyOverlayIfEnabled` вызывает `mapping.Apply`, не publisher.
- Prepare: `ShadowCopyInSolutionAnalyzerReferencesAsync` (load/enable/refresh).
- Политика `v2-main-only`, content hash, staging → manifest last → `Directory.Move` без replace.
- Failed refresh: stale mapping; inject на edit не является refresh.
- `ClearWorkspaceAsync` обнуляет mapping, опубликованные каталоги не удаляет.
- `TryDeleteRoot`: containment под известный parent.
- Tests + LifecycleTestHost: `PlatformTarget=x64`.
- Версия csproj **1.3.6**.

## Прогоны этой приёмки

64-bit `C:\Program Files\dotnet\dotnet.exe`. `DOTNET_MULTILEVEL_LOOKUP=0`.

Повтор (независимый, после правок harness):

```text
AnalyzerShadowGenerationPublisherTests → 18 passed / 0 failed
AnalyzerReferenceShadowCopierTests → 5 passed / 0 failed
SolutionManagerAnalyzerOverlayTests → 3 passed / 0 failed (revert guard)

Epoch2 | WritePath | Overlay_reapply
→ 11 passed / 0 failed. 2 m 9 s. Не MCP.

Category!=AnalyzerLifecycle → catalog snapshot обновлён; `Epoch4_surface_sizes_match_recorded_release_numbers` pass (63 / 43895)

V1_to_V2_cached_load → fail 14 s, oracle no-type (эпоха 3, assertions не ослаблены)
  overlay = v2-main-only\C7DC4AFF…  loaded = v2-main-only\FF2E34FB… (старое поколение)
```

Подробные oracle/path/байты первого зелёного E2-S5: [epoch-2-results.md](epoch-2-results.md).

## Архитектура и логика (повтор)

Граница эпохи 2 соблюдена: prepare на load/refresh, edit/FSW/post-apply только `mapping.Apply`.
`GetCurrentSolution()` не recopy. `ARCHITECTURE.md` и historical README это описывают как v1.3.6;
timestamp + recompute-on-read помечены как v1.3.5 history.

`TryApplyChanges`: text/FSW строят candidate с `workspace.CurrentSolution` (без overlay);
overlay-derived apply идёт через `RevertAnalyzerReferenceOverlayForApply`, затем снова mapping.
Guard 3/3. Exact inverse — эпоха 4 (`SequenceEqual` wipe списка не менялся).

Иммутабельность поколений: V1→V2 cached публикует **новый** hash-каталог, старый path остаётся
в loader (`loaded ≠ overlay`). Mapping/файлы эпохи 2 работают; CLR identity — эпоха 3, не регресс.

Matcher по имени DLL не менялся (U-ARB-01 / эпоха 5). Sticky `false`/`omitted` — U-ARB-05.

---

ID: A2-01
Severity: Blocker
Category: Reproducibility
Status: **закрыт**
Confidence: High

Target:
RoslynMcpServer.Tests.csproj `PlatformTarget=x64`, `Prefer32Bit=false`
AnalyzerLifecycleFactAttribute / AnalyzerLifecycleTheoryAttribute (Skip на FileNotFound/BadImageFormat/TypeLoad)

Claim:
Эпоха заканчивается воспроизводимыми тестами E2-S5, включая MSBuildWorkspace host.

Evidence:
Discovery: xUnit adapter `64-bit .NET 10.0.11`, 11 lifecycle tests **passed** (не Skip, не 1 ms FileNotFound).
PE Tests.dll и LifecycleTestHost.dll = AMD64.

---

ID: A2-02
Severity: High
Category: MatrixCoverage
Status: **закрыт**
Confidence: High

Target:
E2-S5; Epoch2ImmutableShadowTests; Epoch1WritePathTests; Overlay_reapply_*

Claim:
Edit/flush/post-apply сохраняют mapping и маркер при нуле analyzer I/O, в том числе missing original.
Cached refresh с теми же size/timestamp даёт новую identity.
Два процесса, один root: kill publisher + survivor reuse.

Evidence:
Все шесть write-path: marker V1, overlay = `v2-main-only\<hash>\Generator.dll`, `OverlayPrepareCount 1→1`, `AnalyzerFileIoCount 8→8`.
Cached: generation `191302CB…` → `65235AD3…`.
Kill-mid-publish: survivor `ReusedExisting=true`, `GenerationBytes=1471`.
Overlay reapply inject и `File.Copy` fail: marker V1, те же counts, shadow path не сброшен на original.

---

ID: A2-03
Severity: High
Category: Evidence
Status: **закрыт**
Confidence: High

Target:
epoch-2-results.md; epoch-1-results.md (добавлен повтор x64); FOLLOWUPS F-04

Claim:
Write-path 6/6 и inject/copy-fail reapply зелёные на записанном прогоне.

Evidence:
[epoch-2-results.md](epoch-2-results.md) и секция повтора в [epoch-1-results.md](epoch-1-results.md).
Исходный 32-bit baseline эпохи 1 сохранён; полный `Category=AnalyzerLifecycle` не переписывался (V1→V2 / A→B остаются эпохи 3).

---

ID: A2-04
Severity: High
Category: Documentation
Status: **закрыт**
Confidence: High

Target:
docs/ARCHITECTURE.md workspace lifecycle
docs/analyzer-shadow-copy/README.md current-state
E6-S1 DOC-EARLY
SolutionManager remarks / overlay field comments

Claim:
Документация текущего shipped поведения совпадает с кодом v1.3.6.

Evidence:
ARCHITECTURE: prepare at load/refresh; getter не recopy; mapping reapply; timestamp layout = v1.3.5 history.
Historical README: отдельный блок v1.3.6 vs history v1.3.5.

---

ID: A2-05
Severity: Medium
Category: Ownership
Status: **закрыт**
Confidence: High

Target:
Services/AnalyzerShadowCacheCleanup.cs `TryDeleteRoot`

Claim:
Cleanup проверяет resolved absolute target, containment и владение.

Evidence:
`IsUnderAllowedParent`: `%TEMP%\RoslynMcpServer.AnalyzerShadowCopy` / `.Tests` / `.Epoch1`.
`Cleanup_refuses_path_outside_allowed_parent_even_when_owners_stopped`: **pass**.
`allOwningProcessesStopped: false` по-прежнему отказ.

---

ID: A2-06
Severity: Medium
Category: PublicationProtocol
Status: **закрыт**
Confidence: High

Target:
Epoch2ImmutableShadowTests.Two_processes_share_one_root_kill_mid_publish_then_survivor_reuses

Claim:
Межпроцессная гонка + kill publisher во время BeforeMove + survivor verify/reuse.

Evidence:
Host A: `GatePath` + `BeforeMove` wait, staging виден. Host B публикует. Dispose/Kill A без открытия gate. Survivor `ReusedExisting`. Два процесса LifecycleTestHost, не unit-потоки.
`generationBytes=1471`, `rootBytes=2942` (поколение + leftover staging).

---

ID: A2-07
Severity: Low
Category: FaultInjection
Status: **закрыт**
Confidence: High

Target:
AnalyzerShadowGenerationPublisherTests.Disk_full_at_copy_manifest_or_move_does_not_activate_partial_staging

Claim:
Disk-full на copy, manifest и move не активирует partial staging.

Evidence:
Theory `InlineData("copy"|"manifest"|"move")`, 3/3 **pass**. `ForcedDiskFullStage`. Partial dest не появляется.

---

ID: A2-08
Severity: Low
Category: ResourceBudget
Status: **закрыт**
Confidence: High

Target:
E2-S4 измеренные значения в документе приёмки

Claim:
Измерены bytes поколения, суммарный root, рост same vs changed content.

Evidence:
Unit: GenerationBytes 682; reuse root 938→938; changed 1620.
Lifecycle kill-mid: GenerationBytes 1471, rootBytes 2942.
Бюджет: уникальный hash → одно поколение + один staging; leftover staging не published; cleanup после stop всех владельцев под allowed parent. Авто-GC нет.
Числа: [epoch-2-results.md](epoch-2-results.md).

---

ID: A2-09
Severity: Low
Category: Model
Status: **открыт**, не блокер mapping
Confidence: Medium

Target:
E2-S1 раздельное хранение requested / prepared / active / last refresh / observed execution

Evidence:
`_shadowCopyAnalyzersEnabled` совмещает requested и active. Mapping = prepared+active. `_lastRefreshStale` / `_lastShadowCopyResults` — последний refresh. Наблюдаемое исполнение не хранится (эпоха 3).
Удержание mapping на время незавершённой записи — эпоха 4. Комментарий в `SolutionManager` фиксирует, что один enabled bool — не пять состояний для новых API.

Suggested change:
Не блокировать mapping/reapply. Эпоха 4: база операции держит mapping. Не выдавать один enabled bool за пять состояний в новых публичных API.

---

ID: A2-10
Severity: —
Category: PublicationProtocol
Status: **закрыт**

`AnalyzerShadowGenerationPublisherTests` 18/18: reuse без overwrite; новая identity при тех же size/timestamp; interrupted staging; missing/corrupt file; unsafe path; wrong policy; move fail; PDB fail не патчит published; source-unstable; timestamp layout не v2; forced copy fail; cleanup flag; disk-full copy/manifest/move; containment cleanup.

Нет отдельного теста corrupt JSON manifest (есть missing + deserialize catch). Не блокер.

---

ID: A2-11
Severity: —
Category: OverlayReapply
Status: **закрыт**

Unit `ApplyMapping_does_not_read_analyzer_files_and_survivives_missing_source` **pass**.
Production write-path 6/6 и Overlay_reapply_* : `AnalyzerFileIoCount` не растёт на edit, маркер V1, shadow path сохранён.

---

ID: A2-12
Severity: Low
Category: Regression
Status: **закрыт**
Confidence: High

Target:
RoslynMcpServer.Tests/McpToolCatalogTests.cs `Epoch4_surface_sizes_match_recorded_release_numbers`
Tools/WorkspaceTools.cs `load_workspace` Description

Claim:
Смена описания overlay не ломает остальной test surface. Число MCP tools стабильно.

Evidence:
Description `load_workspace` добавило предложение про immutable generations / mapping reapply (+276 UTF-8).
Бюджетный cap не пробит. Recorded sizes переписаны: full 63 / **43895**; lite и lite+group — та же дельта +276.
`Epoch4_surface_sizes_match_recorded_release_numbers` **pass**. Description не откатывалось.

---

## Порядок дальше

1. F-05 / эпоха 4 — missing-path write-path зелёный.
2. A2-09 не требует нового public API в эпохе 2.
3. Эпохи 3 и 5 не открывать. U-ARB-02/03/05 остаются открытыми.
