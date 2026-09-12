# Доработки после эпохи 1

Эпоха 1 закрыта как **измеренный красный baseline**
([epoch-1-results.md](epoch-1-results.md), [epoch-1-acceptance.md](epoch-1-acceptance.md)).
Ниже — оставшаяся работа. Эпохи 2–4 и 6 закрыты. Эпоха 5: политика U-ARB-01
выбрана (capture), production snapshot [принят](epoch-5-f09-production-capture-acceptance.md);
matcher ждёт E5-S2.

| ID | Когда | Что |
| --- | --- | --- |
| F-01 | сразу, до эпохи 2 | Зафиксировать harness: x64 host, 45 с RPC, Kill на dispose |
| F-02 | сразу | Перепрогнать `Category=AnalyzerLifecycle`, обновить results (убрать x86 / 8 мин) |
| F-03 | **сделано** | DOC-EARLY: ARCHITECTURE / historical README / remarks = stored snapshot (аудит E6-S1) |
| F-08 | **принято** ([epoch-6-acceptance.md](epoch-6-acceptance.md)) | Эпоха 6: аудит S1–S4. Серия не завершена. A6-13 не блокер |
| F-04 | **принято** ([epoch-2-acceptance.md](epoch-2-acceptance.md)) | E2-S5 11/11. Открыт A2-09 (stores). A2-12 закрыт: catalog 63 / 43895 |
| F-05 | **принято** ([epoch-4-acceptance.md](epoch-4-acceptance.md)) | Эпоха 4: write boundary. Открыты A4-09…A4-12 (не блокеры) |
| F-06 | мерж в main | A1-10: version bump, изоляция test seams |
| F-07 | **эпоха 3 принята** ([epoch-3-acceptance.md](epoch-3-acceptance.md)) | restart-required / main-only. Matcher эпохи 5 и U-ARB-05 sticky не открывать |
| F-09 | **принято** ([epoch-5-f09-production-capture-acceptance.md](epoch-5-f09-production-capture-acceptance.md)) | Snapshot v1.3.9. Открыты F09-PROD-1/2. Matcher нет |

---

ID: F-01
Severity: High
Depends: —

Target:
RoslynMcpServer.LifecycleTestHost.csproj
LifecycleHostClient.cs
HostSession.BuildAsync
AnalyzerLifecycleFactAttribute.cs

Work:
Полный прогон эпохи 1 занял ~50 мин: self-contained **win-x86** host → `Program Files (x86)\dotnet`, `dotnet build` зависал; клиент ждал 8 мин RPC, Dispose слал `exit` в тот же stdin ещё 8 мин.

Уже начато в дереве (проверить, что в коммите):
- хост framework-dependent, `PlatformTarget=x64`, `UseAppHost=false`;
- запуск только `dotnet exec` 64-bit, не x86 `.exe`;
- `FindHostDll` отбрасывает `win-x86` на 64-bit процессе;
- RPC и `dotnet build` **45 с**;
- `Dispose` сразу `Kill(entireProcessTree)`, без `exit` RPC;
- probe skip **15 с**.

Добить: не оставлять `SelfContained=true` на test host; не выбирать newest DLL по timestamp из `win-x86`. Перед rebuild убивать leftover `RoslynMcpServer.LifecycleTestHost`.

Done when:
`LifecycleTestHost` в логе env — 64-bit, `C:\Program Files\dotnet\dotnet.exe`. Зависший `build` даёт fail ≤45 с, не 15 мин.

---

ID: F-02
Severity: High
Depends: F-01

Target:
epoch-1-results.md (строки 4–5, 77–81)

Work:
Results записаны на 32-bit host и 8-минутном timeout. После F-01 полный filter **локально**, не через MCP (`-32001`).

Ожидаемые красные (не чинить в этом шаге): missing-path write paths `no-type`; inject/`File.Copy` fail; V1→V2 cached и reset+load; false→true cached `no-type`; A→B маркер A.

Если suite > нескольких минут на тест — снова harness, не эпоха 2.

Done when:
Results: битность/SDK path, длительность suite, те же строки матрицы. Раздел «8 мин / зомби-хост» актуализирован.

---

ID: F-03
Severity: Medium
Depends: —

