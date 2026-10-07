ID: E3-01
Severity: Blocker
Category: Lock

Target:
epoch-3-live-consistency.md / «Refresh и публикация сериализуются с semantic/edit lifecycle»; таблица `graph-dirty`

Claim:
Refresh сериализуется с semantic/edit. Нельзя держать workspace lock и рекурсивно вызывать метод, который снова его берёт. `graph-dirty` → обычный load полного scope *перед* semantic call.

Evidence:
`GetPublishedSolutionAsync` / `FindDocumentAsync` берут `_workspaceLock`, затем flush. `LoadAndPrepareAsync` берёт тот же lock на весь load+prepare (U-ARB-04). «Обычный load» изнутри semantic call — либо рекурсия на том же SemaphoreSlim (deadlock: не реентерабельный), либо release lock на время DTB. Release на время Open — окно, которое U-ARB-04 закрывал: второй tool может читать/писать `_solution` пока граф заменяется. Кандидат «публиковать целиком» эпохи 1/контракт §7 не описывает, кто держит lock, когда DTB идёт *из* find_symbol.

Failure scenario:
1. `find_usages` видит graph-dirty, вызывает load, не отпуская lock → hang до timeout клиента.
2. Отпускает lock, второй `apply_patch` пишет в старый workspace, load публикует новый — lost update / stale-base на write boundary.
3. Считают это «нельзя recurse» и выносят load наружу без смены API tools — агент должен сам вызвать `load_workspace`, тогда Epoch 3 не делает то, что обещает.

Suggested change:
Либо semantic tools не запускают DTB (только hint `graph-stale`, как сейчас), либо load остаётся единственной точкой публикации и Epoch 3 не существует как «перед каждым semantic call». Третий путь — явная очередь refresh с двумя фазами lock, это новая concurrency-модель, в эпохе объявленная как работа, но не спроектированная.

Confidence:
High

---

ID: E3-02
Severity: High
Category: Consistency

Target:
epoch-3-live-consistency.md / состояние `trusted`; «watcher — ускоритель, не доказательство»

Claim:
Для первой строгой реализации перед semantic call выполнять strict probes и membership scan даже в `trusted`. Факт `trusted` не разрешает бесконечно пропускать проверки.

Evidence:
Тогда `trusted` не дешевле `untrusted`. Состояния таблицы не меняют стоимость горячего пути. Текущая ценность MCP — повторные symbol query после одного load. SHA-256 всех значимых входов на каждый `find_symbol_*` на 100+ проектах — это и есть стоимость, которую диск-кэш должен был убрать. Эпоха сама говорит: если дорого — revise, не доверять watcher. Revise не специфицирован как допустимый shipped исход основного маршрута.

Failure scenario:
1. Реализуют буквально — каждый semantic call хеширует дерево; продукт хуже baseline.
2. Реализуют «trusted = доверие watcher» — нарушают этот же абзац, возвращают pitfall 20.
3. Handoff выбирает (2) и называет это Epoch 3 done.

Suggested change:
Убрать `trusted`+strict как обязательный shipped режим. Либо watcher+явный untrusted на overflow (текущая модель + честный hint), либо probes на `load_workspace` only. Не делать строгий inter-process протокол горячим путём.

Confidence:
High

---

ID: E3-03
Severity: High
Category: Classification

Target:
epoch-3-live-consistency.md / «Created/Deleted/Renamed … консервативно дают graph-dirty»

Claim:
Created/Deleted/Renamed, смена imports/configs/DLL, неизвестный путь и изменение членства консервативно дают graph-dirty. Не воспроизводить общий MSBuild glob.

Evidence:
Нет фильтра расширений. Текущий watcher: `Filter = "*.*"`, `QueueDiskPath` игнорирует не-`.cs` и не-graph; `nuget.config` *не* graph file (`WorkspaceDiskPathFilter.IsProjectGraphFile`). Epoch 3 расширяет классификацию до «любой Created». Editor/`git` создают tmp, lock, `.md`.

Failure scenario:
1. Сохранение в IDE пишет `Foo.cs~` / swap file в каталоге проекта.
2. Следующий `find_symbol_definition` запускает полный DTB (E3-01).
3. Либо фильтр оставляют как сейчас — тогда Created `.cs` в папке, которую glob не включает, всё равно graph-dirty, а `nuget.config` / `obj/*.assets.json` по-прежнему не видны.

Suggested change:
Задать классификатор событий отдельно от `WorkspaceDiskPathFilter` (контракт §5 это уже требует). Conservative graph-dirty только для путей из membership+explicit set, не для любого Created. Иначе DTB-шторм.

Confidence:
High

---

ID: E3-04
Severity: High
Category: FailurePath

Target:
epoch-3-live-consistency.md / состояние `refresh-failed`

Claim:
Ошибка/hint с причиной; не представлять прежний snapshot как свежий.

Evidence:
Сейчас semantic tools отдают опубликованный `_solution` даже при `_projectGraphStale` (stale hint на load, не hard fail поиска). File lock во время hash (`dotnet build` держит DLL/assets) — штатный агентский параллелизм. Fail-closed поиска ломает цикл build→diagnose.

Failure scenario:
1. `run_dotnet_build` и `find_usages` пересекаются.
2. Probe не может прочитать DLL/assets → refresh-failed.
3. Поиск возвращает ошибку; агент ретраит `load_workspace` → ещё один DTB. Доступность хуже, чем честный stale graph.

