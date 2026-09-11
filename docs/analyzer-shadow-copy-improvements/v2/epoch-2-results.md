# Эпоха 2 — результаты прогона E2-S5

Дата: 2026-09-11. Статус: **записанный прогон** к [epoch-2-acceptance.md](epoch-2-acceptance.md).
Assertions oracle не ослаблялись.
Повтор той же команды E2-S5 (независимая приёмка): **11 passed / 0 failed**, 2 m 9 s, x64 `C:\Program Files\dotnet`.

## Окружение

- Windows 10 (build 26200), .NET SDK 10.0.204, Roslyn 5.9.0
- `C:\Program Files\dotnet\dotnet.exe` (x64). testhost: xUnit VSTest Adapter **64-bit .NET 10.0.11**
- PE: `RoslynMcpServer.Tests.dll` **AMD64**, `RoslynMcpServer.LifecycleTestHost.dll` **AMD64**
- MSBuild: `C:\Program Files\dotnet\sdk\10.0.204`
- Fixture: `%TEMP%\RoslynMcpServer.Epoch1`. Shadow: `%TEMP%\RoslynMcpServer.AnalyzerShadowCopy\…\v2-main-only\<sha256>\`

Не использовать x86 `dotnet` из PATH / `Program Files (x86)`: Testhost i386 не грузит x64 host (A2-01).
Полный `Category=AnalyzerLifecycle` через MCP не гонять (`-32001`).

```text
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
& 'C:\Program Files\dotnet\dotnet.exe' test `
  c:\Repos\RoslynMcpServer\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj `
  --filter 'FullyQualifiedName~AnalyzerShadowGenerationPublisherTests|FullyQualifiedName~AnalyzerReferenceShadowCopierTests'
& 'C:\Program Files\dotnet\dotnet.exe' test `
  c:\Repos\RoslynMcpServer\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj `
  --filter 'FullyQualifiedName~Epoch2ImmutableShadowTests|FullyQualifiedName~Epoch1WritePathTests|FullyQualifiedName~Overlay_reapply'
```

## Unit

| Filter | Результат |
| --- | --- |
| `AnalyzerShadowGenerationPublisherTests` | **18 passed** (включая disk-full copy/manifest/move и cleanup containment) |
| `AnalyzerReferenceShadowCopierTests` | **5 passed** (включая `ApplyMapping` без analyzer I/O) |

Рост (unit `Growth_same_content_does_not_add_generation_changed_content_does`):

| Метрика | Значение |
| --- | --- |
| `first.GenerationBytes` | 682 |
| root после reuse (same content) | 938 (не вырос) |
| `second.GenerationBytes` (changed content) | 682 |
| root после второго поколения | 1620 (> reuse) |

Root unit-теста включает исходные DLL под `_root/src`; бюджет — **одно опубликованное поколение на уникальный hash**, плюс место под один staging; reuse не добавляет поколение.

## Lifecycle E2-S5

**11 passed / 0 failed**, ~1.8 мин. xUnit 64-bit.

### Cached refresh, same size/timestamp, new identity

`Cached_refresh_same_size_timestamp_gets_new_content_identity`:

- first generation `191302CB5446BAA333A33F317C522E526EB860914D766EE683A44FAFAE0C954A`
- second generation `65235AD3594374CAC1021D912494CF40379110E4E8BC4B7C41B5A555610BF978`
- overlay = `…\v2-main-only\<second>\Generator.dll`

### Failed refresh + edit

`Failed_refresh_keeps_stale_mapping_and_edit_does_not_clear_it`: **pass**. Stale mapping, маркер V1, edit не refresh.

### Two-process kill mid-publish

`Two_processes_share_one_root_kill_mid_publish_then_survivor_reuses`: publisher A ждёт `BeforeMove` (gate file), survivor B публикует, A **Kill process tree** без открытия gate, B повторно `ReusedExisting`.

| Метрика | Значение |
| --- | --- |
| generation | `E9F3B5770C3ED062C018E05D96ED195837B7B00187122EA6E831A633F0A511E8` |
| `GenerationBytes` | 1471 |
| `ShadowRootBytes` после survivor | 2942 (опубликованное поколение + leftover staging убитого A) |
| first B `ReusedExisting` | false |
| survivor `ReusedExisting` | true |

### Write-path 3×2 (маркер V1, overlay = shadow, I/O не растёт)

Все шесть: `OverlayPrepareCount 1→1`, `AnalyzerFileIoCount 8→8`.

| Путь | Redirected missing path | SDK default correct path |
| --- | --- | --- |
| text-edit | **pass** overlay `…\v2-main-only\46A6B024…\Generator.dll` | **pass** overlay `…\v2-main-only\772FA872…\Generator.dll` |
| overlay-apply | **pass** overlay `…\v2-main-only\E6C1582D…\Generator.dll`; `SameSnapshotAfterSymbol=True` | **pass** overlay `…\v2-main-only\7F12C9DF…\Generator.dll`; `SameSnapshotAfterSymbol=True` |
| FSW + flush | **pass** overlay `…\v2-main-only\42931F5B…\Generator.dll` | **pass** overlay `…\v2-main-only\24A20075…\Generator.dll` |

`.csproj` без `<Analyzer Include>` shadow path. Forced rebuild пишет новые bytes в original DLL (anti-lock).

### Overlay reapply fault injection

| Тест | update / oracle |
| --- | --- |
| inject prepare failure | OverlayPrepareCount=1 AnalyzerFileIoCount=8; marker **V1**; overlay `…\DDB476FD…\Generator.dll` |
| forced `File.Copy` IOException | OverlayPrepareCount=1 AnalyzerFileIoCount=8; marker **V1**; overlay `…\EEE9CBC6…\Generator.dll` |

Edit не потребляет inject и не копирует analyzer files.

## Не в этом прогоне

Полный `Category=AnalyzerLifecycle` не гонялся. Ожидаемые красные эпохи 3 без изменения assertions: V1→V2 cached, V1→V2 reset+load, A→B same identity, false→true cached (U-ARB-05). CLR / V2 execution — эпоха 3. Inverse write-boundary — эпоха 4.

## Бюджет и cleanup

- Уникальный content hash → одно поколение + один staging на публикацию.
- Same-content refresh: reuse, рост root = 0 (unit: 938→938).
- Changed content: второе поколение (unit: 938→1620).
- Kill mid-publish: leftover `.staging-*` не активируется; survivor verify/reuse опубликованного.
- Cleanup: только под `%TEMP%\RoslynMcpServer.AnalyzerShadowCopy` / `.Tests` / `.Epoch1`, и только при `allOwningProcessesStopped: true`.
- Интервал: операторская проверка после остановки всех MCP/test-host владельцев root; не на `reset_workspace`.
- Авто-GC нет.
