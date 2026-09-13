ID: C-01
Severity: Blocker
Category: Data consistency

Target:
cache-contract.md / §4 «Профиль поддержки»; §5 «Membership scan»

Claim:
Профиль документирует все влияющие входы, включая отсутствие файлов в условных imports/`Exists` и новые файлы в wildcard-областях. Membership scan находит новые отрицательные зависимости. Появление значимого файла в Epoch 2 — полный miss.

Evidence:
README запрещает общий интерпретатор MSBuild. Публичные `Project`/`Document`/`AnalyzerConfigDocument` не содержат список неудавшихся `Exists`. DTB provenance «какие imports реально открылись» не даёт «какие пути были проверены и отсутствуют». Контракт (§5) говорит, что список уже разрешённых imports недостаточен, и тут же требует scan, который без этого списка не знает, куда смотреть за пределами уже известных путей.

Failure scenario:
1. Capture сохраняет разрешённые imports и documents.
2. Появляется файл, который меняет evaluation по `Exists`, но не лежит в сохранённой membership-области (или область не включала его, потому что его не было).
3. Hashes известных входов совпали → disk-hit со старым графом. V12 формально «должен» ловить это, но реализовать V12 из контракта нельзя.

Suggested change:
Сделать отрицательные зависимости либо обязательным полем snapshot, полученным из evaluation/binlog, либо явно `unsupported` для любого проекта с условным import вне статически закрытого набора. Пока ни того ни другого — §4 и §5 невыполнимы одновременно с запретом интерпретатора.

Confidence:
High

---

ID: C-02
Severity: High
Category: Concurrency

Target:
cache-contract.md / §7 «Согласованность поколений» / шаги 1–5

Claim:
Начать наблюдение за покрываемыми входами, сохранить счётчик событий; изменение / overflow / *непокрытый* источник событий отвергает кандидата. Строгий hit требует стабильных входов на период проверки.

Evidence:
Отвергнуть можно только событие, которое наблюдатель получил. «Непокрытый источник» — файлы, которые DTB прочитал вне заранее выбранного watch set. На первом capture watch set = области профиля, не результат ещё не закончившегося DTB. Поздние imports (NuGet-generated props, SDK extras, linked path вне корня) появляются *после* шага 1. Контракт сам пишет, что это optimistic validation, не filesystem snapshot, и просит документировать остаточный TOCTOU — но §7 шаг 5 всё равно формулирует «непокрытый источник → reject» как операциональный критерий.

Failure scenario:
1. Watcher стартовал на каталогах проектов.
2. DTB импортирует `obj/Project.nuget.g.props` или props выше корня `.sln` (watcher в коде стартует на `Path.GetDirectoryName(workspaceFilePath)`, `SolutionManager.StartDiskWatcherUnderLock`).
3. Файл меняют во время DTB; события нет; кандидат публикуется. Следующий процесс получает hit на поколение, которого «не было» на диске в момент конца DTB.

Suggested change:
Либо filesystem snapshot / volume shadow, либо capture-only при доказанно закрытом наборе путей *после* DTB (второй проход: узнать входы, потом пересчитать hashes — и всё равно TOCTOU между проходами). Убрать «непокрытый источник → reject» как будто это детектируемое событие.

Confidence:
High

---

ID: C-03
Severity: High
Category: Cache identity

Target:
cache-contract.md / §2 `RequestIdentity`; §9 «Эффективное состояние возвращается даже при RAM-hit»

Claim:
`RequestIdentity` включает все реально переданные global properties и различает отсутствие значения и явное значение. Ключ одинаков для RAM и диска, когда появится metadata mode.

Evidence:
`MsBuildWorkspaceProperties.IsSameLoadCache` сравнивает уже нормализованные `string?` (`SolutionManager.LoadCoreAsync`). RAM-hit при совпадении path+Configuration+Platform+TFM не читает диск и не пересчитывает identity. `buildArgs` в RAM не входит в ключ (`ApplySessionBuildArgs`). Overlay-флаг в ключ не входит. Контракт §9 прямо сохраняет существующий RAM path.

Failure scenario:
1. Первый load без `configuration` (SDK default Debug) — диск пишет identity «absent».
2. Второй load в том же PID с `configuration=Debug` — RAM-hit, диск не смотрит, контракт «absent ≠ explicit» обойден.
3. Новый PID: два ключа или miss при фактически том же DTB. Обратно: RAM считает разными нормализации, которые диск склеил. Слой, который должен быть строже, не участвует в самом частом пути.

