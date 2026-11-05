# task-03 — отчёт реализации

- Дата: 2026-11-03.
- Исполнитель: прежний исполнитель эпохи Codex; точный runtime model ID средой не предоставлен.
- Scope: сессионный baseline, его публикация/чтение в фоне, поколения и журнал путей; интеграция с реальными MCP writes, disk watcher, reload/reset. Без графа, project mapping и публичного инструмента.
- Состояние: **reopened, 2026-11-05** — [ревью приёмки, раунд 21](evidence/task-03-review-round-21.md): MUST-FIX R21-001, [приёмка раунда 20 снята](#возврат-после-раунда-21). R14-001 закрыт. Scope: сессионный baseline, поколения, безопасная фиксация, журнал входов исчезнувшего каталога и lifetime-защита C# baseline/membership с derived ancestors. Известное ограничение: каталог с не-C# входом вне проекта. Без графа, task-04 реализации и публичного инструмента; исторические приёмки и возвраты сохранены.

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

## Возврат после раунда 11

- Дата: 2026-11-04. [Раунд 11](evidence/task-03-review-round-11.md), Claude Opus 5.5, вердикт **MUST-FIX**: приёмка раунда 10 снята. Результаты её validation остаются историей. R9-001 подтверждён закрытым.
- R11-001: при solution в подкаталоге `bin`, `obj`, `node_modules` и `.git` соседних каталогов больше не игнорируются. Переименование каталога там навсегда поднимает `RequiresFullScan`, а `git add` и `git commit` пишут `.git` в журнал. Тесты не теряются.
- История task-03: **11 раундов, 9 исправлений**; R11-001 открыт.

## Реализация R11-001 — ignored segments после общего префикса

Дата: 2026-11-04. База 6bedfcf, прежний исполнитель эпохи Codex; точный runtime model ID неизвестен. Scope — только [R11-001](evidence/task-03-review-round-11.md), без task04 implementation. Приёмка и счётчики остаются у координатора.

### Причина и минимальная правка

Двухаргументный WorkspaceDiskPathFilter.IsIgnoredPath делал outside-root path безусловно неигнорируемым. Когда load принимает build/All.slnx, paths src/App/obj, src/App/node_modules и root/.git находятся вне build и обходят фильтр. Исправлен только этот production helper: canonical target/workspace разбиваются на platform segments, совпавшее начало исключается с OS-aware InputPathCanon.Comparer, оставшиеся target segments проверяются прежним ignored set.

Literal common-prefix comparison выражает контракт напрямую, включая общий server segment разных UNC shares. Pure path guards покрывают drive/case, разные drives, общую UNC server часть с разными shares, sibling boundary и root itself, без UNC/network I/O. Nullable/invalid workspace directory сохраняет legacy whole-path fallback. Существующий public one-argument overload не менялся.

R9 map authority/both endpoints, strict directory evidence, immutable session root/admission, прочие watcher events и document sync не менялись. Исправление не реализует path mapping или build-output selection будущей task04. Неизвестная KVM-причина старого R5-002 не переисследовалась; WSL evidence не выдаётся за её доказательство.

### TDD и native границы

Все новые20 cases написаны до поведения:9 native и11 pure path guards. [Первый RED](evidence/task-03-review-round-11-red.md) — 20 выполнено,10 passed,10 failed. Отдельный native повтор9/3/6 выявил harness boundary: cross-parent node_modules/.staging/pkg→node_modules/pkg не дал ожидаемый Renamed receipt на Windows и завершился timeout. Это не засчитано behavioral RED, production не менялся. Final fixture использует same-parent .staging/pkg→pkg2; повтор двух ignored-directory rows до production fix дал **2/0/2**, оба failures на Assert.False(RequiresFullScan) после успешного native receipt. Остальные native failures — journal/gap assertions, guards сохранялись зелёными. Полные выводы и хронология сохранены; MCP показывает первые5 failures.

Native layout build/All.slnx с src/App покрывает obj directory rename, node_modules directory rename и obj file rename. Layout с root project покрывает реальные Git add/commit при включённом watcher и Directory.Move внутри .git. Последняя операция не называется Git rebase, node_modules операция не называется npm install; частота подобных действий в реальных инструментах не измерялась. Во всех final native cases отсутствуют Silence/Notify injection; Session.DirectoryRenamePaths/EventRevision доказывают receipt до baseline assertions.

Три native guards той же раскладки: source directory rename поднимает gap, .cs rename даёт оба journal paths, explicit obj/Manual.cs rename сохраняет map authority. Контролируемые прошлые mtimes и Known/fresh-journal assertions исключают ложное покрытие по timestamp rejection. Старые R9 artifact/bin и mapped old/new/tmp guards не менялись и входят в final focused.

### GREEN и передача

[Focused GREEN](evidence/task-03-review-round-11-green.md): **511/511**, Windows Release, specialized Roslyn MCP build+test; прежние491 +20 новых, все123 SourceStructure. Strict inventory16 identities/kinds/paths без изменений. Четыре новых Windows-only pure path cases помечены RequiresWindows и не заявляются Linux coverage. Final incremental build напечатал прежний CS8603; full build warnings проверяет координатор.

**CODE FROZEN** сообщён немедленно после GREEN, code/style cleanup завершён до него; после freeze — только docs. Изменены3 C# файла: Services/Workspace/WorkspaceDiskPathFilter.cs; RoslynMcpServer.Tests/Workspace/WorkspaceDiskPathFilterTests.cs; новый RoslynMcpServer.Tests/Testing/BaselineNestedSolutionRenameTests.cs. Дополнен report, добавлены task-03-review-round-11-red.md/-green.md, старые evidence сохранены.

Final Windows build/main, три Linux main, lifecycle по применимости, fresh review, status/bookkeeping и commit/push выполняет координатор. Исполнитель не запускал runtime на parent Linux roots. Версия1.5.18, spec/README/status, task04+, посторонний untracked .agents/skills/prepare-github-release/ не менялись; commit/push не выполнялись. IDE file edit API недоступен, применён разрешённый local filesystem fallback без Roslyn MCP apply_patch/update_file_content.


### Поздняя точная Windows-only inventory доработка

После первоначального freeze3 C# файлов и review12 полный main обнаружил пропущенную reviewed identity: Linux1718/1719 и Windows1726/1727, единственное падение Windows_only_category_matches_the_reviewed_list. Production/native failures не было. [Parent evidence до раунда13](evidence/task-03-validation-before-round-13.md) и [дополнение RED](evidence/task-03-review-round-11-red.md) сохраняют эту фазу; первоначальный focused511 не включал PlatformSupport inventory.

В WindowsOnlyTestInventoryTests.cs добавлены только ожидаемая WorkspaceDiskPathFilterTests.Workspace_filter_respects_windows_roots_without_filesystem_access и English why-comment о Windows drive/UNC/case semantics. Это одна identity с4 theory rows; reviewed inventory теперь5 методов. Строгий Assert.Equal и категории не менялись. Production code не менялся. Нового finding ID или дополнительного behavior fix эта bookkeeping доработка не вводит.

[Relevant focused GREEN](evidence/task-03-review-round-11-green.md): **28/28**, Windows Release MCP build+test, filter FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskPathFilterTests. Отдельный искусственный RED не нужен: оба реальных full-main failures предшествовали изменению. MCP напечатал существующий xUnit1031 в WpfTemporaryProjectCallbackTests.cs:195.

**CODE FROZEN повторно сообщён немедленно после GREEN28/28.** Final C# overlay4 файла: Services/Workspace/WorkspaceDiskPathFilter.cs; RoslynMcpServer.Tests/Workspace/WorkspaceDiskPathFilterTests.cs; RoslynMcpServer.Tests/Testing/BaselineNestedSolutionRenameTests.cs; RoslynMcpServer.Tests/PlatformSupport/WindowsOnlyTestInventoryTests.cs. Далее только docs. Финальные build/main,3 Linux main, lifecycle по применимости, review13 и acceptance/bookkeeping/commit/push остаются у координатора. Версия1.5.18 и остальные границы задачи сохранены, runtime model ID неизвестен.

## Повторная приёмка после раунда 13

- Дата: 2026-11-04. [Раунд13 — ACCEPT](evidence/task-03-review-round-13.md): R11-001 закрыт. Task-03: **13 раундов, 10 исправленных уникальных замечаний** — R2-001/R2-002/R3-001, R5-001…R5-004, R7-001, R9-001, R11-001. Codex: независимые сессии1,3,4,6,7,8,10,12,13; Claude Opus5.5:2,5,9,11. Runtime model ID Codex недоступен.
- [Финальная validation координатора](evidence/task-03-final-validation-round-13.md): обе Release solution build успешны, Windows focused **513/513**, весь SourceStructure123; полный Windows main **1727/1727**, opted-in AnalyzerLifecycle **13/13**. Три последовательных полных Linux main — **1719/1719 каждый**, без падений и пропусков. Все TRX counters Linux и lifecycle проверены отдельно.
- [Полные Linux логи и counters](evidence/task-03-final-validation-round-13-linux/summary.json), build output и source manifest сохранены. Все4 изменённых C# файла сверены Windows/Linux по SHA-256 с финальной сборкой и после прогонов; далее только docs/status. Strict SourceStructure inventory16 identities/kinds/paths сохранён, новых limits нет.
- R11-001: helper исключает только общий начальный набор canonical segments; оставшиеся target segments проверяются и за пределами solution directory. Native obj/node_modules/.git guards и реальные git add/commit при активных watchers не дают ложного gap/journal; source directory/source file/mapped obj сохраняют нужное наблюдение. Map priority/R9 ancestor-root guards и прочие события не менялись. Task-04 ещё не реализована.
- [Предварительные main failures](evidence/task-03-validation-before-round-13.md) не скрыты: Win1726/1727 и Linux1718/1719 из-за одной пропущенной exact Windows-only inventory identity. Запись добавлена, strict equality/categories сохранены; это часть R11-001, не новый finding ID. Все финальные наборы повторены после доработки.
- Первая cross-parent node_modules fixture имела receipt timeout, исключённый из behavioral RED; corrected same-parent native rows дали2/0/2 после receipt до production fix. Directory.Move внутри .git не выдаётся за Git rebase, node_modules move — за npm install. [RED/GREEN](evidence/task-03-review-round-11-red.md) сохраняют фазовую историю. Прежнее поведение Changed/Created под ignored ancestor остаётся границей task-04; фильтрация всех таких Git events не заявляется.
- Три build warning прежние, вне задачи. Windows AnalyzerLifecycle повторён как дополнительный regression check watcher filtering; load/publication code на этом раунде не менялся. Linux AnalyzerLifecycle и отдельный GC probe не запускались. KVM-причина старого R5-002 неизвестна, согласно review11 это не новый блокер.
- Версия1.5.18 сохранена. Остальные задачи не реализованы, эпоха целиком не принята, публичная активация не добавлялась. Посторонний untracked .agents/skills/prepare-github-release/ не включён.

## Возврат после раунда 14

- Дата: 2026-11-04. [Раунд 14](evidence/task-03-review-round-14.md), Claude Opus 5.5, вердикт **MUST-FIX**: приёмка раунда 13 снята. Результаты её validation остаются историей. R11-001 подтверждён закрытым, три полных main на Linux — 1719/1719.
- R14-001: каталог, исчезнувший без `Renamed`, даёт в журнале только свой путь, а флаг опущен, поэтому известный вход под ним выглядит неизменённым. На настоящем watcher Linux это каталог, вынесенный за пределы дерева, и перенос сразу после создания нового родителя; для подключённых ссылкой каталогов нет и признака устаревшего графа. Для Windows последовательность подставлена, настоящий запуск требуется от исполнителя. Это не регрессия R11-001.
- История task-03: **14 раундов, 10 исправлений**; R14-001 открыт.


## Реализация R14-001 — исчезновение каталогов без Renamed

Дата:2026-11-04, база1b617c4, прежний исполнитель эпохи Codex; runtime model ID неизвестен. Scope только [R14-001](evidence/task-03-review-round-14.md), без task04/public activation. Статусы и окончательная приёмка остаются у координатора.

### Фактическая причина и минимальная правка

QueueDiskPath учитывал только путь Deleted каталога. При Directory.Move Windows может передать Deleted старого каталога + Created нового, без Renamed и child events; mapped C# документ сохранял Known baseline, но не попадал в touched journal при опущенном RequiresFullScan. Watcher содержимого отдельного linked каталога также не гарантирует событие движения своего root. [Native RED](evidence/task-03-review-round-14-red.md) закрепляет реальную Windows cross-parent sequence и linked cases с независимым observer receipt.

Для Deleted path без exact map entry SolutionManager один раз перечисляет map descendants и проводит каждый через существующий QueueDiskPath. Это добавляет их journal entries и обычные input revisions/dirty/composition signals. Каждый descendant имеет exact lookup, поэтому дальнейшего tree scan не вызывает; обычное удаление известного файла обходится без scan. Необратимый baseline gap не вводится, обычное recursive delete — в том числе linked root — остаётся no-gap. Known snapshots сохраняются, правила write invalidation незавершённого capture прежние. Для исходного ignored/unmapped Deleted path отдельно подавляется baseline journal по root-relative filter; его semantic classification/revision handling не меняется. Новое число descendant signals, естественно, больше прежнего. Explicit obj/artifacts mapped inputs сохраняют приоритет.

WorkspaceDiskWatcherStarter сохраняет существующие descriptors, recursion и *.* point delivery. Только непокрытым root lifetime добавлены exact-name DirectoryName-only nonrecursive subscriptions в родителе. Фильтрация полного old/new path не допускает чужие parent events. Guard цепочка защищает собственные ancestors до уже наблюдаемого parent, дедуплицируется; membership/search roots и recursive ancestor watching не расширяются. Startup/error handling общее, все watchers попадают в прежнюю группу disposal, callback admission/closure прежний. Публикация по-прежнему следует за startup. Missing descriptor не превращается в ancestor watch. Глобального polling/rescan framework и новых public API нет.

### TDD и честные фазы

1. До production написаны18 cases:16 native +2 deterministic Deleted/Created orders на реальной MSBuild загрузке. Initial18/3/15 и corrected18/5/13 полные выводы сохранены. Два initial failures навязывали лишний no-journal ordinary Empty/Unmapped contract; assertions ограничены ignored paths до production. Кроме того, скрытый cap первых5 obj failure был fixture Known/Single precondition из-за mixed separators; он не считается behavioral RED. Поэтому в обоих каталогах12 behavioral failures, остальные явно отделены.
2. До production добавлен19-й case — linked own-root recursive deletion guard. Targeted7/2/5: оба recursive-delete guards passed; оба deterministic orders, cross-parent и оба linked move cases действительно failed. Windows cross-parent Directory.Move: Deleted Sub, Created Other/Sub, без Renamed. Никакого mv/git mv не запускалось, переносы каталога не называются действиями Git/npm.
3. Первый GREEN-кандидат145/144/1: все123 SourceStructure и starter guards прошли, obj fixture precondition failed. Поздняя harness correction канонизировала Same helper и source path. Только собственный2-file production patch временно снят; targeted obj/artifacts pair на baseproduction дал2/0/2 (дважды, final pair с canonical source), затем patch восстановлен. Ни один новый behavior case после реализации не добавлен; late phase сохранена целиком в RED.
4. [Итоговый focused GREEN](evidence/task-03-review-round-14-green.md) — **537/537**, Windows Release specialized Roslyn MCP build+test. Включены все Baseline tests, watcher starter/callback/delivery/freshness/classification guards, reviewed Windows-only inventory и все123 SourceStructure. Strict inventory16 identities/kinds/paths в SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits не менялся; новых limits нет. Три напечатанных warning прежние: CS8603 NuGetFallbackAssemblyResolver, CS8601 lifecycle HostSession, xUnit1031 WpfTemporaryProjectCallbackTests.

Новые19 cases:4 glob/explicit/root/build move-out,2 linked-root observation layouts,1 Windows cross-parent,2 mapped ignored-segment inputs,2 deterministic callback orders,2 recursive deletes,6 unknown/ignored/empty guards. Historical mtimes и Known/fresh-journal предусловия сохранены. Eight-second bounded polling не используется как доказательство времени/порядка; receipt и состояние проверяются явно. Настоящий directory-rename gap сохраняют прежние native cases final focused.

### Передача и пределы

**CODE FROZEN** сообщён сразу после537/537 и завершённого style cleanup; после него только docs. C# files3: Services/Workspace/SolutionManager.cs; Services/Workspace/WorkspaceDiskWatcherStarter.cs; новый RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs. Добавлены evidence/task-03-review-round-14-red.md/-green.md и это дополнение отчёта. Старые raw outputs/acceptance evidence не переписывались.

Исполнитель не запускал Linux runtime или parent final roots; Windows focused не заменяет3 Linux main, full Windows main, Release solution build, opted-in AnalyzerLifecycle и fresh review15 координатора. KVM-причина старого R5-002 остаётся неизвестной и не переисследовалась. Версия1.5.18, spec/README/status, task04+, unrelated skill не менялись; commit/push не выполнялись. IDE editing API недоступен, применён разрешённый local filesystem fallback без Roslyn MCP apply_patch/update_file_content. Status/acceptance/commit/push только у координатора.


### Поздняя resource доработка после review15 и первого Linux main

Первоначальный freeze537/537 прошёл parent Windows main1746/1746, но [Linux1736/1738](evidence/task-03-validation-before-round-16.md) показал inotify user limit128 IOException в PluginStartupTests и неожиданно затронутый source в timestamp guard. Parent после suite измерил0 видимых inotify instances, max_user_watches524288, fdlimit10240; этот снимок не измеряет peak. Точная причинность timestamp failure не установлена. [Review15](evidence/task-03-review-round-15.md) вернул R14-001 для bounded resource correction; это не новый unique finding ID.

Проверка выявила concrete FSW multiplier: lifetime seeds создавались и для SDK pure metadata/output roots. До дополнительной production правки написаны13 late diagnostic cases:12 starter-role count rows и1 real MSBuild App. [Actual RED13/9/4](evidence/task-03-review-round-14-red.md):3 excluded-role rows failed0vs1parent guard, все9 retained guards passed; ordinary App имел13planned descriptors,25actual watchers и13extra roots. Отдельно видны4 unique unsupported packs parents и дублирующий shared parent guard;8 AnalyzerConfig ancestor guards остаются в согласованном scope. Это measured watcher-count multiplier, но не установленный peak inotify usage в момент Linux failures.

Согласованная с review15 минимальная правка исключает только новые nonrecursive lifetime seeds с непустым Roles, состоящим целиком из MetadataDependency/Output. Остальные/empty/mixed роли и recursive membership seeds сохраняются. Existing content watchers всех ролей и parent-boundary применение pure metadata/output descriptors остаются прежними. C# baseline keys physical project.Documents имеют UserInput/Generated либо Unknown, поэтому их guards не отрезаны. Полная lifetime coverage pure metadata/output roots теперь явно не заявляется; source shortcut comment фиксирует границу и необходимость расширения, когда такие inputs получат baseline coverage. Дополнительного descriptor fact/API/polling framework не вводилось; limits и test parallelism не повышались.

Fresh [focused GREEN550/550](evidence/task-03-review-round-14-green.md) — Windows Release specialized MCP, тот же полный filter, все123 SourceStructure и строгий16-limit inventory сохранены. Три warning прежние. Real App count test пишет aftercounts в ITestOutputHelper; specialized successful output их не возвращает, поэтому фактические aftercounts здесь не приписаны. Linux pressure окончательно устранённым не объявляется до parent main при прежних штатных limits.

**CODE RE-FROZEN** сообщён немедленно после550/550; далее только docs. Current C# overlay4files: Services/Workspace/SolutionManager.cs; Services/Workspace/WorkspaceDiskWatcherStarter.cs; RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs; RoslynMcpServer.Tests/Workspace/WorkspaceDiskWatcherStarterTests.cs. Всего32newcases:19original +13late resource diagnostic cases. Старый firstfreeze3files остаётся историей. Parent выполняет новые final build/Windowsmain/Linux3/lifecycle, freshreview16, статусы и commit/push. Исполнитель не запускал runtime на Linux parent roots. Версия1.5.18, spec/README/status и unrelated skill не менялись, model ID неизвестен.


### Транзитивный boundary, timestamp диагностика и Output notice race — после раунда16

[Review16](evidence/task-03-review-round-16.md) выявил ложную lifetime boundary: existing pure output Shared parent наблюдает Shared/Sub, но после seed exclusion никто не обязан наблюдать перемещение самого Shared. Chain eligible linked source останавливалась слишком рано. Перед closure fix написаны2 starter registration rows и real-MSBuild fixture External/Shared(Output)/Sub(Known source). [RED](evidence/task-03-review-round-14-red.md): Windows actual registration3/0/3; отдельный parent Linux native1/0/1 с независимым Renamed Shared→Detached receipt, sourceKnown/map preconditions и missing journal при опущенном gap. [Native counters](evidence/task-03-r16-boundary-native-red.json), raw log/source manifest сохранены у координатора.

Первая Windows Directory.Move попытка завершилась AccessDenied до события; её1/0/1 не считается behavioral RED, причина отказа не устанавливалась. Final portable case безskip/новойкатегории: Windows проверяет физическую registration нужного ancestor guard и deterministic Deleted admission; Linux делает настоящий move. Это не заявляется Windows native move proof. mv/git mv не выполнялись.

Минимальная closure правка в starter: покрывающая primary subscription завершает chain лишь когда сам descriptor eligible lifetime seed. Иначе chain продолжается через excluded parent; его content watch повторно не создаётся. Исключение standalone pure metadata/output seed остаётся, но primary metadata/output parent eligible C# descendant получает transitive root protection. Новых моделей/API/глобального watcher framework нет. SourceStructure подтвердил прежние16 exact limits без новых identities/kinds.

[Parent validation до17](evidence/task-03-validation-before-round-17.md): предыдущие Windows1759/1759 и Linux1750/1751 с timestamp failure сохранены историей; отдельный timestamp1/1 не заменяетmain. Для точной диагностики добавлены3 test-only изменения: WorkspaceRecordingLogger.Exceptions queue при прежнем Messages; optional logger в BaselineLifecycleFixture; ITestOutputHelper и strong initial/prewrite no-gap/no-sourceTouched preconditions в timestamp test. Original postwrite DoesNotContain и Unknown/timestamp outcome не ослаблены. Phase dumps включают RequiresFullScan, coverage/reasons, sourceRevision, events, directory renames, subscriptions и полные startup exceptions. Diagnostic Linux main1750/1751 на прежнем production: timestamp PASS с RequiresFullScanfalse и sourceRevision0; его прежняя причина остаётся Unknown, logger не объявляется её исправлением. Sampled0.1s inotify observation120/128 не является exact peak measurement.

Тот же diagnostic main дал независимый RED в Explicit_obj_input_is_synced_and_generated_outputs_do_not_bump_input_revisions: Output notice требовалась до ожидания её доставки, хотя ожидались notices других файлов. В этот существующий fact добавлен bounded WaitUntil с тем же Output/!CountsAsInputRevision predicate до прежних assertions и unchanged input revision check. Это test-only async receipt correction; production classification/revisions не менялись ради race. Источникfailure и полныйparentoutput сохранены в before17 evidence.

Fresh [Windows focused GREEN553/553](evidence/task-03-review-round-14-green.md), Release specialized Roslyn MCP с build: все Baseline/registration/доставка/freshness/classification/Windows inventory guards и все123 SourceStructure прошли. Строгий16-limit inventory и3прежнихwarnings сохранены. Linux final GREEN и устранение непойманных timestamp failures этим результатом не заявляются.

**CODE RE-FROZEN** сообщён немедленно после553/553; далее только docs. Current C#8files: Services/Workspace/SolutionManager.cs; Services/Workspace/WorkspaceDiskWatcherStarter.cs; RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs; RoslynMcpServer.Tests/Workspace/WorkspaceDiskWatcherStarterTests.cs; RoslynMcpServer.Tests/Support/WorkspaceRecordingLogger.cs; RoslynMcpServer.Tests/Testing/BaselineLifecycleFixture.cs; RoslynMcpServer.Tests/Testing/BaselineSessionIntegrationTests.cs; RoslynMcpServer.Tests/Workspace/WorkspaceInputWatcherDeliveryTests.cs. Всего35newcases:19initial +13resource +3boundary; старые timestamp/output facts усилены, без новых counts. Только исходные2 production files, остальные6C# — тесты/fixture/support. Parent owns final builds/mainWindows/Linux3/lifecycle, freshreview17, acceptance/status/commit/push. Версия1.5.18, requirements/README/status/unrelated skill сохранены; runtime model ID неизвестен.


### Последний согласованный build-fix: C# baseline seeds и native missing-root Error

[Before18 validation](evidence/task-03-validation-before-round-18.md) сохранила обязательные failures предыдущегоfreeze: Linux1745/1754 (9прямыхinotify128 exceptions) и Windows1761/1762 (native no-gap guard). [Review17](evidence/task-03-review-round-17.md) принимало код условно по последующей validation, потому task acceptance не объявлялась. Нижеследующие2 correction остаются аспектами R14-001, не новымиuniqueIDs.

1. **Standalone guard scope теперь соответствует physical C# baseline.** Baseline.Inventory только project.Documents, sourceDocument RoleForDocument возвращает UserInput/Generated/Unknown. Lifetime predicate: recursive OR empty roles OR Any(UserInput,Generated,Unknown,PotentialMembership). Остальные подтверждённые non-C# роли сами не начинают цепочку; все их прежние content subscriptions остаются, а C# derived ancestors продолжают chain через любые excluded primary parent boundaries. Mixed и Unknown/empty остаются консервативно eligible. Это не полная lifetime coverage allnonC# inputs и не task04implementation.
2. **Доказанный Windows RootGone Error больше не превращает обычный recursive delete в gap.** Targeted native2/1/1 доbehaviorfix показал Linked=True, origin Shared/Sub, directoryExistsFalse, Win32Exception5 AccessDenied, watcher-error, renamepaths empty. Это точныйroot-disappearance сценарий, а не предположение по общему IOException. Missing-root nonoverflow Error с parent теперь направляется как Deleted в shared onChanged→classifier/journal всех mapped descendants; обычныйrecursiveDelete безgap. Overflow даже при missingroot и genuine existing-root Error идут onError→gap; volume root безparent также conservativegap. Старые raised rename/overflow gaps не очищаются, admission closed session сохранён. Diagnostic origin logging защищён catch от provider exceptions в nativecallback.

TDD [полныеactual RED](evidence/task-03-review-round-14-red.md): передC#scopeproduction скорректированы3избыточныхpositive nonC#expectations; добавлены PotentialMembership и3nonC#transitive boundary rows. RED21/16/5 включаетscope3, realAppcount иlinkeddelete;16guards прошли. Diagnostic-only starter origin logging затем дала native2/1/1 с точным missingroot. После подтверждения добавлены8 deterministic framework OnError rows на реальных зарегистрированных handlers (reflection защищённогоframework API, не sourceinspection), RED8/5/3 передroutingfix. Guard variants: missingnormal/overflow, existingnormal/overflow, priorrename/overflowgap retained, closed-session no mutation. Native strict noGap assertion сохранён; bounded1s diagnosticsettle не заменяет deterministic routing tests.

Related firstGREEN173/173 включал все123SourceStructure. Actual Windows App watcher measurements:13planned descriptors,25actual доpureMeta;20послеpureMeta с8SDKconfigguards;**12actual,0extra послеC#scope**. Один regular descriptor dedup. Послеfix через MCP execute_dotnet_command выполнен counttest1/1 сrawTRX, поскольку specializedGREEN не печатает pass stdout; [counters+stdout](evidence/task-03-r18-app-counts.json), artifacts/task-03-r18-app-counts/app-counts.trx. Это локальный count, не утверждение универсального Linuxpeak; limits и globalparallelism не менялись.

[Freshfinalfocused565/565](evidence/task-03-review-round-14-green.md), Windows Release specialized MCP build+test: весь Baseline, starter/callback/delivery/classification/freshness/Windows inventory и все123SourceStructure. Strict16 identities/kinds/paths unchanged, новыхlimitsнет. Триexistingwarnings сохранены; последний incrementalreport warnings не напечатал. Permanent timestamp diagnostics остаются, прежняя причина timestamp failures всё ещё Unknown и исправлениемlogger не объявляется.

**CODE FROZEN** сообщён немедленно после565/565, всеC#/style/logcleanup донего; далее толькоdocs. Current8C#files: Services/Workspace/SolutionManager.cs; Services/Workspace/WorkspaceDiskWatcherStarter.cs; RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs; RoslynMcpServer.Tests/Workspace/WorkspaceDiskWatcherStarterTests.cs; RoslynMcpServer.Tests/Support/WorkspaceRecordingLogger.cs; RoslynMcpServer.Tests/Testing/BaselineLifecycleFixture.cs; RoslynMcpServer.Tests/Testing/BaselineSessionIntegrationTests.cs; RoslynMcpServer.Tests/Workspace/WorkspaceInputWatcherDeliveryTests.cs. Новыхcase rows47:19initial +13resource +3boundary +4C#scope +8Errorroute; существующиеfacts усиленыбезsoftening. Старыеболее широкие role expectations исторические, superseded согласованнымC#scope. Parent owns последнийmandatoryround18: Windows/Linuxbuild, focused, Windowsmain, Linux3normalruns, opted-inlifecycleW/L, freshreview18/status/commit/push. Доэтихрезультатов задача непринята. Версия1.5.18, spec/README/status/unrelatedskill не менялись, exactruntimeID неизвестен.


### Review18 refinement: optional exact entry может быть каталогом

[Review18](evidence/task-03-review-round-18.md) нашёл оставшийся shortcut внутри R14-001: map.Find(path)!=null не доказывает исходный файл. Builder хранит optional walk-up candidates по File.Exists, поэтому каталог App/Directory.Build.props с Known source под ним имеет exact EvaluationInput entry Present=false. Deleted такого каталога прежде журналировал только root, а Source оставался вне journal при опущенном gap.

До production добавлены4 real-MSBuild rows: props/targets × native/deterministic, [actual RED4/0/4](evidence/task-03-review-round-18-red.md). Native Windows Directory.Move дал Deleted root + Created Detached, без Renamed; independent observer и Known/exactPresentfalse/Directory.Exists preconditions подтверждены. Deterministic cases подавляют native delivery и вызывают Deleted после физического move. До production добавлен5-й nested optional row с двумя project instances и разными sources; final RED5/0/5 записан отдельно. Ни одного case после реализации не добавлено.

Минимальный production delta: exact fast path только для Present=true file entry; strict descendant comparison исключает self, включая trailing separator normalization. Outer scan уже покрывает все mapped descendants, поэтому private expandDeletedDescendants flag передаётся false synthetic calls. Вложенные optional entries не повторяют scan и не порождают recursion loop; оба source EventRevision в nested regression увеличиваются ровно на1 за одно admitted Deleted. Для одного directory event — один map enumeration плюс обычная classification каждого descendant; стоимость occurrence processing не скрывается в O(1). Обычный present known-file delete по-прежнему не сканирует map. Публичных опций/API, новых моделей и task04 features нет.

[Focused GREEN570/570](evidence/task-03-review-round-18-green.md), Windows Release specialized MCP с build: все123 SourceStructure и прежний строгий16-limit inventory unchanged. Incremental build напечатал существующий CS8603; full warning inventory проверяет координатор. Baseline hashes/keys/identity, no-gap recursive delete, C#-only independent guard scope, transitive ancestors и overflow/error guards сохранились.

**CODE FROZEN** сообщён немедленно после570/570, далее только docs. В этом refinement изменены2 C# файла: Services/Workspace/SolutionManager.cs и RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs; общий overlay остаётся8 C# файлов предыдущего списка. Добавлены отдельные review-round-18-red.md/-green.md, старые evidence сохранены. Новыхcase rows теперь52 (прежние47+5 optional-entry), source inventory16 unchanged. Версия1.5.18, task/spec/README/status/unrelated skill не менялись; commit/push не выполнялись, exact model ID неизвестен.

[Mandatory checks565 tree](evidence/task-03-validation-before-round-19.md) были зелёными: Windows1774/1774 и3×Linux1766/1766. Это историческая фаза до нового fix, не final acceptance. Coordinator повторяет required builds/main/lifecycle и independent review19, затем владеет статусами/commit/push. Unknown причина старых timestamp failures здесь не переинтерпретируется.


### Review19 refinement: WPF filename prefilter перед mapped Deleted admission

[Review19](evidence/task-03-review-round-19.md) нашло следующий bypass внутри R14-001: IsWpfTemporaryProject возвращал до descendant stage. Валидный каталог Scratch_wpftmp.csproj/Sub/A.cs с Known Compile input выглядел temporary project filename, Deleted каталога терял source безgap. Отдельно подтверждён ещё один valid C# baseline input: Compile Item_wpftmp.csproj. Baseline.Inventory индексирует project.Documents без extension filter, поэтому synthetic queue этого документа тоже нельзя терять из baseline journal.

Полный QueueDiskPath prefilter audit: canonical path validation необходима и не исключает valid map key; Changed-existing-directory и own-write content deferral не применяются Deleted; known map entries проходят ignored authority; Known document classification не возвращает ProvenIrrelevant. Единственный filename-specific early bypass — WPF suppression. Mapless behavior не расширяется.

До production написаны8 real-MSBuild cases, [actual RED8/2/6](evidence/task-03-review-round-19-red.md). Пять rows покрывают WPF-named directory/обычныйsource, обычныйdirectory/WPF-namedsource и combo;2native и3deterministic. Known source и occurrence.DocumentId preconditions подтверждены для необычного extension. Windows native Directory.Move дал independent Deleted original + Created Detached, безgap и безsourcejournal. Direct mapped-file deletion —6-й failed case. Два actual temporary XML-file guards сmap/безmap прошли уже на RED. MCP показывает первые5 failure cards, direct-file stack сохранён в stdout; harness failures не было. Позднихcases после реализации нет.

Минимальная правка переносит captured-map Deleted descendant stage сразу после path validation, до filename suppression. Внутри прежнего WPF return отдельная Deleted+mapped DocumentId baseline admission записывает source path; graph/classification/dirty behavior WPF-looking root/file сохраняется. Обычные mapped descendant classification проходят существующим путём. Mapless, loaded project membership, actual temporary-file suppression, normal present-file fast path, strict self exclusion и synthetic non-expansion остаются. Никаких public configs/API/task04 semantics не вводилось. Live Changed semantic support необычных filename extensions не заявляется.

[Fresh focused GREEN578/578](evidence/task-03-review-round-19-green.md), Windows Release specialized MCP с build: полный Baseline и все WPF/delivery/freshness/classification/starter/callback guards, Windows inventory и все123 SourceStructure. Strict16 identities/kinds/paths unchanged,3existingwarnings прежние. **CODE FROZEN** сообщён немедленно после578/578, далее толькоdocs. Current8C#overlay тот же; delta570→578 только Services/Workspace/SolutionManager.cs и RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs. Новыхcase rows теперь60 (прежние52+8WPF), old evidence immutable.

[Parent before20](evidence/task-03-validation-before-round-20.md) сохранил Windows1779/1779 и3×Linux1771/1771 прежнего570 tree. Эти успехи не заменяют final validation послеfix и не означают acceptance. Координатор выполняет freshreview20, final builds/main/lifecycle и bookkeeping/commit/push. Версия1.5.18, task/spec/README/status/unrelatedskill не менялись, commit/push не выполнялись, exact model ID неизвестен. Причина старых timestamp failures по-прежнему Unknown.

## Повторная приёмка после раунда 20

- Дата: 2026-11-04. [Раунд20 — ACCEPT](evidence/task-03-review-round-20.md): R14-001 закрыт. Task-03: **20 раундов, 11 исправленных уникальных замечаний** — R2-001/R2-002/R3-001, R5-001…R5-004, R7-001, R9-001, R11-001, R14-001. Codex: независимые сессии1,3,4,6,7,8,10,12,13,15,16,17,18,19,20; Claude Opus5.5:2,5,9,11,14; runtime model ID Codex недоступен.
- [Финальная validation координатора](evidence/task-03-final-validation-round-20.md): Release solution build Windows/Linux успешны; Windows focused578/578, все123 SourceStructure; Windows main1787/1787; три последовательных полных Linux main1779/1779 каждый. Opted-in AnalyzerLifecycle Windows13/13 и Linux13/13. Падений и пропусков нет; Linux/lifecycle TRX counters проверены отдельно.
- [Linux logs/counters](evidence/task-03-final-validation-round-20-linux/summary.json), lifecycle output и SHA-256 manifest сохранены. Все8 C# файлов совпадают Windows/Linux с финальной сборкой; после неё только docs/status. Strict SourceStructure inventory16 identities/kinds/paths не менялся, новых limits нет.
- Deleted каталога разворачивается в mapped descendants под прежним session gate; exact mapped file не сканирует map повторно. Сохраняются map authority под obj/artifacts и прежние rename guards. Root lifetime seeds ограничены C# baseline/membership и консервативными empty/Unknown/PotentialMembership ролями; independent non-C# seeds сохраняют content watchers. Транзитивная защита C# descendants проходит через excluded primary parents без duplicate subscriptions. Полная lifetime coverage всех non-C# inputs не заявляется, task-04 не реализована.
- Mapped Deleted descendants обрабатываются перед WPF filename suppression; Known TextDocument с временно-похожим именем сохраняется в baseline journal без расширения semantic/graph suppression настоящих WPF temporary files и mapless callbacks. [Review19 RED/GREEN](evidence/task-03-review-round-19-red.md) закрепляют root/child/combo и legacy guards.
- Exact optional candidate Present=false не считается подтверждённым файлом: props/targets каталоги разворачиваются в strict descendants, exact self исключён. Внутренние synthetic calls не перечисляют subtree повторно, nested optional entries дают ровно одну запись/revision каждого физического входа. [Review18 RED/GREEN](evidence/task-03-review-round-18-red.md) содержит native Windows Deleted/Created и deterministic multi-project proof до fix.
- Windows ordinary linked-root deletion: доказанный Win32Exception5 при уже отсутствующем Shared/Sub классифицирован как Deleted через тот же journal path. Overflow и ошибка существующего root по-прежнему дают gap, прежний gap не очищается, закрытая сессия не мутирует новый baseline. [RED/GREEN](evidence/task-03-review-round-14-red.md) сохраняют exact origin trace и deterministic routing guards.
- [Windows App counts](evidence/task-03-r18-app-counts.json):13 planned descriptors,12 actual watchers,0 extra roots против прежних25 и20. Inotify128, max_watches524288, fd10240 и штатная16CPU параллельность Linux не менялись. Diagnostic sampling120/128 не объявляется строгой верхней границей peak.
- Промежуточные failures не скрыты: [до16](evidence/task-03-validation-before-round-16.md), [до17](evidence/task-03-validation-before-round-17.md), [до18](evidence/task-03-validation-before-round-18.md). Причина двух прежних timestamp failures не установлена; logger/успешный isolated run не выдаются за доказательство исправления. Assertions timestamp усилены, Output notice теперь явно ожидается перед прежним строгим role/revision assertion. Все финальные main suites выполнены после последних изменений.
- Native Windows cross-parent trace Deleted→Created без Renamed записан. Для сложного output-parent boundary Windows Directory.Move получил AccessDenied до события: это operation limitation, не behavioral RED; там actual registration/deterministic Deleted, на Linux настоящий move с independent observer. mv/git mv не запускались.
- Три build warning прежние, вне задачи. Отдельный GC probe не запускался. Причина старого KVM R5-002 остаётся неизвестной и не объясняется новым Windows trace.
- Версия1.5.18 сохранена. Остальные задачи не реализованы, эпоха целиком не принята, публичной активации нет. Посторонний untracked .agents/skills/prepare-github-release/ не включён.

## Возврат после раунда 21

- Дата: 2026-11-05. [Раунд 21](evidence/task-03-review-round-21.md), Claude Opus 5.5, вердикт **MUST-FIX**: приёмка раунда 20 снята. Результаты её validation остаются историей. R14-001 подтверждён закрытым на Linux, три полных main — 1779/1779.
- R21-001: каждое `Deleted` обходит всю карту входов до фильтра игнорируемых путей. Удаление 20 000 файлов из `node_modules` на настоящем watcher Linux поднимает необратимый флаг при 50 и 200 исходниках: 9 из 9 на `e116df1` и 0 из 9 на `1b617c4`. Тесты не теряются. Это регрессия исправления R14-001.
- Известное ограничение, решение владельца: каталог с не-C# входом вне проекта, наблюдаемый только собственным watcher, при выносе за пределы дерева не даёт сигнала. Исправлять в task-03 не нужно.
- История task-03: **21 раунд, 11 исправлений**; R21-001 открыт.
