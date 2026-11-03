# task-03 — отчёт реализации

- Дата: 2026-11-03.
- Исполнитель: прежний исполнитель эпохи Codex; точный runtime model ID средой не предоставлен.
- Scope: сессионный baseline, его публикация/чтение в фоне, поколения и журнал путей; интеграция с реальными MCP writes, disk watcher, reload/reset. Без графа, project mapping и публичного инструмента.
- Состояние: **accepted, 2026-11-04** — R9-001 подтверждён закрытым [раундом10](evidence/task-03-review-round-10.md), [финальная validation](evidence/task-03-final-validation-round-10.md). Scope: сессионный baseline и поколения, безопасная фиксация, корректный rename journal относительно solution root с приоритетом карты входов. Прочие события не менялись; без графа, task-04 реализации и публичного инструмента. Исторические приёмки и возвраты ниже сохранены, актуален итог в конце.

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

## Исправления по ревью приёмки, раунд 5

Дата: 2026-11-03. База `31cc8d8`, прежний исполнитель эпохи Codex; точный runtime model ID недоступен. Scope — только R5-001…R5-004 из [ревью раунда 5](evidence/task-03-review-round-5.md); исходные требования и статусы исполнитель не менял.

### R5-001 — ожидание admitted event вместо dirty marker

Детерминированный новый тест сначала читает syntax trees всех документов общей DeliveryFixture, затем выполняет directory rename/re-read и Semantic_sync в закреплённом порядке. Перед второй операцией утверждается OwnWriteDefersContent(Program.cs) и отсутствие dirty marker. До исправления ожидания это дало настоящий runtime RED `FileSystemWatcher did not deliver Program.cs`: событие принималось, но откладывалось до byte reconciliation собственной записи.

Исправлены только tests: в Semantic_sync ожидается сдвиг EventRevision относительно значения перед записью. Проверен весь каталог WaitForDirtySourceAsync callers. Два других caller в той же общей fixture (linked Shared и explicit obj/Manual) также ждут admitted EventRevision; проверка InputRevision для Manual перенесена после синхронизации, когда отложенная byte reconciliation уже произошла. Два оставшихся caller — свежая baseline fixture с capture до первого чтения и отдельная plugin fixture без предшествующих собственных текстовых записей — сохраняют осмысленное ожидание очереди dirty. Production API WaitForDirtySourceAsync и semantic sync поведение не менялись для R5-001.

### R5-002 — доказанная причина cached-test failure

На изолированной Linux-копии исходной базы выполнены bounded focused повторы. Исходный тест упал на попытке 6 (5 passed), диагностический на 17 (16 passed), фазовый на 25 (24 passed). Часть повторов шла одновременно с Windows focused нагрузкой. Все raw failure logs, временные диагностические patches и manifest сохранены в [Linux evidence](evidence/task-03-review-round-5-linux/r5-cached-repeat-manifest.log), не выдаются за full suite.

Доказанный механизм: cache-hit и generation assertions прошли; оба документа Unknown, RequiresFullScan=false, worker exception и watcher Changed/Renamed отсутствуют. Исходные mtimes не менялись. В [фазовом trace](evidence/task-03-review-round-5-linux/r5-cached-phase-25.log) UTC после создания файлов — `20:17:15.8397766Z`, оба mtime — `20:17:15.8311164Z`; после load UTC — `20:17:15.6152615Z`, Published — `20:17:15.6044919Z`. Время UTC между фазами пошло назад, поэтому исходный mtime оказался позже Published. Timestamp guard корректно сделал baseline неизвестным. Причина — предположение теста, что свежесозданные файлы обязательно имеют mtime не позже публикации, несмотря на корректировку часов; cache lifecycle работал. Источник изменения clock source/часов не устанавливался и не объявляется известным; предположение о поздней записи BuildHost не подтвердилось.

