# Доработки после эпохи 1

Эпоха 1 закрыта как **измеренный красный baseline**
([epoch-1-results.md](epoch-1-results.md), [epoch-1-acceptance.md](epoch-1-acceptance.md)).
Ниже — оставшаяся работа. Не начинать эпохи 3 и 5.

| ID | Когда | Что |
| --- | --- | --- |
| F-01 | сразу, до эпохи 2 | Зафиксировать harness: x64 host, 45 с RPC, Kill на dispose |
| F-02 | сразу | Перепрогнать `Category=AnalyzerLifecycle`, обновить results (убрать x86 / 8 мин) |
| F-03 | **сделано** | DOC-EARLY: ARCHITECTURE / historical README / remarks = v1.3.6 mapping |
| F-04 | **принято** ([epoch-2-acceptance.md](epoch-2-acceptance.md)) | E2-S5 11/11. Открыт A2-09 (stores). A2-12 закрыт: catalog 63 / 43895 |
| F-05 | после F-04 | Эпоха 4: write boundary на mapping |
| F-06 | мерж в main | A1-10: version bump, изоляция test seams |
| F-07 | не сейчас | U-ARB-02/03 loader, U-ARB-01 matcher, U-ARB-05 sticky flag |

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

Не делать в ближайшем чате:

- Эпоха 3 / U-ARB-02: V1→V2 same-identity, ALC, restart-required как реализация.
- U-ARB-03: production helper discovery.
- Эпоха 5 / U-ARB-01: смена matcher. Foreign results — evidence, не rollout.
- U-ARB-05: менять sticky `false`/`omitted` на cached load.
- Ослаблять oracle assertions ради зелёного filter.

---

Порядок в следующем чате: **F-05 (эпоха 4)** после принятой эпохи 2. F-02 полный `Category=AnalyzerLifecycle` по желанию (V1→V2/A→B останутся красными). F-06 на коммит server. F-07 не открывать.
