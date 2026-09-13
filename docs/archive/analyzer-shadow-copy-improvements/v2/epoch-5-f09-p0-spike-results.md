# F-09 P0-spike — runtime-проверка design-time provenance

Дата: 2026-09-12. Вердикт: **GO для binlog-канала на проверенной матрице**.
Независимая приёмка: [epoch-5-f09-p0-acceptance.md](epoch-5-f09-p0-acceptance.md).
Production capture и matcher этим spike не реализованы.

Норматив:
[capture design](epoch-5-f09-capture-design.md),
[приёмка эскиза](epoch-5-f09-acceptance.md),
[E5-S1](epoch-5-s1-results.md).

## Проверенная матрица

- Windows 10 build 26200, x64;
- .NET SDK 10.0.204;
- runtime MSBuild из SDK:
  `C:\Program Files\dotnet\sdk\10.0.204\Microsoft.Build.dll`,
  product/file version 18.3.3;
- `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0;
- compile-time MSBuild packages spike: 18.6.3, runtime assets исключены через
  `ExcludeAssets="runtime"` и загружаются из зарегистрированного toolset.

Runtime evidence закреплён в
`RoslynMcpServer.Tests/AnalyzerLifecycle/F09CaptureSpikeTests.cs`.
Spike не меняет `SolutionManager`, `AnalyzerReferenceShadowCopier`, matcher или
публичный MCP API.

## Вердикт по неизвестному P0

`MSBuildWorkspace.OpenSolutionAsync` и `OpenProjectAsync` с runtime-type
`Microsoft.Build.Logging.BinaryLogger` создали suffixed BuildHost binlog.
Replay через `BinaryLogReplayEventSource` получил
`TaskParameterEventArgs` со следующей наблюдаемой формой:

```text
Kind = TaskOutput
ItemType = Analyzer
Items[n] = ITaskItem
MSBuildSourceProjectFile = <absolute Generator.csproj>
NearestTargetFramework = netstandard2.0 | net10.0
```

Событие сохраняет `BuildEventContext` consumer-а. Вложенный
`ProjectStartedEventArgs.ParentProjectBuildEventContext` совпал с task context,
поэтому `Analyzer` output однозначно привязан к фактическому consumer project
context без filename matcher.

Project-produced item и explicit missing same-name `Analyzer` различимы:
первый получен с `MSBuildSourceProjectFile`, explicit reference остаётся в
загруженном Roslyn project, но production provenance output для него нет.
Отсутствие capture не переинтерпретируется как `foreign`.

## Exact join и поправка к эскизу

На single-TFM redirected fixture, Debug/Release и при `OpenProjectAsync` exact
join дал ровно один consumer и один source `ProjectId`:

1. event project file = `consumer.Project.FilePath`;
2. captured item identity = `consumer.AnalyzerReference.FullPath`;
3. `MSBuildSourceProjectFile` = `source.Project.FilePath`;
4. captured item identity = `source.Project.OutputFilePath`.

На multi-TFM fixture (`netstandard2.0;net10.0`) получены оба
`NearestTargetFramework`; каждый item связан ровно с одним source `ProjectId`,
и эти `ProjectId` различны.

Важная поправка: `ProjectStartedEventArgs.Properties` в измеренном nested
`GetTargetPath` context не содержал надёжных `TargetPath` /
`IntermediateAssembly`. Поэтому production join не должен требовать эти два
property как обязательный источник. Разрешённый exact hit:
`captured Analyzer.Identity == loaded Project.OutputFilePath`.
`IntermediateAssembly == CompilationOutputInfo.AssemblyPath` остаётся
дополнительным exact hit, если значение реально пришло. Mismatch одной пары не
является конфликтом, пока он не указывает на другой `ProjectId`.

Если exact source path дал несколько inner projects, но ни TFM metadata, ни
один из exact output hits не выделяет один `ProjectId`, результат остаётся
`unconfirmed`. Сегменты пути, filename и первый кандидат не используются.
F09-01 этим правилом закрывается.

## Side effects

До/после capture сравнивались timestamp и SHA-256 существующих `.dll`, `.pdb`,
`.exe` под `bin`/redirected `artifacts`. Debug, Release, project-open и
multi-TFM capture не изменили ни один compilation output и не создали новый.

Design-time load может создать обычные текстовые intermediates в ещё не
открывавшемся `obj\<Configuration>` (`AssemblyInfo.cs`, generated
editorconfig/cache). Это поведение самого `MSBuildWorkspace`, а не compilation:
compiler output не появился и существующие binaries не изменились.

## Стоимость

Три парных измерения после одного warm-up; это наблюдение, не SLA.

### Fixture: Generator + Consumer

- baseline open: 1805.3, 1838.6, 1831.9 ms;
- capture open: 1902.6, 1883.8, 1757.7 ms;
- replay: 6.0, 5.1, 8.6 ms;
- paired additional median: 50.3 ms;
- один binlog, 40,236 bytes, четыре project contexts, один captured analyzer.

### Реальная solution этого репозитория: три проекта

- baseline open: 2318.8, 6629.1, 2221.3 ms;
- capture open: 2379.1, 2302.5, 2320.2 ms;
- replay: 14.8, 14.2, 14.3 ms;
- paired additional median: 75.2 ms;
- один binlog, 157,929 bytes, 11 project contexts, 18 `Analyzer` task outputs.

Три пары содержат заметный шум BuildHost open, поэтому числа не задают SLA.
Наблюдаемый paired median capture+replay overhead: 50.3 ms на fixture и
75.2 ms на solution; median replay отдельно — 6.0 и 14.3 ms, плюс указанный
объём временного файла. Always-on режим F09-02 сохраняется:
отключать capture по feature flag нельзя, иначе cached `false -> true` потребует
скрытый второй eval. Для больших solution telemetry `bytes/files/replay wall`
обязательна.

## Fail-closed и cleanup

Проверены missing, corrupt, truncated/partial binlog, cancellation и смесь
valid+corrupt файлов:

- missing/corrupt/truncated не публикуют complete snapshot;
- valid+corrupt даёт `incomplete`, даже если valid events сохранены для
  диагностики;
- `BinaryLogReplayEventSource.Replay` при уже отменённом token возвращается без
  исключения, поэтому consumer обязан после replay вызвать
  `cancellationToken.ThrowIfCancellationRequested()`;
- replay выполняется через явно открытый `FileStream`, а не
  `Replay(string)`: при повреждённом header строковый overload может оставить
  открытым reader до finalization и помешать немедленному удалению;
- временный каталог удаляется в `finally` с коротким best-effort retry; в тесте
  после failure matrix каталог отсутствовал.

Binlog создаётся с `ProjectImports=None`; имя и каталог случайны. Production
код не должен логировать event payload, properties или metadata.

## Lifecycle

В production capture должен находиться только в physical-open ветке
`SolutionManager.LoadCoreAsync` после cache guard и до публикации snapshot:

- physical load / graph-stale / смена load globals: один capture;
- cache hit, `.cs` reconciliation, semantic query, document edit и artifact
  refresh: ноль;
- `ClearWorkspaceAsync`: сброс ссылки на snapshot;
- replay и cleanup завершаются до атомарной публикации.

Spike не встраивал BinaryLogger в `SolutionManager`, поэтому этот пункт
подтверждён границей существующего lifecycle-кода, а не production counter.
Counter/lifecycle tests обязательны в реализации capture и не являются
разрешением менять matcher заранее.

## Решение и следующий шаг

1. Binlog той же design-time загрузки принимается как production channel F-09
   на указанной матрице; переход к Alt-2/Alt-3 не требуется.
2. Production capture реализовать отдельным сервисом и immutable snapshot,
   используя exact join выше и fail-closed replay.
3. В production project добавить явную compile-time зависимость
   `Microsoft.Build` с `PrivateAssets="all"` / `ExcludeAssets="runtime"` вместе
   с существующим MSBuild dependency set (F09-04).
4. До atomic snapshot, lifecycle counters и marker acceptance matcher не
   менять. P0 разрешает начать E5-S2 implementation, но не считать rollout
   принятым.