Перед коррекцией теста сделан дополнительный [детерминированный future-mtime RED](evidence/task-03-review-round-5-linux/r5-cached-future-red.log): mtime обоих файлов до load выставлен на 5 минут в будущем, прежний All(Known) падает при проходящих cache/generation assertions. Первое исправление задавало A/B mtime на день в прошлом только в cached-test; затем оно перенесено в общую fixture, см. позднюю validation доработку ниже. Все прежние cache/generation/Known assertions сохранены. Production timestamp guard не ослаблялся, отдельные timestamp/event regressions сохранены. С контролируемым прошлым mtime диагностическая Linux-копия прошла [20/20 повторов](evidence/task-03-review-round-5-linux/r5-cached-past-all.log). Временная диагностическая instrumentation не переносилась в production; [patch](evidence/task-03-review-round-5-linux/r5-diagnostic.patch) оставлен только как provenance. Исходный конкретный failure рецензента без error output нельзя независимо отождествить с каждой воспроизведённой причиной; приведён доказанный механизм воспроизведения в доступной Linux-среде.

### R5-003 — настоящее directory evidence для baseline

Существующая IsDirectoryRename refresh-эвристика и map/session classification оставлены прежними. Новый HasDirectoryRenameEvidence требует существующий каталог или strict mapped descendant; equality пути карты недостаточно, mapped extensionless FILE сам себя каталогом не доказывает. Перед baseline rename journal применяется WorkspaceDiskPathFilter; ignored paths не попадают в него и не вызывают baseline gap. В обычном QueueDiskPath подавляется только baseline journal для ignored Renamed events, прежние map/session revisions и project graph handling продолжаются. Обычный unignored LICENSE остаётся Touched, но без RequiresFullScan. Normal write/build-output journal не менялся; отбор task-04 не реализован.

NoteDirectoryRename сохраняет синхронный callback под admission/closure gate, но принимает confirmedDirectory: callback baseline вызывается при доказанном каталоге; прямой ProjectRenameHelper.Apply использует true по умолчанию. Когда strict evidence есть, а legacy refresh-эвристика его не признаёт (оба dotted directory пути уже исчезли), baseline signal проводится отдельно внутри session.TryRun без изменения старой refresh/classification ветви. Known snapshots сохраняются; настоящие directory move и прямой rename_project покрыты guards.

Регрессии выполняют реальные Git init/add/commit во временной fixture, реальное File.Move в имя без расширения и шесть ignored rename сегментов. Явная доставка соответствующего rename callback дополнительно делает проверку admitted classification независимой от OS event coalescing. Git assert проверяет только .git subtree; обычные bin/obj события не объявляются дефектом task-03. Mapped extensionless guard добавлен после первой реализации как equivalence guard strict-descendant критерия; отдельный RED ему не приписывается. Поздний missing dotted directory case дал [1/1 RED](evidence/task-03-review-round-5-red.md) перед дополнительной правкой baseline signal.

### R5-004 — prepublication gap сохраняет захват

InitializeBaselineUnderLock сохраняет _pendingObservationGap в RequiresFullScan, но больше не замораживает весь первый inventory. Захват выполняется; уже journaled paths по-прежнему Unknown, остальные могут стать Known. Разрыв после публикации по-прежнему замораживает pending tail, Known не теряются. Startup-gap и opt-in expectations исправлены на Known; новый prepublication gap + journaled write case проверяет Unknown только A и Known B при поднятом флаге. Missing-directory fixture теперь удаляет внешний AdditionalFile point-watch directory до startup; C# файлы остаются доступны и не нужно перемещать обратно project root, что само создавало второй native gap или Windows IOException. Это поздняя fixture correction после первых GREEN попыток, не production workaround.

### TDD, итог и передача координатору

[RED](evidence/task-03-review-round-5-red.md): после исправления read-only Git cleanup 15 выполнено, 1 passed, 14 failed до поведения. Предыдущие два Git fixture cleanup failures не выдаются за поведенческий RED. Поздние фазы — mapped-file guard без RED, missing-dotted-directory RED перед дополнительной правкой, cached-test diagnosis и контролируемый future-mtime RED перед коррекцией его временных предусловий — перечислены отдельно. Исторические evidence сохранены.

