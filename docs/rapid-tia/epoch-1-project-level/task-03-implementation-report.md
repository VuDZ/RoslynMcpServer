# task-03 — отчёт реализации

- Дата: 2026-11-03.
- Исполнитель: прежний исполнитель эпохи Codex; точный runtime model ID средой не предоставлен.
- Scope: сессионный baseline, его публикация/чтение в фоне, поколения и журнал путей; интеграция с реальными MCP writes, disk watcher, reload/reset. Без графа, project mapping и публичного инструмента.
- Состояние: **accepted, 2026-11-03** — повторно принята после R2-001/R2-002/R3-001, [раунд 4 — ACCEPT](evidence/task-03-review-round-4.md), [финальная validation](evidence/task-03-final-validation-round-4.md). Scope: baseline сессии, безопасная фиксация после старта watcher и передача потери наблюдения, поколения, Known/Unknown и журнал/кандидаты путей; без графа, project mapping и публичного инструмента. Историческая приёмка раунда1 ниже была снята раундом2; актуальна повторная приёмка в конце отчёта.

## Модель и интеграция

Добавлен внутренний `Services/Testing/Baseline/BaselineSession.cs`, принадлежащий
SolutionManager. Сессионный lock независим от workspace lock. Модели в `Models/` —
BaselineDocumentKey, BaselineDocumentState, BaselineDocumentStatus и
BaselineSessionSnapshot — содержат только неизменяемые факты/снимки, без Document,
Solution, SyntaxNode или semantic model.

Первичная публикация сразу фиксирует identity и inventory документов, до запуска
фонового worker. Clock берётся у SetPublishedSnapshot, до перечисления inventory;
это момент публикации для timestamp guard. Worker по захваченному immutable Solution
читает syntax trees и строит снимки task-01 вне workspace lock, без компиляции и без
ожидания search prewarm. Под lock workspace перечисляются только метаданные/IDs для
первичного capture; текст и хеширование там не выполняются.

BaselineDocumentKey включает физический проект, actual loaded project name с номером
повторного контекста, физический и логический путь документа и номер повторного
membership. На Windows ключевые физические пути case-folded; сравнение пользовательских
путей следует InputPathCanon. Volatile output/parse settings и Roslyn DocumentId не
служат стабильным ключом: обычная правка csproj и новые DocumentIds не делают известные
снимки недоступными. Ссылки одного файла в разных проектах/контекстах и разные документы
с одинаковым partial-member key остаются отдельными записями. Точное повторное loaded
имя/путь различается occurrence, не схлопывается по ключу члена.

Фоновая публикация хеша проверяет generation, состояние Pending и sampled write
version пути под одним gate. Запись, пришедшая до фиксации, делает незавершённые
контексты пути Unknown; уже Known snapshot не заменяется. LastWriteTimeUtc проверяется
до чтения и после before-commit barrier: время после публикации или недоступный файл
также не принимаются за baseline. Все ошибки захвата приводят к Unknown/degraded
состоянию и строке результата worker, не к тихому пустому снимку.

MCP записывает путь до I/O в WriteTrackedTextAsync/WriteTrackedText и в старой boundary
SuppressDiskWatchForPath. Watcher пишет журнал внутри уже существующего session
admission, после классификации релевантности; закрытая старая watcher session не
может записать событие нового поколения. Для opt-in load до финальной публикации
существует provisional журнал: событие с сохранённым mtime не теряется между запуском
watcher и prepare/publication. Этот журнал не считается созданным baseline:
WorkspacePath отсутствует, пока semantic snapshot не опубликован; withheld/unavailable
publication capture не запускает.

Обычная запись не сканирует документы workspace: pending-by-path index содержит только
не зафиксированные memberships. Коммит снимает свой key, а запись инвалидирует только
оставшиеся pending contexts этого пути. После завершения capture это O(1) admission.
Обычная publication/cached load с той же identity возвращается до Inventory; полное
перечисление остаётся для первой или настоящей повторной загрузки.

## Reload/reset и время жизни

Физическое переоткрытие и reset немедленно закрывают generation, отменяют worker и
не ждут его. BeginLoad атомарно открывает provisional write journal. Known hashes
сохраняются, Pending превращаются в Unknown и при загрузке того же solution никогда
не дочитываются заново. Cache hit не прерывает capture и не меняет generation. Новая
identity solution очищает старое состояние; поздняя работа прежнего поколения не
добавляет хвост и не меняет снимки другого solution.