Suggested change:
Различить «доказано грязно» (graph-dirty → надо reload) и «проверка не выполнена» (оставить last trusted + `freshness=unknown`). Не смешивать в одном `refresh-failed`, запрещающем чтение.

Confidence:
High

---

ID: E3-05
Severity: High
Category: Classification

Target:
epoch-3-live-consistency.md / «Content-only sync допускается лишь если профиль доказывает, что содержимое этого файла не влияет на DTB»

Claim:
Иначе полный load. Все linked memberships обновляются вместе.

Evidence:
Доказать «содержимое не влияет на DTB» = модель evaluation (R-02). `.cs` compile item обычно не влияет; `.props` / `global.json` / `nuget.config` влияют; generated editorconfig — серая зона. Ошибка классификации `.props` как content-dirty: файла нет в `project.Documents`, `WithDocumentText` не применим, DTB не вызван — граф старый.

Failure scenario:
1. Watcher видит change `Directory.Build.props` (сейчас это graph-stale, не content).
2. Новый классификатор по «config» кладёт его в content-dirty.
3. Sync ищет document, не находит, помечает trusted/content-applied; следующий semantic на старых defines/refs.

Suggested change:
Белый список content-dirty: только пути, которые уже есть как compile/additional/analyzer-config documents *и* которые профиль явно исключил из evaluation inputs. Всё остальное — graph-dirty. Не «профиль доказывает».

Confidence:
High

---

ID: E3-06
Severity: High
Category: Storage

Target:
epoch-3-live-consistency.md / «При content sync disk snapshot обновляется только после подтверждения нового целостного поколения»

Claim:
Индекс, RAM и диск согласуются. Untrusted/stale не публикуются как reusable cache.

Evidence:
Целостное поколение при strict §6 = перехешировать все значимые входы и записать immutable generation. Это на каждый успешный `apply_patch`. Параллельный process reader (зомби MCP + новый) пересекается с частыми atomic pointer updates (C-02, C-08). Если диск *не* обновлять — RAM и диск расходятся, заголовок эпохи ложен.

Failure scenario:
1. Каждая правка переписывает `%LOCALAPPDATA%/.../eval-cache/v2`.
2. Cleanup/leases на гонках; либо диск отстаёт, новый процесс miss (E2-04) — «согласованность» только в одну сторону.
3. Исполнитель делает инкрементальный Merkle — это Epoch 4B, запрещённый попутно.

Suggested change:
В Epoch 3 диск обновлять только на graph-load, не на content-dirty. Тогда заголовок «RAM и диск согласуются» снять. Content-dirty живёт только в процессе (как сейчас `WithDocumentText`).

Confidence:
High

---

ID: E3-07
Severity: High
Category: Watcher

Target:
epoch-3-live-consistency.md / «Наблюдать корни membership и explicit inputs вне них»; overflow/untrusted

Claim:
Родительские configs и imports не теряются при загрузке вложенного `.csproj`. Overflow даёт untrusted и обход, включающий новые и удалённые файлы.

Evidence:
`StartDiskWatcherUnderLock` — один `FileSystemWatcher` на каталог файла workspace. `IsIgnoredPath` отбрасывает любой путь с сегментом `obj`/`bin` — в том числе `project.assets.json` и NuGet-generated props, которые контракт считает explicit probes. Родитель `Directory.Build.props` *выше* каталога `.sln` в watch set не входит. Контракт запрещает одну политику для watcher и probes; эпоха не говорит, что появится второй watcher/allowlist.

Failure scenario:
1. Меняется `..\Directory.Build.props` или `obj/project.assets.json`.
2. События нет. Если E3-02 ослабят, состояние остаётся trusted.
3. Если E3-02 оставят, probes на каждый semantic call всё равно увидят изменение — watcher «ускоритель» бесполезен, стоимость как E3-02.

Suggested change:
Явный набор watch targets = union(project dirs, walk-up, explicit files), с исключением prune *кроме* explicit. Пока используется текущий watcher+filter, гарантии эпохи 3 на ancestor/obj ложны.

Confidence:
High

---

ID: E3-08
Severity: High
Category: WritePath

Target:
epoch-3-live-consistency.md / «Успешные собственные записи уведомляют индекс напрямую»; «suppress-window не используется для сокрытия последующих внешних изменений»

Claim:
События дедуплицируются; окно suppress не прячет внешнюю запись по тому же пути.

Evidence:
`IsSelfWriteSuppressed` держит путь до `TickCount64` (`SolutionManager`). `apply_patch` пишет диск и подавляет echo. Внешний редактор/build может записать тот же путь в окне. Убрать suppress → echo собственного write = вторая нотификация; при conservative Created/Changed → graph-dirty после каждой собственной правки (E3-03).

Failure scenario:
1. Оставляют suppress + добавляют notify: внешний overwrite в окне потерян, индекс считает текст MCP.
2. Убирают suppress: каждый `apply_patch` → graph-dirty → DTB (E3-01).
3. Dedup по path без поколения: внешнее изменение с тем же size в окне схлопывается.

Suggested change:
Notify с content hash после успешной записи; suppress только для FSW echo с тем же hash. Иначе либо lost external write, либо DTB после каждого edit. Оба исхода опровергают эпоху.

Confidence:
High