[Итоговый Windows focused GREEN](evidence/task-03-review-round-5-green.md): **385/385** — baseline 254, все SourceStructure 123, WorkspaceInputWatcherDelivery 8. Specialized Roslyn MCP, Release, noBuild=false. Точный SourceStructure inventory остаётся **16** с прежними identities/kinds/paths и строгим count, новых limits нет. Новые baseline tests не меняют task-01/02 contracts или keys/hashes. Incremental final build напечатал прежний CS8603; полная сборка координатора отдельно проверяет весь список предупреждений.

Финальный delta — 8 C# файлов: Services/Models/WorkspaceInputSession.cs; Services/Testing/Baseline/BaselineSession.cs; Services/Workspace/SolutionManager.cs; Services/Workspace/WorkspaceDiskEventClassifier.cs; RoslynMcpServer.Tests/Testing/BaselineObservationGapTests.cs; RoslynMcpServer.Tests/Testing/BaselineLifecycleFixture.cs; RoslynMcpServer.Tests/Workspace/WorkspaceInputWatcherDeliveryTests.cs; новый RoslynMcpServer.Tests/Testing/BaselineRenameJournalTests.cs. В первой заморозке вместо общей fixture менялся BaselineSessionIntegrationTests.cs; после переноса предусловий он совпадает с базой31cc8d8. Дополнены настоящий report и два новых RED/GREEN evidence. Первая фаза сохранила 9 raw Linux artifacts, validation доработка добавила ещё 5; всего 14 файлов в evidence/task-03-review-round-5-linux/.

**CODE FROZEN** сообщён после итогового GREEN; дальше только документация. Runtime validation теперь у координатора: Windows Release build/main/SourceStructure/AnalyzerLifecycle и три полных Linux main suite на отдельной финальной копии. Доступность Linux подтверждена; эти три final runs ещё не объявляются завершёнными результатами исполнителя. Собственные Linux diagnostics использовали явно разрешённый WSL integration fallback, потому что специализированный Roslyn MCP runner не задаёт это окружение. Final Linux checkout координатора исполнитель не менял.

Версия 1.5.18 не повышалась. Исполнитель не менял task/spec/README/status, task-04+, старые evidence или посторонний untracked .agents/skills/prepare-github-release/; commit/push не выполнял. File-edit IDE API недоступен, применён разрешённый локальный fallback без Roslyn MCP apply_patch/update_file_content. Ограничения baseline прежние: conservative rescan candidates и неполное не-C# наблюдение после настоящего gap; источник коррекции часов Linux остаётся неустановленным, безопасное production поведение при future mtime сохранено.

## Поздняя доработка после полных прогонов первой заморозки

Дата: 2026-11-03. Это продолжение R5-001/R5-002 и build-fix round 1, не новый finding и не production scope. Условный review ACCEPT до runtime проверок не объявляется final acceptance.

Координатор получил Linux main **1683 executed, 1682 passed, 1 failed, 0 skipped**: Startup_gap_survives_first_publication(missingDirectory:false), оба Other/A.cs,B.cs Unknown. [Полный log](evidence/task-03-review-round-5-linux/r6-main-run-1-failed.log) и [TRX counters/failed result](evidence/task-03-review-round-5-linux/r6-main-run-1-failed-trx-summary.xml) скопированы в Windows evidence. TRX имеет start23:27:33.6081288+03 и end23:27:33.5162422+03 при duration1.749s — наблюдаемую инверсию времени. В исходном main нет timestamp trace, и его конкретная ветка не объявляется доказанной.

