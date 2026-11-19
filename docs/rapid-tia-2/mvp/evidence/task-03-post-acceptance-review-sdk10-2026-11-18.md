# Task-03 — дополнительное ревью и передача исправлений

- Дата: **2026-11-18**, Europe/Moscow.
- Проверенный commit: **`d78bb2d`**, `Keep the pre-edit assembly snapshot for the whole baseline session`;
  база изменения — `03648ad`. Ветка `rapid-tia` обновлена через `git pull --ff-only`.
- Вердикт дополнительного ревью: **MUST-FIX, два открытых P2: POST-F-01 и POST-F-02**.
  Исторические ACCEPT раунда 2 и
  [Linux-validation](task-03-final-validation-2026-11-18.md) сохраняются, но не закрывают эти новые сценарии.
- Scope: реализация и тесты [task-03](../task-03-assembly-snapshot-session-hi.md), вызовы из
  `BaselineSession`, модели, reader, retirement, SourceStructure и существующие отчёты. Это дополнительная
  проверка готового коммита, а не новый раунд прежнего цикла реализации.
- Исправления и регрессионные тесты **не внесены**. Воспроизводитель работал вне репозитория с его
  собранным production-кодом и существующими fixture/compiler helpers. На момент ревью потребителя пары
  ещё не было, task-04 не была начата. Новые требования, решения владельца и разрешения на активацию не вводятся.

При обновлении перед публикацией документации получен **`b27d16d`** с реализацией task-04. Код
`Services/Testing/Snapshots` в нём относительно `d78bb2d` не изменён; появился внутренний потребитель
`Services/Testing/Narrowing/TestClassNarrower.cs`, вызывающий `assemblies.GetPairAsync(solution, ...)`.
Статусы и evidence task-04 сохранены. Числа проверок ниже относятся к **`d78bb2d`**, не к новому коммиту;
task-04 в этом дополнительном ревью не проверялась.

## POST-F-01 — P2, open: запрос смешивает новый baseline со старыми проектами

Место: [`AssemblySnapshotSession.GetPairAsync`](../../../../Services/Testing/Snapshots/AssemblySnapshotSession.cs),
строки **136–160** в `d78bb2d`, особенно вызов `TryBuildPairAsync(snapshot, projects, ...)` на строке 154.
Overload с `Solution` на строках 118–128 один раз извлекает `projects` из переданного solution; overload
со списком повторяет запрос в цикле с тем же списком.

Если новый baseline заменяет `_snapshot` во время чтения текущего снимка, `TryBuildPairAsync` замечает смену
и возвращает null. Цикл берёт уже новый `_snapshot`, но снова передаёт прежние `projects`. Итоговая пара
помечена путём и временем нового baseline; `Before` и `BeforeGraph` относятся к нему, а `Current`,
соседство и `CurrentGraph` строятся по проектам прежнего запроса. Нарушено обещание согласованной пары.

Детерминированное воспроизведение с `AssemblySnapshotFixture`:

1. Записать `Lib` из `LibV1` и `Other` с `public class O { }`, запустить сессию только с `Lib`, дождаться
   `FirstReadTask`. Время файлов — `BeforePublication`.
2. Перезаписать `Lib` из `LibV2` со временем `PublishedAtUtc.AddMinutes(1)`.
3. В `session.Reader.WhileFileIsOpenForTests` остановить **первое текущее чтение Lib**, используя два
   `ManualResetEventSlim` (`entered`/`release`) и `Interlocked`-счётчик. Первое чтение baseline к этому
   моменту уже завершилось; hook ставится после него. У ожиданий должен быть timeout.
4. Запустить через `Task.Run` запрос `session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None)`
   и дождаться `entered`.
5. Пока запрос остановлен, вызвать
   `session.Start(secondPath, DateTime.UtcNow, fixture.Inventory("Other"), NullLogger.Instance).Cancel()`,
   где `secondPath = Path.Combine(fixture.Root, "Second.slnx")`. Дождаться нового `FirstReadTask`.
