# Эпоха 2. Результаты: watchers и общая синхронизация текста

Статус: **принята**. На момент реализации версия сборки не менялась (`1.5.1` / `1.5.1.0`). Спека эпохи не переписывалась. Серия закрыта как v1.5.2.

## Что сделано

Watchers стартуют из описателей карты эпохи 1 (`InputWatcherDescriptor`), а не из расширенного `ComputeWatchRoots`. Рекурсия остаётся только у каталогов загруженных проектов. Внешний linked-файл, walk-up props/targets и внешний glob получают нерекурсивную подписку на каталог самого файла. Нерекурсивная подписка на тот же каталог, где уже есть рекурсивная область проекта, не дублируется и не поднимается до рекурсии. Отсутствующий каталог записывает `watcher-directory-missing` и не заменяется watcher предка. Ошибка старта — `watcher-start-failed`. Ошибка живого watcher — `watcher-error`; исключение логгера в error/rename callback процесс не убивает.

Событие классифицируется по карте и ролям. Один подтверждённый текст пользовательского `.cs` читается с диска один раз и применяется ко всем `DocumentId` роли user input этого пути. Документ с ролью generated того же пути текстом не обновляется. Additional documents и analyzer config documents этой серией не синхронизируются. `search_code` и `ComputeWatchRoots` не расширялись и не сужались.

Новый, удалённый и переименованный путь в значимой области помечает composition stale. Оба пути rename учитываются. Callback не вызывает `AddDocument` / `RemoveDocument` и не пишет `Compile Include` в `.csproj`. `Compile Remove` по-прежнему не является доказанным исключением (`ProvenExcluded` остаётся false): такой файл не игнорируется. Новый `.cs` внутри каталога проекта, которого нет в обратном индексе, консервативно composition stale.

Переименование каталога внутри membership region ставит покрытие `unknown` (`directory-rename-inside-membership-region`). Повторное чтение известных `.cs` эту причину не снимает. `Directory.Exists` не единственное доказательство: отсутствующий каталог, который задевает область или является предком индексированного пути, тоже считается directory rename.

Явный user input в `obj` обрабатывается по адресу и увеличивает input revision проекта. Generated, output и unknown provenance уведомляют и в input revision производящего проекта не входят. Смешанная роль сохраняет оба вхождения: учитывается только user-потребитель. Подавление echo после apply — прежнее окно `TickCount` (`SuppressDiskWatchForPath`). Это не протокол сравнения байтов.

Наблюдение живёт в существующей `WorkspaceInputSession` (те же generation и token WPF). Семантический flush notices, input revisions и причины unknown не очищает. Отдельной ленты ревизий и pull provider нет.

## Реальные FileSystemWatcher

Фикстура `WorkspaceInputWatcherDeliveryTests` грузит два SDK-проекта через `LoadAsync`. Linked `Shared.cs` лежит вне обоих каталогов проектов. `Directory.Build.props` — на три уровня выше проекта. Внешний glob, явный `obj\Manual.cs`, `Compile Remove` и BOM-файл входят в ту же загрузку. Семь фактов прошли в Debug.

- Изменение `Shared.cs` доходит через живой watcher. Следующий `GetPublishedSolutionAfterDiskSyncAsync` видит один и тот же текст в обоих проектах. `DefineConstants` `APP` и `OTHER` не смешиваются: у App есть `AppOnly` и нет `OtherOnly`, у Other наоборот. Байты обоих `.csproj` и `.sln` после flush совпали со снимком до записи.
- Появление glob-файла, правка файла из `Compile Remove`, удаление и rename файла помечают composition. Число документов solution не выросло, новый путь в документы не попал, байты `.csproj` и `.sln` не изменились.
- Подписки: каталог linked и каталог props нерекурсивные, каталог проекта рекурсивный. Рекурсивной подписки на каталог props нет и нет рекурсии вне каталогов проектов. Изменение существующего props и появление отсутствовавшего `Directory.Build.targets` увеличивают input revision. Файл во вложенном чужом дереве предка за 1,5 с не дал notice и не попал в dirty. Сосед внешнего glob записан как unknown, в composition не входит, причина `external-glob-completeness-unknown` сохранена.
- `Directory.Move` каталога внутри каталога проекта ставит `directory-rename-inside-membership-region`. Перечитывание `Program.cs` обновляет его текст и оставляет покрытие unknown.
- `obj\Manual.cs` доходит до watcher, текст попадает в документ, input revision растёт. Повторные правки `AssemblyInfo`, запись output и файл неизвестного происхождения в `obj` дают notices без увеличения той же revision и без dirty/composition.
- Корни поиска равны прежнему `ComputeWatchRoots`. Явный `directoryPath` и пустой workspace ведут себя как раньше. Каталог linked в корни не входит. Соседний `Loose.cs` виден watcher как unknown и в поиск не добавляется.
- После flush файл с BOM сохраняет `EF BB BF`, файл без BOM преамбулу не получает. Байты `.csproj` и `.sln` те же.