На отдельной diagnostic копии выполнено ровно 30 natural startup повторов, по 2 cases: **60/60 passed**, main failure естественно не воспроизведён. [Summary](evidence/task-03-review-round-5-linux/r6-startup-natural-summary.log). Затем один контролируемый future-mtime startup прогон дал **2/2 RED** с явным timestamp rejected=True для Other/A.cs и B.cs; единственный gap записан до публикации. [Полный trace](evidence/task-03-review-round-5-linux/r6-startup-future-red.log), [временный patch](evidence/task-03-review-round-5-linux/r6-startup-future-diagnostic.patch). Это подтверждает корректный timestamp branch и необходимое Known-предусловие, но не доказывает ветку исходного main. Дополнительной production диагностики в финальном коде нет.

Known-предусловие теперь задаёт общий BaselineLifecycleFixture.WriteBaselineSource: A/B в каждом CreateProject (включая OtherProject) и Shared.cs в LinkedSolution получают контролируемый mtime на день в прошлом сразу после записи fixture data. Redundant cached-test-only настройка удалена. Explicit timestamp/event tests по-прежнему сами задают future либо preserved mtime в своей фазе; проверки Unknown, событий и Known не ослаблены. Production timestamp guard не менялся. Это устанавливает нужные условия тестов независимо от источника коррекции часов, а не пытается исправить системные часы.

Windows main той же первой заморозки: **1687 executed, 1686 passed, 1 failed**. Ordered pre-read regression через Directory_rename helper ожидал AfterRenameRead, но получил cached BomFreeAfter от предыдущего Semantic_sync. Проверка standing DirectoryRenameInsideMembership reason могла пройти до приёма нового rename общей fixture. Helper теперь ждёт обе стороны новой уникальной пары area/moved в DirectoryRenamePaths; post-sync AfterRenameRead assertion сохранён. Ordered regression дополнительно повторяет directory step после Semantic_sync, чтобы stale reason и cached text заведомо сосуществовали. Сырой Windows MCP output сохраняет координатор. Реальные main failures — RED этой test-validation доработки; им не приписывается зелёный результат.