Target:
docs/ARCHITECTURE.md workspace lifecycle
docs/analyzer-shadow-copy/README.md
SolutionManager.ShadowCopyInSolutionAnalyzerReferencesAsync remarks
[E6-S1](epoch-6-contract-and-documentation.md)

Work:
В v1.3.5 `GetCurrentSolution()` возвращает `_solution` с fallback на `workspace.CurrentSolution`. Чтение getter не пересчитывает overlay. Подготовка — в load/enable и (пока) после document apply. Исторические эпохи оставить историей с оговоркой.

Не обещать «новый path = новая CLR assembly». Не ждать эпоху 6.

Done when:
Три targets совпадают с кодом; DOC-EARLY не отмечен выполненным, пока файлы не изменены.

Сделано 2026-09-11: `docs/ARCHITECTURE.md`, `docs/analyzer-shadow-copy/README.md`, remarks/`SolutionManager` overlay comments описывают v1.3.6 mapping (prepare на load/refresh, getter не recopy). Timestamp-layout оставлен как история v1.3.5.

---

ID: F-04
Severity: High
Depends: F-01 (желательно F-02)

Target:
[epoch-2-immutable-shadow-copies.md](epoch-2-immutable-shadow-copies.md)
Красные эпохи 1: write-path missing-path; inject prepare fail; File.Copy IOException; false→true `no-type` (если recopy)

Work:
Продуктовый следующий шаг.

1. Подготовка файлов отделена от transform `Solution`.
2. В памяти mapping: session, `ProjectId`, original↔shadow, generation id, per-ref result.
3. Prepare только load/enable и явный artifact refresh/reopen. Edit, FSW flush, post-apply, reconciliation — **только reapply mapping, ноль analyzer file I/O**.
4. Main-only content identity (hash DLL), immutable publish, без overwrite loaded shadow.
5. Fail prepare на edit не существует: mapping сохраняется, маркер V1 на missing-path после text/overlay/FSW.
6. Inject и реальный `File.Copy` fail — оба зелёные по маркеру/shadow path.
7. Timestamp-каталоги не мигрировать; новый namespace.

Не делать: ALC, helper DLL в identity, auto-GC, in-process V2, смену matcher.

Целевые тесты эпохи 1, которые должны стать pass: три missing-path write-path; оба reapply-fail. V1→V2 cached/reset и A→B **остаются fail** (эпоха 3).

Done when:
E2-S5 по файлам/mapping **и** прогнанный lifecycle (write-path missing-path зелёные, `OverlayPrepareCount` на edit не растёт).

Сделано 2026-09-11: приёмка **принята** ([epoch-2-acceptance.md](epoch-2-acceptance.md), прогон [epoch-2-results.md](epoch-2-results.md)). Write-path 6/6: `OverlayPrepareCount 1→1`, `AnalyzerFileIoCount 8→8`, marker V1. V1→V2 cached/reset и A→B остаются fail (эпоха 3). A2-09 не блокер.

---

ID: F-05
Severity: High
Depends: F-04

Target:
[epoch-4-workspace-write-boundary.md](epoch-4-workspace-write-boundary.md)

Work:
Единый workflow: preflight → точный inverse overlay по mapping → запись → reconciliation → publish overlay без analyzer I/O. Unknown analyzer diff отклонять **до** любых записей. Fallback/reconciliation, отложенные в E1-S3, закрываются здесь.

Не делать: MVCC, load-isolation redesign (U-ARB-04) без воспроизведённой утечки.

Done when:
E4-S4: inventory `TryApplyChanges`, exact inverse tests (не whole-list wipe), 3 write path + fallback.

Сделано 2026-09-11: приёмка **принята** ([epoch-4-acceptance.md](epoch-4-acceptance.md),
прогон [epoch-4-results.md](epoch-4-results.md)). Независимый прогон: unit 10/10,
lifecycle+регресс 15/15. A4-09…A4-12 не блокеры.

---

ID: F-06
Severity: Low
Depends: любой мерж кода server

Target:
RoslynMcpServer.csproj Version
SolutionManager FailNextOverlayPrepare / LastPrepare*
InProcessAnalyzerAssemblyLoader.SnapshotLoadedAssemblies
InternalsVisibleTo LifecycleTestHost