6. Отпустить `release`, дождаться исходного запроса. Фактически возвращается:

```text
generation: workspace=Second.slnx, before=Other.dll, current=Lib.dll
generation: new-baseline-labelled-pair-has-old-projects=True
```

Это гонка **текущего запроса**, а не опоздавший результат фонового первого чтения. Имеющийся тест
`Another_solution_started_during_a_read_gets_no_entry_of_the_old_one` проверяет именно первое чтение и
не закрывает этот случай.

Минимальное направление исправления: привязать запрос к исходному baseline/поколению и явно отклонить его
при смене; повторный запрос должен получить актуальные solution/projects. Если сохраняется автоматический
повтор, он должен обновлять и набор проектов, а не только `_snapshot`. Не возвращать смешанную пару как
успешный результат. Добавить регрессию с описанной остановкой текущего чтения; она должна падать на
`d78bb2d`. Точный способ сообщить устаревание согласовать с контрактом внутреннего API и уже появившимся
в `b27d16d` внутренним потребителем из task-04, без изменения публичного инструмента.

## POST-F-02 — P2, open: кэш удерживает результаты удалённых проектов

Место: [`AssemblySnapshotSession.TryBuildPairAsync`](../../../../Services/Testing/Snapshots/AssemblySnapshotSession.cs),
строки **203–208** в `d78bb2d`. `snapshot.Current` обновляется только присваиваниями
`snapshot.Current[entry.Path] = entry`; пути, отсутствующие в новом запросе, не удаляются.

Снимок «до правки» законно хранится до нового baseline. Отдельная проблема — записи, которых в нём
никогда не было и которых уже нет в последнем текущем снимке. Например, добавленный после reload проект
или прежний output path после изменения конфигурации остаётся в кэше со всеми facts и fingerprints.
При повторении таких изменений память накапливается до смены baseline; reset/reload того же solution по
M-10 baseline сохраняет.

Детерминированное воспроизведение:

1. Создать `Lib` и `Other`, но запустить исходный baseline только с `Lib`; дождаться `FirstReadTask`.
2. В отдельном `[MethodImpl(MethodImplOptions.NoInlining)]` async helper запросить пару для
   `fixture.Projects("Lib", "Other")` и вернуть только
   `new WeakReference(pair.Current.Find(fixture.PathOf("Other")))`. Сильную ссылку на пару не сохранять.
3. Запросить следующую пару только для `fixture.Projects("Lib")`.
4. Выполнить несколько циклов `GC.Collect()` / `GC.WaitForPendingFinalizers()`, удерживая саму сессию
   (`GC.KeepAlive(session)` в конце). `Other` отсутствует и в `Before`, и в возвращённом `Current`,
   но слабая ссылка остаётся живой.
5. Контроль: начать новый baseline только с `Lib`, дождаться чтения, повторить GC. Теперь слабая ссылка
   мертва. Это подтверждает удержание старой записью сессии, а не fixture или локальной переменной пары.

```text
memory: removed-in-before=False, removed-in-current=False, removed-entry-still-alive=True
memory: removed-entry-alive-after-new-baseline=False
```

Минимальное направление исправления: при успешной публикации текущего снимка удалить из его кэша пути,
которых нет в новом наборе, либо заменить кэш картой только последних entries. `Before` сохранять независимо.
Добавить weak-reference регрессию для добавленного, затем удалённого проекта/выходного пути; она должна
падать на `d78bb2d`. Имеющийся `Replaced_current_result_is_released_while_the_before_result_stays`
проверяет замену результата по **тому же пути**, а не исчезновение пути из текущего снимка.

## Выполненные проверки на Windows

SDK **10.0.402**, Release; изолированный SDK10 host и его окружение переданы дочерним процессам.
Прогоны последовательные. Roslyn build/test tools не выражают требуемое окружение и произвольный фильтр;
использован разрешённый AGENTS.md PowerShell runner.