После этих test-only edits повторный [Release focused GREEN](evidence/task-03-review-round-5-green.md#повторный-green-после-final-validation-доработки) — **385/385**, filter прежний: baseline + watcher delivery + весь SourceStructure. Exact inventory16 прежний. **CODE RE-FROZEN** сообщён сразу после результата; дальнейшие изменения исполнителя только в документации. Координатор запускает полные проверки заново на чистой final-r7 Linux-копии; неудачные старые прогоны и natural 60/60 не подменяют требуемые три Linux main suites.

## R7-001 — доказательство порядка публикации через hooks

Дата: 2026-11-03. Review раунда 7 выявил оставшееся предположение о монотонности UTC: Normal_publication_waits_until_watcher_startup_finishes сравнивал PublishedAtUtc с отдельным startupBoundary=DateTime.UtcNow. Это могло дать ложное падение при уже наблюдавшемся обратном ходе UTC.

Исправлен только этот тест. Снимок при удержанном before-start hook и снимок внутри after-start hook оба проверяются на отсутствие опубликованной identity, пустой inventory и default timestamp. После Load/capture проверяются опубликованная identity, nondefault timestamp, ровно два Known документа. Текущее UTC не читается, порядок следует из детерминированных hooks. Production и API hooks не менялись. Новый искусственный runtime RED не заявляется; прежние доказательства и сырые логи сохранены.

Аудит остальных task-03 baseline tests не нашёл других order proofs через wall-clock readings. UtcNow в общей fixture задаёт явно историческое mtime; отдельные timestamp/event tests сравнивают контролируемые mtime с PublishedAtUtc как предмет своего контракта. Их проверки не ослаблены.

[Focused GREEN R7-001](evidence/task-03-review-round-7-green.md): Windows Release, MCP, **385/385**, 0 failed, все123 SourceStructure включены; exact inventory16 не менялся. **CODE FROZEN** сообщён немедленно после результата. Единственный C# файл относительно предыдущего freeze — BaselineObservationGapTests.cs; report и новый GREEN evidence дополнены отдельно. Runtime model ID недоступен.

По сообщению координатора, до исправления R7-001 Windows main прошёл1687/1687, первые два Linux main — по1683/1683, третий явно отменён без TRX, поскольку требовалась эта правка. Эти результаты исторические, не заявлены как три финальных зелёных Linux прогона. Полную validation нового freeze выполняет координатор. Исполнитель не менял статусы/spec/README/version1.5.18, production или посторонний untracked skill; commit/push не выполнял. Счётчики замечаний и итоговую приёмку обновляет координатор после независимого review и обязательных прогонов.

## Повторная приёмка после раунда 8

- Дата: 2026-11-03. [Раунд8 — ACCEPT](evidence/task-03-review-round-8.md) подтверждает R5-001…R5-004 и R7-001. История task-03: **8 раундов, 8 исправленных уникальных замечаний** (R2-001, R2-002, R3-001, R5-001…R5-004, R7-001). Codex: независимые сессии1,3,4,6,7,8; Claude Opus5.5:2,5; runtime model ID Codex недоступен. Дополнительные фикстурные доработки R5-001/R5-002 не увеличивают число ID.
- [Окончательная validation](evidence/task-03-final-validation-round-8.md): Release solution build Windows и Linux успешны, Windows focused **385/385**, окончательный полный Windows main **1687/1687**, Windows opted-in AnalyzerLifecycle **13/13**. Три последовательных полных Linux main на окончательных исходниках — **1683/1683 каждый**, без падений и пропусков. Counters всех трёх TRX и lifecycle отдельно проверены, нулевые/пропущенные тесты за pass не принимались.
- [Linux логи и counters](evidence/task-03-final-validation-round-8-linux/summary.json) сохранены вместе с полным build output и SHA-256 manifest. Восемь изменённых C# файлов плюс восстановленный cached Integration сверены между Windows/Linux и с финальной сборкой; диагностическая instrumentation не переносилась в final binaries. Точный SourceStructure inventory16 не менялся, новых limits нет.
- Прежние неудачные и отменённые прогоны не подменяют финальные: [до раунда7](evidence/task-03-validation-before-round-7.md), [раунд7](evidence/task-03-review-round-7.md). На окончательном freeze предварительный Windows main дал1686/1687 из-за отсутствующего process-global cwd в неизменённом CLI-тесте; отдельный1/1 и полный1687/1687 повторы прошли без изменения кода. [Исходный failure и известная граница стабильности suite](evidence/task-03-windows-cwd-retry.md). Эта отдельная гонка не объявляется исправленной task-03.
- Clarification task-03: неигнорируемый файл без расширения остаётся обычной записью журнала без gap; ignored rename paths не журналируются. Это раздельно выражает уже заданные условия R5-003 и не добавляет отбор build-output из task-04. Его код не начат. [Task-08](task-08-estimate-tool-hi.md#вход-из-task-03) сохраняет факт консервативного rescan после настоящего gap и отсутствия полной истории не-C# входов.
- Три предупреждения обеих сборок прежние, вне задачи. Linux AnalyzerLifecycle и отдельный GC probe не запускались. Причина корректировки часов Linux не устанавливалась; controlled future-mtime RED не выдаётся за точную ветку каждого прежнего startup падения. Production timestamp guard сохранён.
- Версия1.5.18 сохранена. Остальные задачи не реализованы, эпоха целиком не принята, публичная активация не разрешалась. Посторонний untracked .agents/skills/prepare-github-release/ не включён в task-03.

## Возврат после раунда 9

- Дата: 2026-11-03. [Раунд 9](evidence/task-03-review-round-9.md), Claude Opus 5.5, вердикт **MUST-FIX**: приёмка раунда 8 снята. Результаты её validation остаются историей.
- R9-001: исправление R5-003 теряет переименования. В репозитории внутри каталога с игнорируемым именем из журнала пропадает переименованный файл, а переименование каталога не поднимает разрыв; под `obj` пропадает явный вход из карты.
- История task-03: **9 раундов, 8 исправлений**; R9-001 открыт.

## Реализация R9-001 — rename journal относительно workspace root

Дата: 2026-11-04. База bfbe27c, прежний исполнитель эпохи Codex, точный runtime model ID неизвестен. Scope — только [R9-001](evidence/task-03-review-round-9.md); итоговую приёмку и счётчики обновляет координатор после review/validation.

### Причина и исправление

Глобальный IsIgnoredPath учитывал bin/artifacts выше активного workspace и безусловно исключал endpoints под obj, хотя путь был явно известен карте. Новый internal root-relative overload повторно использует тот же набор ignored names, но смотрит только на descendants directory загруженного solution/csproj. Сам root, его ancestors и paths outside root не наследуют игнорируемое имя общего префикса. Public legacy overload не менялся.

WorkspaceInputSession хранит immutable WorkspaceDirectory, захваченный при создании watcher session. Это root файла, переданного load, а не ближайший project/search root. OnDiskWatcherRenamed использует эту session-фактуру внутри прежнего admission/closure gate, а не позднее изменяемый LoadedPath. Тестовые sessions без root сохраняют legacy fallback.

Карта имеет приоритет: если endpoint или его mapped descendant известен, baseline сохраняет оба конца перехода, включая unmapped tmp/new counterpart. Это отдельный HasMappedRenamePath, который не использует Consult.Unknown: Unknown мог быть следствием ignored ancestor, и не доказывает map membership. Strict HasDirectoryRenameEvidence сохранён отдельно; mapped extensionless FILE по equality не превращается в directory gap.

Mapped transitions записываются до прежней semantic классификации, чтобы та не скрыла unmapped counterpart внутри .git/obj. Для остальных rename paths QueueDiskPath получает отдельный recordBaselineWrite admission. Обычные Create/Changed/Delete используют прежний default, прежняя semantic classification/map/session revisions и refresh-эвристика не менялись. Root-relative policy применяется только к baseline rename journal/gap; task04 filtering не реализован.

### TDD и проверенные сценарии

Все tests написаны до production fix. Первый [RED](evidence/task-03-review-round-9-red.md): **29 выполнено,16 passed,13 failed**. Затем до поведения добавлены ещё3 cases — два native directory renames под obj с mapped descendants и explicit mapped .git input→unmapped destination; усилены явные Known/fresh-journal предусловия. Расширенный **32/16/16**. Отдельный native subset — **6/0/6**: EventRevision/DirectoryRenamePaths receipt уже подтверждён, failures находятся в journal/gap assertions, не в delivery timeouts. MCP выводит только первые5 failures, поэтому оба фактических отчёта сохранены.

Два ancestor roots — artifacts и bin. Native .cs rename даёт оба пути, native directory rename поднимает gap; obj directory descendants также сохраняют наблюдение. Mapped obj transitions проверены в трёх расположениях (обычный root/artifacts/bin) и трёх вариантах (mapped old, mapped new при tmp save, оба mapped). Настоящие File.Move/overwrite выполняются на диске; явная доставка callback изолирует именно Renamed. Изменяемые C# данные имеют контролируемый прошлый mtime, Known status и пустой journal для endpoints до операции, поэтому timestamp rejection не может ложно покрыть потерю rename.

Git add/commit table расширена на оба ancestor roots. В новых строках native watchers подавлены, затем доставляется metadata rename index.lock→index; реальные Git операции и file cleanup сохранены. Старые Git guards обычного root остаются native. Это честно ограниченная проверка rename-only контракта: прежние Changed/Created callbacks под ignored ancestor могут пройти Consult.Unknown и журналировать .git. Их поведение не менялось; не утверждается фильтрация всех native Git events. Шесть ignored сегментов вне карты, direct rename_project и остальные R5 guards сохранены. После production fix новые cases не добавлялись.

### GREEN и передача

[Focused GREEN](evidence/task-03-review-round-9-green.md): **491/491**, Windows Release, MCP build+test. Основной каталог405 (прежние385 +20 новых), дополнительно86 legacy classification/path/freshness/WPF guards. Все123 SourceStructure выполнены; строгий inventory16 identities/kinds/paths без изменений, новых limits нет. Три напечатанных предупреждения прежние, вне задачи.

После GREEN выполнены только mandatory call formatting и XML clarification null-root fallback, без semantic changes; **CODE FROZEN** сообщён после этой доводки. Финальные Windows build/main, три Linux main и AnalyzerLifecycle выполняет координатор на окончательных исходниках. StartDiskWatcher session construction относится к load path, поэтому lifecycle проверка включена в родительский план. Исполнитель не запускал runtime на parent Linux roots.

Изменены ровно5 C# файлов: Services/Models/WorkspaceInputSession.cs; Services/Workspace/WorkspaceDiskPathFilter.cs; Services/Workspace/WorkspaceDiskEventClassifier.cs; Services/Workspace/SolutionManager.cs; RoslynMcpServer.Tests/Testing/BaselineRenameJournalTests.cs. Дополнен настоящий report, добавлены task-03-review-round-9-red.md/-green.md. Старые evidence не переписывались.

R5-002 не переисследовался: прежний WSL trace и контролируемые mtime эксперименты не устанавливают причину потерянного KVM failure рецензента. Это ограничение сохранено, новое исправление ему не приписывается. Версия1.5.18, task/spec/README/status, task04+, посторонний untracked .agents/skills/prepare-github-release/ не менялись. IDE file edit API недоступен; применён разрешённый локальный fallback без Roslyn MCP apply_patch/update_file_content. Commit/push, финальное review, acceptance и bookkeeping остаются у координатора.

## Повторная приёмка после раунда 10

- Дата: 2026-11-04. [Раунд10 — ACCEPT](evidence/task-03-review-round-10.md): R9-001 закрыт. Всего task-03 **10 раундов, 9 исправленных уникальных замечаний** — R2-001/R2-002/R3-001, R5-001…R5-004, R7-001, R9-001. Codex: независимые сессии1,3,4,6,7,8,10; Claude Opus5.5:2,5,9. Точный runtime model ID Codex недоступен.
- [Финальная validation координатора](evidence/task-03-final-validation-round-10.md): обе Release solution build успешны, Windows focused **491/491**, Windows main **1707/1707**, Windows opted-in AnalyzerLifecycle **13/13**. Три последовательных полных Linux main на окончательном коде — **1703/1703 каждый**. Падений и пропусков нет; counters всех трёх TRX и lifecycle подтверждены отдельно.
- [Linux logs/counters](evidence/task-03-final-validation-round-10-linux/summary.json), полная сборка и source manifest сохранены. Все5 C# файлов сверены по SHA-256 между Windows/Linux и с финальной сборкой. После неё менялись только docs/status. SourceStructure strict inventory16 с прежними identities/kinds/paths сохранён, новых limits нет.
- Session.WorkspaceDirectory — неизменяемая база root-relative helper, captured до EnableEvents. Mapped endpoint/descendant сохраняет обе стороны rename, включая tmp save. Старый global overload WorkspaceDiskPathFilter и semantic classification/revisions прочих событий сохранены. Native .cs/directory проверки и real File.Move guards подтверждают поведение, metadata Git guards изолируют rename-handler; фильтрация всех Changed/Created Git callbacks не объявляется результатом R9-001.
- Для будущей task-04 доступен internal overload WorkspaceDiskPathFilter.IsIgnoredPath(path, workspaceDirectory); его вызов не заменяет map authority. Реализация task-04 не начиналась, переданные в раунде9 требования по build-output фильтрации сохранены.
- Три build warning прежние, вне задачи. Linux AnalyzerLifecycle и отдельный GC probe не запускались. KVM-причина старого R5-002 остаётся неизвестной, WSL evidence не выдаётся за её доказательство; согласно раунду9 это не новый блокер и не дополнительное исправление.
- Версия1.5.18 сохранена. Остальные задачи не реализованы, эпоха целиком не принята, публичная активация не добавлялась. Посторонний untracked .agents/skills/prepare-github-release/ не включён.
