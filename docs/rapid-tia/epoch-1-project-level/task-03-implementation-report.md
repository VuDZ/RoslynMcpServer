# task-03 — отчёт реализации

- Дата: 2026-11-03.
- Исполнитель: прежний исполнитель эпохи Codex; точный runtime model ID средой не предоставлен.
- Scope: сессионный baseline, его публикация/чтение в фоне, поколения и журнал путей; интеграция с реальными MCP writes, disk watcher, reload/reset. Без графа, project mapping и публичного инструмента.
- Состояние: **reopened, 2026-11-03** — [ревью приёмки, раунд 2](evidence/task-03-review-round-2.md): MUST-FIX R2-001, R2-002. Приёмка по раунду 1 ниже снята этим ревью. Scope: baseline сессии, безопасная фиксация текстов/поколений и журнал/кандидаты путей; без графа, project mapping и публичного инструмента.

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

## Приёмка координатором

- Дата: 2026-11-03. [Раунд 1](evidence/task-03-review-round-1.md) — **ACCEPT**, must-fix нет; **1 раунд, 0 исправлений после независимого ревью**. Доводка до первого ревью, late guards и эквивалентная очистка assertions не входят в этот счётчик.
- Исполнитель: прежний исполнитель эпохи Codex. Рецензент: Codex, независимая сессия; точные runtime model ID недоступны.
- [Финальная validation](evidence/task-03-final-validation.md): Release solution build успешен; Testing + весь SourceStructure **351/351** (18 новых integration + 210 прежних baseline cases + 123 SourceStructure); полный Windows main suite **1660/1660**; opted-in AnalyzerLifecycle **13/13**. Все прогоны без падений и пропусков.
- AnalyzerLifecycle выполнен координатором через MCP runtime runner с `--environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, поскольку специализированные test API не принимают env. Уникальный TRX проверен отдельно: total=executed=passed=13, failed=notExecuted=error=timeout=aborted=0.
- Шесть новых xUnit2031 warnings устранены эквивалентными API assertions перед финальной сборкой. Остались три прежних предупреждения в файлах вне задачи. Linux suite и отдельный GC probe не запускались; source review подтверждает отсутствие Solution в завершённом состоянии, а R-15 лог сохраняет честную отмену без ожидания.
- Инвентарь SourceStructure сохраняет прежние 11 и ровно два новых limits: beforeRead/beforeCommit, TargetOutsideTheScope, source BaselineSession.cs. Проверка точных identities/kinds/paths не ослаблялась.
- В [task-08](task-08-estimate-tool-hi.md#вход-из-task-03) добавлена справка о фактическом входе RequiresFullScan/TouchedPaths: C# candidates после gap не доказывают полноту наблюдения всех входов. Будущий потребитель учитывает этот факт в существующей обработке Not covered/консервативного расширения; код task-08 не начат.
- Версия 1.5.18 сохранена. Task-04 и следующие задачи не реализовывались; эпоха целиком не принята. Известная стоимость fallback — повторное сравнение C# candidates после разрыва наблюдения; список содержит возможные пути, а не обещание точного журнала всех записей.