После watcher gap выставляется RequiresFullScan. В immutable view TouchedPaths означает
событийные пути плюс консервативные C# rescan candidates исходного и нового inventory.
Поэтому правка известного C# файла между reset и reload не выглядит как достоверное
отсутствие изменений: исходный Known snapshot сохранён, текущий текст сравнивается с ним.
Новые пути после reload добавляются в rescan candidates, а не захватываются как новый
baseline. Флаг намеренно сохраняется в сессии, без acknowledgement API: это дополнительная
стоимость повторной проверки путей; неизменённые известные C# снимки можно отбрасывать
сравнением task-02. Междокументное/project-level поведение остаётся будущим потребителям.

Solution хранится только в work packet активного worker и явно обнуляется в finally;
локальный Document также освобождается. Сессия хранит отдельный completion TaskCompletionSource,
а не завершённый Task.Run с его async locals. Поэтому завершённый capture не закрепляет
старый Solution. Пока отменённый worker ещё не остановился, reset пишет об оставшейся
захваченной ссылке по R-15, не обещая её немедленного освобождения. Существующая логика
остановки search prewarm не изменялась.

## TDD и фактические проверки

Добавлены BaselineLifecycleFixture, BaselineCaptureGate и BaselineSessionIntegrationTests.
Фикстуры создают настоящие SDK csproj и двухпроектный slnx, грузят их тем же
SolutionManager/MSBuild, что production. Async barriers удерживают конкретный документ
до чтения или до коммита; token намеренно можно не заметить, чтобы проверять admission
поколения, а не случайный sleep. В исходном RED каждый тест после реальной загрузки
достигал компилируемого GetSnapshot stub с NotImplementedException.

Все исходные 16 интеграционных cases написаны до production-логики:

- внешняя правка до первого чтения, одинаковый исход MCP/disk события;
- отдельные timestamp guard без watcher и event guard с сохранённым mtime;
- запись между хешированием и коммитом;
- два partial-документа с одинаковым member key и отдельными изменениями;
- csproj reload с новыми DocumentIds и сохранёнными key/hash/touched;
- поздний хвост после same-solution reopen, cache-hit guard;
- reset после первого хеша и до первого чтения, reload без recapture Unknown;
- другая identity solution с поздним завершением старого worker;
- linked source с разными активными preprocessor contexts в двух проектах;
- opt-in event до финальной publication при сохранённом mtime;
- внешняя правка известного файла в reset watcher gap с preserved baseline и rescan.

[RED](evidence/task-03-red.md): **16 выполнено, 0 прошло, 16 ожидаемых
NotImplementedException** после успешных MSBuild load. Первый fixture build требовал
missing using Microsoft.CodeAnalysis.CSharp; ошибка компиляции RED не считается.
Первый implementation run дал 15/16: leaf-путь Shared.cs в fixture сравнивался
case-sensitive, хотя Windows ключ case-folded. Predicate исправлен на существующий
OS path comparer, сохранив ожидание двух разных контекстов; затем 16/16.

Поздние случаи записаны отдельно:

1. Перед pending-by-path оптимизацией добавлен реальный linked-context guard: один
   контекст уже Known, второй удержан перед commit; preserved-mtime событие должно
   оставить первый снимок и сделать второй Unknown. Он прошёл **1/1** до оптимизации
   сканирования и входит в итоговый GREEN. Это equivalence guard, а не заявленный RED.
2. При финальном audit добавлена unavailable opt-in publication. До исправления getter
   provisional path показывался как identity: отдельный
   [RED](evidence/task-03-additional-red.md) — **1 выполнено, 1 ожидаемое Assert.Null
   падение** после реального MSBuild load. После исправления baseline identity не
   публикуется до semantic snapshot.

[Итоговый focused GREEN](evidence/task-03-green.md): **351/351**, без падений и пропусков —
18 lifecycle integration + 210 прежних baseline cases + все 123 SourceStructure.
Windows Release, специализированный Roslyn MCP runner. Incremental focused build
напечатал только прежний CS8603. Последующая полная сборка координатора выявила шесть
новых xUnit2031 warnings; эквивалентные Assert.Single(sequence.Where(predicate)) заменены
на Assert.Single(sequence, predicate) без смены predicates/results. Для API cleanup
дополнительный RED не заявлялся. Координатор повторяет финальные проверки frozen tree.

