# Эпоха 3. Результаты: лента изменений и восстановление покрытия

Статус: **реализована, приёмка не закрыта**. Версия сборки не менялась (`1.5.1` / `1.5.1.0`). Спека эпохи, README серии и архив эпохи 1 не переписывались. Отдельной эпохи 4 нет.

## Что сделано

Потребитель получает неизменяемый снимок через `SolutionManager.PullInputSnapshot` / `WorkspaceInputSession.Pull`. Снимок несёт generation, snapshot revision, membership revision, ревизии путей, всех владельцев и роли, pending, покрытие и причины unknown. Pull не собирает, не перезагружает, не потребляет изменения и не берёт семафор менеджера. Два чтения видят один и тот же снимок: одно чтение не очищает состояние для другого.

Поколение одно — `WorkspaceInputSession.Generation`, тот же token, что у проверки WPF и группы watchers. Второй счётчик сессии, второй реестр входов и вторая карта не добавлялись. Ревизии монотонны. Путь сообщает каждое владеющее вхождение. Обратные зависимости считает потребитель.

Собственная запись регистрируется до I/O (`BeginOwnWrite`). Событие до успешного persist остаётся pending и заранее echo не называется. После успешной записи сохраняются точные байты (кодировка и BOM) и отдельное уведомление каждому владельцу. Хеш авторство не доказывает. Частичная операция фиксирует только сохранённые пути. Неуспешная запись payload не оставляет. Повторная запись внутри `TryApplyChanges` идёт тем же протоколом: перед apply пути ставятся в pending, после успеха байты читаются с диска и сравниваются; совпадение с уже записанным payload второй ревизии не добавляет. Окно `TickCount` снято. Время может только отложить работу и не отбрасывает событие из-за прошедших тиков.

Callback watcher ставит в очередь путь и ревизию события. Содержимое файла в callback не читается. Сравнение делает обработчик очереди: два чтения текущих байт и сравнение с committed snapshot того же поколения. Совпадение байт при отсутствии более нового события пропускает повторную работу с содержимым: уведомление own-write уже было. Несовпадение — новая правка. Отсутствующий файл, ошибка чтения и нестабильное чтение остаются pending и ставят покрытие unknown. Delete, rename и смена membership текстовым совпадением не подавляются. Событие новее начала сверки старая проверка не снимает. Обнаруженная ABA-последовательность ревизию сохраняет. Reset и reload закрывают записи старого поколения; сравнение со снимком другого поколения не выполняется.

Generated и output производящей сборки в input revision этой сборки не входят. Для них растёт отдельный output revision. Другая подтверждённая роль того же пути считается отдельно. Неизвестная роль или неизвестный производитель — unknown.

Покрытие становится unknown при overflow буфера, ошибке старта watcher, потере наблюдаемого каталога и недоступном входе, с причиной и scope. Если scope назвать нельзя, неизвестность покрывает весь граф. Overflow и общая ошибка watcher — весь граф: потерянные пути не идентифицированы. Переименование каталога внутри membership region ставит `directory-rename-inside-membership-region`. Повторное чтение известных текстов причину не снимает. Complete возвращается только после новой оценки membership и imports (reload или тот же источник, который приняла эпоха 1), пересборки карты и подтверждённых подписок. Отменённая или неуспешная оценка, недоступный вход и ошибка watcher unknown сохраняют. Неподтверждённый вход другого TFM оставляет неизвестным весь граф; перечитывание выбранного inner TFM это не снимает.

Значимые правки C# и не-C# пишутся в сессию до появления потребителя. Семантический flush и отсутствие подписчиков ревизии, роли и причины unknown не очищают. Очистка очереди pending не превращает unknown в complete. Событие во время flush остаётся в снимке. Поздний callback старого watcher после reset/reload новое поколение не меняет: закрытая сессия события не принимает.

## Предел доверия FileSystemWatcher

`FileSystemWatcher.Error` поднимается не на каждое потерянное событие. Типичный сигнал — переполнение внутреннего буфера (`InternalBufferOverflowException`); часть потерь ошибкой не отмечается. Отсутствие callback не доказывает, что граф не менялся. Эта эпоха абсолютную свежесть из тишины watcher не обещает. Потребитель сам выбирает проверку перед тем, как пропустить сборку. Покрытие unknown фиксирует известные сбои наблюдения (overflow, ошибка старта, пропавший каталог, недоступный вход, directory rename, неподтверждённый TFM), а не каждый пропущенный callback.