Suggested change:
Либо выровнять RAM-ключ с `RequestIdentity` (breaking для cache-hit), либо вычеркнуть «absent vs explicit» из гарантий, пока жив текущий `IsSameLoadCache`. Не утверждать единую identity на оба слоя.

Confidence:
High

---

ID: C-04
Severity: High
Category: Completeness

Target:
cache-contract.md / §3 Provenance «полнота загрузки»; README «не трактовать неполную загрузку с диагностикой как полный snapshot»; epoch-2 «Capture разрешается лишь при подтверждённой полноте»

Claim:
Неполная загрузка с диагностикой не является доказанно полным snapshot. Capture только при подтверждённой полноте. Неразрешённые references запрещают сохранение.

Evidence:
Предикат полноты не определён. Сейчас `HasBlockingLoadFailure` = нет проектов или `WorkspaceDiagnosticFormatter.IsBlockingLoadFailure` на `_lastDiagnostics`. Pitfall 18: MSBuildWorkspace оборачивает обычные warning в `Kind=Failure` с текстом `Msbuild failed when processing the file`. Если взять Kind==Failure — capture почти никогда. Если взять текущий soft classifier — можно закэшировать граф без проекта / без `Compile` (pitfall 17) и назвать его полным.

Failure scenario:
1. Design-time warning обёрнут как Failure; capture запрещён на всех реальных sln — диск-кэш пуст.
2. Классификатор soft; один проект не открылся; snapshot «полный»; hydrate отдаёт граф без dependency; V01 navigation расходится со свежим MSBuild, который в другой попытке открыл проект.

Suggested change:
Привязать completeness к уже существующему blocking-classifier *и* к инварианту «каждый запрошенный root и все его project references имеют instance + Compile/documents». Явно: wrapped `Msbuild failed` без `error NU|MSB|NETSDK` не делает snapshot неполным и не делает его полным — нужен отдельный сигнал «project opened».

Confidence:
High

---

ID: C-05
Severity: High
Category: Overlay

Target:
cache-contract.md / §2 Overlay; §9 параметры только `useDiskCache` / `forceReload`

Claim:
Overlay — состояние сессии, повторно применяется к базе; temp-пути никогда не входят в snapshot. Смена семантического режима не должна молча возвращать старый граф (сказан о metadata references).

Evidence:
`shadowCopyInSolutionAnalyzers` — семантический режим исполнения analyzers/generators. Его нет в `RequestIdentity` и в предложенных параметрах Epoch 2. После рестарта PID session-sticky не существует. Prepare не является чистой функцией от snapshot: нужен provenance, output на диске, loader identity, политика main-only.

Failure scenario:
1. Disk-hit без флага → исходные `AnalyzerReference` → нет generation / MSB3027 на следующем `dotnet build` generator-проекта.
2. Disk-hit с флагом → prepare после «успешного» hit; first-semantic включает I/O и может fail-closed (`_solution` null) при том же дереве, которое вчера работало.
3. Ответ load говорит `disk`, агент считает семантику восстановленной.

Suggested change:
Режим overlay в identity и в metadata ответа. Disk-hit + отсутствующий/запрещённый prepare ≠ «та же семантика». Либо запретить disk-hit при необходимости overlay, либо включать prepare в критерий hit.

Confidence:
High

---

ID: C-06
Severity: High
Category: Snapshot

Target:
cache-contract.md / §3 «Generated compile inputs на диске, включая необходимые файлы из `obj`»; §6 SHA-256 всех значимых файлов

Claim:
Generated compile/editorconfig в `obj` хранятся как ссылки на проверяемые файлы. Их отсутствие/изменение — miss. SHA-256 всех значимых файлов, включая sources и DLL.

Evidence:
DTB и `dotnet build` переписывают файлы в `obj`. Содержимое части из них нестабильно (timestamps, file writes). Watcher и `WorkspaceDiskPathFilter` игнорируют сегмент `obj` целиком — live-сессия этих изменений не видит (см. E3-07). Контракт запрещает сериализовать generated *тексты* source generators, но обязывает хешировать SDK-generated compile items в `obj`.