## Точный инвентарь ограничений SourceStructure

Первый полный SourceStructure дал 122/123 из-за двух новых test seam limits. После
диагностического вывода установлены точные identities и kinds:

- `beforeRead` — ReachabilityLimitKind.TargetOutsideTheScope, параметр CaptureWork,
  достигнутый через invoked delegate `BeforeRead` в Services/Testing/Baseline/BaselineSession.cs.
- `beforeCommit` — ReachabilityLimitKind.TargetOutsideTheScope, параметр CaptureWork,
  достигнутый через invoked delegate `BeforeCommit` в том же файле.

Причина — не моделируется delegate argument-to-parameter flow; production оставляет
test-only hooks null, а тесты задают barriers. Файл
RoslynMcpServer.Tests/SourceStructure/SourceSetAnalysisTests.cs,
Production_reachability_records_only_the_known_delegate_limits, теперь проверяет
прежние 11 и ровно эти два имени, kind и source path. Count снова Assert.Equal по
длине точного списка, неизвестные дополнительные limits не допускаются.

## Передача координатору

Production: SolutionManager и новый BaselineSession с четырьмя моделями. Tests: три
новых файла integration/fixture/gate и точечный exact inventory SourceStructure.
Статусы задачи/индексов, Release solution build, полный main suite и opted-in
AnalyzerLifecycle (`ROSLYN_MCP_ANALYZER_LIFECYCLE=1`) — у координатора. Исполнитель
полный main/lifecycle не запускал, коммит и push не выполнял. Версия 1.5.18 не
повышалась; спецификации, READMEs, старые evidence и посторонний
.agents/skills/prepare-github-release/ не менялись. Task-04 и следующие задачи не начаты.

IDE API для создания/редактирования файлов недоступен; применён разрешённый локальный
файловый fallback, без Roslyn MCP apply_patch/update_file_content. Baseline — только
в памяти сессии; сериализация графа, извлечение фактов, публичный tool и новые настройки
в задачу не добавлялись.

## Приёмка координатором до раунда 2