Ошибка старта одного watcher в тесте — внедрённый `IOException` внутри `WorkspaceDiskWatcherStarter.Start` после создания watcher. Реальный отказ конструктора ОС не воспроизводился: `Directory.Exists == false` классифицируется как `watcher-directory-missing` до создания watcher. Соседний watcher при этом стартует.

## Что прогнано (Debug)

- `RoslynMcpServer.Tests.Workspace.WorkspaceInputFreshnessTests` — 20. Внешняя правка после own-write внутри прежнего tick-окна остаётся dirty независимо от задержки события. Частичная запись фиксирует только сохранённый путь; событие на несохранённом пути — не echo. Совпадающий echo вторую ревизию не добавляет, несовпадение добавляет. Событие до commit echo не является. Отличие только BOM — новая правка. Нестабильное чтение и событие новее начала сверки остаются pending. ABA сохраняет ревизию. Delete текстовым совпадением не подавляется. Overflow и сбой старта одного watcher остаются unknown после очистки очереди. Правка во время flush остаётся в снимке. Поздний callback старого поколения новый снимок не меняет. Отмена оценки и неуспешная запись echo не становятся. XAML, AdditionalFile и import без потребителя переживают семантический flush и видны первому pull. Generated/output не увеличивают input revision производителя. Directory rename остаётся unknown после перечитывания известных текстов и снимается только успешной переоценкой membership плюс подтверждёнными подписками. Неподтверждённый TFM — unknown на весь граф. Два pull делят generation; одно чтение другое не очищает. Pull не берёт семафор. Отсутствующий и недоступный файл остаются pending/unknown.
- `RoslynMcpServer.Tests.Workspace.WorkspaceWriteEncodingTests.UpdateDocumentInMemory_echo_does_not_add_a_second_input_revision` — 1. Реальный `LoadAsync` и `UpdateDocumentInMemoryAsync`: persist и повтор внутри `TryApplyChanges` дают одну input revision пути; синтетический Changed плюс сверка её не увеличивает. Generation pull совпадает с generation сессии.
- `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits` — 1. Инвентарь вырос с восьми до девяти: добавлен `DuringDiskFlushForTests` (`TargetOutsideTheScope`, `SolutionManager.cs`). Счётчик не ослаблен, ожидаются ровно эти девять.
- `WorkspaceDiskEventClassificationTests` — 6.
- `WorkspaceInputWatcherDeliveryTests` — 7, живые FileSystemWatcher и MSBuild load.
- `WorkspaceDiskWatcherStarterTests` — 3.
- `WpfTemporaryProjectCallbackTests` — 38, включая прежнее подавление self-write (теперь это pending до commit, а не окно тиков).
- `SolutionManagerDiskWatcherCallbackTests` — 2.
- `WorkspaceInputMapTests` — 20.
- `RoslynMcpServer.Tests.Editing.ProjectRenameHelperTests` — 6.

Полный набор `Category!=AnalyzerLifecycle` в Release (981 тест) дал четыре падения. Их причина исправлена, повторный прогон — у приёмки.

`SourceSetAnalysis.ModelFor` кэширует семантическую модель в обычном словаре. `ProductionAnalysis.Instance` — одна компиляция на процесс, и два теста ходят по ней параллельно. Оба промаха по одному и тому же дереву делают второй `Add`. Равенство `SyntaxTree` ссылочное, поэтому ключ — тот файл, который проиграл гонку (`PluginDiscoveryResult` в одном прогоне, `NuGetTools` в другом), а не дубль в scope. `DuringDiskFlushForTests` в инвентаре оставлен: flush его вызывает. Кэш теперь под замком.

`FileSystemWatcher` вместе с созданием, правкой или удалением файла поднимает `LastWrite` на родительском каталоге. Этот путь — сам каталог membership, не новый элемент компиляции. Классификация принимала его за potential membership и ставила composition stale: и на жизненном цикле `*_wpftmp.csproj` (graph-file при этом ложно), и на правке настоящего `.csproj` (graph-file верно, composition лишний). Каталог, который ещё существует, в очередь состава не входит. Rename каталога по-прежнему обрабатывается раньше. Временный проект по-прежнему отсекается `IsWpfTemporaryProject` до классификации.

Остальные факты `WorkspaceWriteEncodingTests`, `WorkspaceDocumentDiskSyncTests`, `WorkspaceSharedSourceSyncTests`, `WriteBoundaryChecksTests`, `SanitizedEntryChecksTests` и `DeadlockWitnessTests` в первом прогоне не запускались. Живой гонки «own-write A и внешний B внутри 1000 мс на настоящем watcher» нет: окно тиков удалено, а контракт проверен немедленным синтетическим событием B после commit A. Двух потребителей будущей серии test-build-freshness нет: два pull читает один тестовый код.