Failure scenario:
1. Capture после load хеширует `obj/...AssemblyAttributes.cs` / `*.nuget.g.props`.
2. Агент делает `run_dotnet_build`. Даже при тех же `.cs`/`.csproj` часть `obj` меняется.
3. Новый процесс: miss на каждом цикле build→reload. Кэш пишется и почти никогда не читается. Если содержимое случайно совпало — hit; если DTB переписал тот же смысл другим текстом — ложный miss; если хеш не включили из-за prune `obj` — ложный hit (противоречит §5 «prune не отменяет probes»).

Suggested change:
Разделить стабильные provenance-файлы (`project.assets.json`, `*.nuget.g.props`) и эфемерные compile items. Для эфемерных — либо не включать в inter-process key (и признать дыру), либо считать любой build автоматическим miss. Нельзя одновременно хешировать `obj` как обязательный вход и ожидать hit после обычного агентского build.

Confidence:
High

---

ID: C-07
Severity: High
Category: Path / I/O

Target:
cache-contract.md / §5 «это не автоматически весь каталог монорепы»; README «Не расширять сканирование на всю монорепу»

Claim:
Membership scan ограничен областями профиля и DTB provenance, не всей монорепой. Prune обрывает рекурсивный обход.

Evidence:
Корень watcher и типичный `.sln`/`.csproj` этого репозитория — корень репо. SDK default `**/*.cs` + csproj в корне = membership area = дерево репозитория минус `bin`/`obj`/`node_modules`. Ограничение «не монорепа» не следует из алгоритма, а из расположения проектных файлов. На монорепе с одним `.slnx` в корне эффект тот же, что запрещённый full-scan.

Failure scenario:
1. Epoch 2 scan «по профилю» обходит весь репозиторий.
2. Бюджет probe съедает выигрыш DTB (или превышает его на холодном диске).
3. Handoff либо врёт про «ограниченные области», либо fail perf-gate, либо обрезает scan и пропускает V12.

Suggested change:
Задать верхнюю границу scan (список project directories + walk-up + explicit paths), не «глобус из csproj». Если csproj в корне — это *известный* worst case, его надо измерять в Epoch 0, не прятать за формулировкой «не монорепа».

Confidence:
High

---

ID: C-08
Severity: Medium
Category: Storage

Target:
cache-contract.md / §8 reader leases / PID reuse / cleanup

Claim:
Cleanup не удаляет активные поколения: нужны reader leases или эквивалент. Просроченные leases учитывают смерть процесса и PID reuse. Если безопасно освободить место нельзя — новая запись пропускается.

Evidence:
Интервал lease, refresh во время долгого hash/hydrate, и критерий PID reuse (PID+start time? service start?) не заданы. Hydrate большого графа + SHA-256 всех входов — минуты. Cleanup «вне критического пути semantic call» может идти параллельно в другом процессе.

Failure scenario:
1. Process A взял lease и хеширует 10k файлов.
2. Lease истек без refresh; Process B cleanup удаляет generation.
3. A читает битый/пропавший payload → `corrupt` / `hydrate_failed`, хотя hit был валиден. Либо PID reuse: cleanup считает чужой процесс держателем, кэш растёт до 2 GiB и перестаёт писать.

Suggested change:
Задать lease = PID + process start timestamp + явный heartbeat на время probe/hydrate. Без heartbeat cleanup не имеет права трогать generation с open-marker на том же volume.

Confidence:
High

---

ID: C-09
Severity: High
Category: Snapshot

Target:
cache-contract.md / §3 Documents «encoding при необходимости»; §6 хеш содержимого

Claim:
Хешируется содержимое всех значимых файлов (SHA-256). Encoding хранится при необходимости. Гидрация восстанавливает documents.

Evidence:
Эквивалентность байт на диске ≠ эквивалентность `SourceText` в workspace. MSBuild/Roslyn выбирает encoding из BOM/editorconfig/`DefaultCodePage`. «При необходимости» не говорит, когда encoding обязателен. Хеш байт совпадёт, декодирование при hydrate — нет.

Failure scenario:
1. Файл без BOM, в исходном MSBuildWorkspace открыт как Windows-1251.
2. Hydrate читает как UTF-8. Хеш тот же, `#if` и строковые литералы с ненулевым ASCII расходятся.
3. V02 «defines и видимые объявления совпадают» зелёный на ASCII-fixture и красный/ложнозелёный на реальном дереве.

Suggested change:
Хеш = байты. Snapshot для каждого document хранит encoding, которым его открыл исходный workspace, не «при необходимости». Oracle сравнивает `SourceText` checksum, не только path set.

Confidence:
High