- Дата: 2026-11-03. [Раунд 1](evidence/task-03-review-round-1.md) — **ACCEPT**, must-fix нет; **1 раунд, 0 исправлений после независимого ревью**. Доводка до первого ревью, late guards и эквивалентная очистка assertions не входят в этот счётчик.
- Исполнитель: прежний исполнитель эпохи Codex. Рецензент: Codex, независимая сессия; точные runtime model ID недоступны.
- [Финальная validation](evidence/task-03-final-validation.md): Release solution build успешен; Testing + весь SourceStructure **351/351** (18 новых integration + 210 прежних baseline cases + 123 SourceStructure); полный Windows main suite **1660/1660**; opted-in AnalyzerLifecycle **13/13**. Все прогоны без падений и пропусков.
- AnalyzerLifecycle выполнен координатором через MCP runtime runner с `--environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, поскольку специализированные test API не принимают env. Уникальный TRX проверен отдельно: total=executed=passed=13, failed=notExecuted=error=timeout=aborted=0.
- Шесть новых xUnit2031 warnings устранены эквивалентными API assertions перед финальной сборкой. Остались три прежних предупреждения в файлах вне задачи. Linux suite и отдельный GC probe не запускались; source review подтверждает отсутствие Solution в завершённом состоянии, а R-15 лог сохраняет честную отмену без ожидания.
- Инвентарь SourceStructure сохраняет прежние 11 и ровно два новых limits: beforeRead/beforeCommit, TargetOutsideTheScope, source BaselineSession.cs. Проверка точных identities/kinds/paths не ослаблялась.
- В [task-08](task-08-estimate-tool-hi.md#вход-из-task-03) добавлена справка о фактическом входе RequiresFullScan/TouchedPaths: C# candidates после gap не доказывают полноту наблюдения всех входов. Будущий потребитель учитывает этот факт в существующей обработке Not covered/консервативного расширения; код task-08 не начат.
- Версия 1.5.18 сохранена. Task-04 и следующие задачи не реализовывались; эпоха целиком не принята. Известная стоимость fallback — повторное сравнение C# candidates после разрыва наблюдения; список содержит возможные пути, а не обещание точного журнала всех записей.

## Исправления по ревью приёмки, раунд 2

Дата: 2026-11-03. Scope — только MUST-FIX R2-001/R2-002 из [ревью раунда 2](evidence/task-03-review-round-2.md). Исполнитель прежний Codex; точный runtime model ID средой не предоставлен. Это дополнение не меняет исторические результаты и не объявляет повторную приёмку.

R2-001: WorkspaceInputSession принимает необязательный синхронный onObservationGap и вызывает его под тем же admission/closure gate, что NoteCoverageUnknown и NoteDirectoryRename. Поэтому сигнал передают watcher overflow/error, оба startup failure пути (нет каталога/исключение), watcher directory rename и прямой вызов из ProjectRenameHelper.Apply. Закрытая сессия callback не вызывает. Baseline.NoteObservationGap замораживает Pending в Unknown и поднимает RequiresFullScan, сохраняя Known и исходные snapshots. Сигнал до публикации сохраняется отдельно для pending load identity и применяется при создании inventory; разрыв старой solution не наследуется новой. Обычные write events не поднимают флаг.

R2-002: в LoadCore обычный SetPublishedSolution перенесён после StartDiskWatcherUnderLock. Момент первой публикации теперь фиксируется после старта watcher; admitted writes до него остаются в pending journal и превращают соответствующие документы в Unknown. Opt-in порядок сохранён, недоступная семантическая публикация не создаёт baseline, cached load не запускает новый capture. Новые before/after-start hooks — только детерминированные test seams, без sleep как доказательства окна.

### TDD и фазы проверок

- До изменения поведения написаны все 13 случаев нового BaselineObservationGapTests на реальном MSBuild: overflow/error/прямой coverage сигнал, directory rename с membership и без, startup exception и missing directory, удержанный startup boundary, событие после startup, opt-in prepublication gap, фактический Directory.Move с документом, закрытая сессия/другая identity, обычная MCP+disk запись. Существующие интеграционные guards Known retention, cached unfinished capture, reload/reset и opt-in остались в наборе.
- [RED](evidence/task-03-review-round-2-red.md): 13 выполнено, 2 passed, 11 failed; сборка и фикстуры исправны. Before-start seam добавлен как тестовая инструментация, до production fix. Затем до production fix добавлен after-start seam и уточнён event guard: он доставляет preserved-mtime событие строго после реального старта; повтор полного RED также 13/2/11. Отдельный window/opt-in прогон 3/1/2 показывает детерминированное Assert.Null падение преждевременной публикации и Assert.True падение потерянного opt-in gap. Event guard на старом коде может проходить или падать в зависимости от завершения старого worker и не выдаётся за самостоятельное детерминированное доказательство дефекта. Доказательство порядка — удержанный startup boundary, без sleep.
- После поведения — 363/364: единственное падение точного inventory13→16. Диагностический вывод подтвердил три новые identities/kinds; после точного обновления и возврата строгого Assert.Equal [GREEN](evidence/task-03-review-round-2-green.md) — **364/364** (241 baseline + 123 SourceStructure), без падений. Поведенческие случаи после production fix не добавлялись; поздняя правка только точного структурного инвентаря по фактическому анализу.
- Новые пределы: BeforeDiskWatcherStartForTests и AfterDiskWatcherStartForTests — ReachabilityLimitKind.TargetOutsideTheScope, Services/Workspace/SolutionManager.cs; onObservationGap через _onObservationGap — тот же kind, Services/Models/WorkspaceInputSession.cs. SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits проверяет все прежние13 + эти3, каждое имя/kind/path, ровно16; неизвестные ограничения не допускаются. Причина — delegate registration вне scope анализа, а не ослабление проверки.

### Файлы и передача

Production: Services/Models/WorkspaceInputSession.cs, Services/Testing/Baseline/BaselineSession.cs, Services/Workspace/SolutionManager.cs. Tests: новый RoslynMcpServer.Tests/Testing/BaselineObservationGapTests.cs и точный inventory в RoslynMcpServer.Tests/SourceStructure/SourceSetAnalysisTests.cs. Evidence: два новых task-03-review-round-2-red.md/-green.md; настоящий отчёт дополнен. Старые evidence сохранены.

CODE FROZEN сообщён координатору после финального GREEN; дальнейшие изменения только документации. Полная Release solution build, main suite, AnalyzerLifecycle и итоговые статусы/приёмка/commit/push — у координатора. Исполнитель их не подменяет focused результатом. Версия1.5.18, task/spec/README/status, task04 и посторонний untracked .agents/skills/prepare-github-release/ не менялись. IDE file editing API недоступен, использован разрешённый локальный fallback без Roslyn MCP apply_patch/update_file_content.

Ограничения прежние: RequiresFullScan остаётся поднятым до другой solution, TouchedPaths включает консервативные C# rescan candidates; после gap сравнивается больше файлов, полнота наблюдения не-C# входов не восстанавливается. Новые callback не выполняют I/O, не ждут capture и не вводят отдельную watcher/acknowledgment инфраструктуру. Детерминированные race tests используют barriers; fail-next-start использует существующий тестовый seam.

## Позднее исправление изоляции тестов — R3-001

Дата: 2026-11-03. Независимое ревью выявило scheduling race: Startup_gap_survives_first_publication задаёт process-wide WorkspaceDiskWatcherStarter.FailNextStartForTests, который могла потребить другая параллельная real-MSBuild загрузка, а WorkspaceInputFreshnessTests также задаёт/сбрасывает его. Добавлен отдельный BaselineObservationGapCollection.cs с CollectionDefinition("BaselineObservationGap", DisableParallelization=true); BaselineObservationGapTests помечен соответствующим Collection. Применён существующий шаблон репозитория, один независимый тип в файле. Production, assertions и exact inventory неизменны.

Это поздняя test isolation правка после GREEN раунда 2, без искусственного nondeterministic RED-воспроизведения. Предыдущие результаты исполнителя 364/364 и координатора build/main/lifecycle относятся к состоянию до неё, не подменяют финальные проверки. Исполнитель повторно замораживает код; координатор выполняет финальную сборку и suites после этой правки. Статусы, версия, спецификации, README и старые результаты не переписывались, commit/push не выполнялись.

## Повторная приёмка после раунда 4

- Дата: 2026-11-03. [Раунд 3](evidence/task-03-review-round-3.md) подтвердил R2-001/R2-002 и нашёл R3-001 — изоляцию глобальной тестовой инъекции. Прежний исполнитель добавил matching collection с DisableParallelization=true, без изменения production/assertions. [Раунд 4](evidence/task-03-review-round-4.md) — **ACCEPT**; все три замечания закрыты. Всего **4 раунда, 3 исправления**: Codex (1,3,4), Claude Opus5.5 (2); runtime model ID Codex недоступен.
- [Окончательная validation координатора](evidence/task-03-final-validation-round-4.md) после изоляции: Release solution build успешен, Testing + весь SourceStructure **364/364**, полный Windows main suite **1673/1673**, opted-in AnalyzerLifecycle **13/13**. Падений и пропусков нет. Все шесть C# файлов совпадают по SHA-256 с финальной сборкой.
- AnalyzerLifecycle выполнен через MCP runtime runner с --environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1; уникальный TRX отдельно подтверждает total=executed=passed=13, failed=notExecuted=error=timeout=aborted=0. Три предупреждения сборки прежние, вне задачи. Linux и отдельный GC probe для этого исправления не запускались.
- Точный структурный inventory16 содержит прежние13 и BeforeDiskWatcherStartForTests/AfterDiskWatcherStartForTests (SolutionManager.cs), onObservationGap (WorkspaceInputSession.cs), все TargetOutsideTheScope; индивидуальные имя/kind/path и строгий count сохранены.
- [Вход task-08](task-08-estimate-tool-hi.md#вход-из-task-03) актуализирован фактическим закрытием R2-001: флаг охватывает ошибки/сбой старта watcher и directory rename, остаётся поднятым до другой solution. C# rescan candidates консервативны; полнота наблюдения не-C# входов после gap не восстанавливается.
- Версия1.5.18 сохранена. Task04 и следующие задачи не начаты; эпоха целиком не принята, публичная активация не добавлена. Посторонний untracked .agents/skills/prepare-github-release/ не включён.