Отдельный `WorkspaceDiskWatcherStarterTests` (без полного MSBuild load) проверяет ту же границу подписок: нерекурсивный watcher предка видит `Directory.Build.props` и не видит файл во вложенном чужом каталоге; появление отсутствующего props доставляется; отсутствующий каталог не заменяется watcher родителя и пишет `watcher-directory-missing`.

## Граница записи

`WorkspaceSharedSourceSyncTests` на adhoc solution: одно чтение диска обновляет оба user-документа общим `SourceText`; parse options проектов остаются своими; generated-документ того же пути не получает `WithDocumentText`; BOM остаётся на `SourceText.Encoding` (пустая преамбула или `EF BB BF`).

Живой flush (`persistDocuments: false`) в delivery-фикстуре не переписал `.csproj` и `.sln` и не снял/не добавил BOM на диске. Перед `TryApplyChanges` dirty-пути помечаются прежним tick-окном, чтобы echo apply не записался второй правкой входа. Второй диск-write одного пути в одном persist пропускается, чтобы linked-файл не получил другой текст от второго документа.

`WriteBoundaryChecksTests` (10), `SanitizedEntryChecksTests` (74) и `DeadlockWitnessTests` (2) прошли. Инвентарь delegate-limits в `Production_reachability_records_only_the_known_delegate_limits` расширен четырьмя параметрами `WorkspaceDiskWatcherStarter.Start` (`log`, `onChanged`, `onRenamed`, `onError`): анализ не связывает аргумент с параметром. Прежние четыре предела сохранены с теми же kind и путями. Счётчик не ослаблен: ожидается ровно эти восемь.

## Прочее, что прогнано (Debug)

- `WorkspaceDiskEventClassificationTests` — 6: composition без `AddDocument`, доказанно посторонний файл, явный obj против generated/output/unknown и смешанная роль, non-C# notice без text sync, directory rename при уже отсутствующем каталоге, loose-сосед не в поиске.
- `SolutionManagerDiskWatcherCallbackTests` — 2, включая throw логгера в error и directory rename.
- `WpfTemporaryProjectCallbackTests` — 38.
- `WorkspaceDocumentDiskSyncTests` — 6.
- `WorkspaceInputMapTests` — 20.
- `SolutionManagerWatchRootsTests` — 6.
- `UtilityToolsSearchScopeTests` — 4.

Полный набор `Category!=AnalyzerLifecycle` в Release не гонялся. Это зона приёмки, не этой эпохи.

## Намеренно оставлено эпохе 3

- Pull provider неизменяемого снимка (generation, snapshot revision, курсоры, потребитель). `InputRevisionFor` и notices — наблюдение сессии, чтобы generated/output не записывались как правки user input. Второй ленты нет.
- Протокол own-write по байтам. Окно `TickCount` на месте и не считается доказательством совпадения байтов.
- Отдельный сигнал «output изменился» для результата сборки. Сейчас output только notice без input revision.
- Возврат покрытия в complete только после новой оценки membership и imports. Эпоха 2 unknown не снимает.
- Overflow буфера и затронутый scope шире уже записанных строк причин.
- Контракт позднего callback через reset/reload как снимок для потребителя.