Work:
Checklist мержа (A1-10): patch bump при изменении server; шовы тестов не обязаны торчать в ship-API без нужды (`internal` + InternalsVisibleTo достаточно). Новые MCP параметры не вводить.

Done when:
Версия в csproj = `get_mcp_server_info` после publish; README agent-tools row если поведение tools менялось (эпоха 2 — да, если overlay больше не теряется).

---

ID: F-07
Severity: — (запрет)
Depends: evidence gates

Сделано 2026-09-11: эпоха 3 выбрала и реализовала restart-required + main-only
отказ. ALC и production helper discovery не открывать без новой спецификации.

Не делать в ближайшем чате:

- ALC / in-process V2 / production helper discovery сверх main-only отказа.
- Эпоха 5 rollout / смена matcher до принятого capture design (F-09).
  Политика U-ARB-01 уже выбрана; Alt-2/Alt-3 не внедрять «на всякий случай».
- U-ARB-05: менять sticky `false`/`omitted` на cached load.
- Ослаблять oracle assertions ради зелёного filter.

---

ID: F-09
Severity: High
Depends: U-ARB-01 выбран (capture); [epoch-5-s1-results.md](epoch-5-s1-results.md)

Target:
[UNRESOLVED-v2.md](UNRESOLVED-v2.md) U-ARB-01
[epoch-5-reference-provenance.md](epoch-5-reference-provenance.md) (эскиз, не S2 rollout)
Новый файл: `docs/analyzer-shadow-copy-improvements/v2/epoch-5-f09-capture-design.md`

Work:
Эскиз **только захвата** design-time provenance на `load_workspace` / явной
смене графа. Нужно: откуда взять `%(Analyzer.MSBuildSourceProjectFile)` и
effective Configuration / inner TFM, не теряя их на границе BuildHost
`/analyzer:<path>`; ключ item → загруженный inner `Project`; когда
пересчитывать snapshot (load / graph-stale / смена global properties);
оценка цены (hook той же design-time загрузки vs `ProjectInstance` того же
graph vs второй `dotnet msbuild` как prototype).

Не делать в F-09:
- смену matcher / таблицу E5-S2 в коде;
- внедрение Alt-2 (unique-name) или Alt-3 (exact path only);
- выбор inaccessible;
- semantic-path evaluation.

Done when:
Файл эскиза отвечает: канал, входы, lifetime snapshot, отказ/skip без
metadata, почему это не второй полный eval «на каждый load» (или честно
что prototype так и делает). Приёмка эскиза отдельно; без неё E5-S2
запрещён. Если канал нереализуем — не молча откатываться: владелец пишет
Alt-2 или Alt-3 в U-ARB-01.

Сделано 2026-09-12:
[epoch-5-f09-capture-design.md](epoch-5-f09-capture-design.md) выбирает
production-кандидатом `BinaryLogger` той же `MSBuildWorkspace` design-time
загрузки и replay после `Open*Async`; отдельный target/evaluation pass не
выполняется. Зафиксированы inputs, exact item→consumer/source inner `ProjectId`
join, lifetime, fail-closed правила, цена/секретность binlog и P0-spike.
`ProjectInstance` и второй `dotnet msbuild` не выбраны production fallback.
Matcher, Alt-2/Alt-3, inaccessible и semantic-path evaluation не менялись.
Приёмка эскиза **принята** ([epoch-5-f09-acceptance.md](epoch-5-f09-acceptance.md)).
P0 **принят** ([epoch-5-f09-p0-acceptance.md](epoch-5-f09-p0-acceptance.md)):
независимый прогон 4/4. Production capture/snapshot
[принят](epoch-5-f09-production-capture-acceptance.md): F09-04 и P0-7 закрыты.
Открыты F09-PROD-1 (join matrix не через production `Bind`) и F09-PROD-2.
Matcher не менять до отдельного E5-S2.

---

Порядок в следующем чате: E5-S2 / смена matcher + marker acceptance.
Join доказывать через production snapshot (F09-PROD-1), не через spike.
F-07 не открывать. A4-09…A4-12 и A6-13 не чинить без отдельного запроса.
Серия незавершена.