```powershell
dotnet build RoslynMcpServer.sln -c Release --no-restore --no-incremental
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~RoslynMcpServer.Tests.Testing.Snapshots|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure' --logger 'trx;LogFileName=focused.trx' --results-directory TestResults/task03-review-sdk10
$env:ROSLYN_MCP_ANALYZER_LIFECYCLE = '1'
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --filter 'Category=AnalyzerLifecycle' --logger 'trx;LogFileName=lifecycle.trx' --results-directory TestResults/task03-review-sdk10
Remove-Item -LiteralPath Env:ROSLYN_MCP_ANALYZER_LIFECYCLE
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --filter 'Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk' --logger 'trx;LogFileName=main.trx' --results-directory TestResults/task03-review-sdk10
./.github/scripts/Assert-NoSkippedTests.ps1 -ResultsDirectory TestResults/task03-review-sdk10
```

- Solution Release build: **0 ошибок**, три прежних предупреждения вне task-03 — CS8603
  `NuGetFallbackAssemblyResolver.cs:72`, CS8601 `HostSession.cs:1179`, xUnit1031
  `WpfTemporaryProjectCallbackTests.cs:195`.
- Snapshots + SourceStructure: **147/147** (24 + 123), 0 skips.
- AnalyzerLifecycle: **13/13**, 0 skips.
- Windows main: **2429/2429**, 0 failures/errors/timeouts/skips, duration **4 min 19 s**.
- Репозиторный no-skips script по трём TRX: **2589 total = executed = passed**, failed/notExecuted=0.

Зелёный штатный набор не опровергает POST-F-01/02: эти сценарии в него ещё не включены. Воспроизводитель
подтверждает дефекты, но не является уже добавленным регрессионным тестом.

Сырые локальные артефакты — ignored `TestResults/task03-review-sdk10/`: `build.log`, `focused.log/.trx`,
`lifecycle.log/.trx`, `main.log/.trx`, `probes.log`. SHA256:

```text
main.trx   BD1E5C46B01DEDEC01D51279380BE7BD23087ABDB4C728A95DE9D065E8279C87
probes.log 05BC69B02180EB5A640EF29E909A25ACB8898933DCD7FE71C1220FA677F9117A
```

Исходный временный harness на машине рецензента:
`AGENT_WORK_ROOT`.
Он содержит `Program.cs`, `Probe.csproj`, минимальный `MetadataReferenceSets.cs`, использует friend-name
`RoslynMcpServer.Tests`, production DLL и linked `AssemblySnapshotFixture.cs`/`IlFingerprintCompiler.cs`.
Для другого агента этот локальный путь не является обязательным входом: полные условия и барьеры двух
сценариев перечислены выше, существующие helpers находятся в репозитории.

## Передача следующему агенту

1. Прочитать AGENTS.md и полный `docs/code-style.md` до изменения C#; сохранить M-10, неизменность `Before`
   и область task-03. Не менять публичный инструмент; учесть существующего внутреннего потребителя из
   task-04 при уточнении контракта устаревшего запроса, не расширяя работу до новых задач.
2. Добавить две регрессии, подтвердить падения на `d78bb2d`, затем исправить общую причину каждого дефекта.
   Не подменять сценарий смены baseline тестом прерывания первого фонового чтения.
3. После production-изменений прогнать все SourceStructure; перед приёмкой — Release build и полный main
   suite. AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` также обязателен: меняются lifecycle снимка
   и путь публикации baseline. Использовать SDK10 и корректный платформенный CI-фильтр.
4. Записать red/green и итоговую validation; закрыть POST-F-01/02 только по проверенному результату.
   Обновить task/index/report summaries после повторной приёмки. Исторические review/validation не удалять.

Замеры на больших solution не повторялись. Код, тесты, версия и CHANGELOG в дополнительном ревью не
менялись; эта запись и ссылки на неё сохраняют результаты для продолжения другим агентом.
