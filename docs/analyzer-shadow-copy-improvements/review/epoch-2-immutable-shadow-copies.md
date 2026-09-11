ID: E2-01
Severity: High
Category: Contract

Target:
epoch-2-immutable-shadow-copies.md / line 16-18

Claim:
Идентификатор поколения зависит от содержимого обязательных файлов. На первом шаге это основная DLL; после эпохи 3 — весь набор runtime-зависимостей. Изменение только helper DLL должно менять поколение.

Evidence:
В одном абзаце: (a) epoch-2 hash = только основная DLL; (b) helper-only change обязан менять поколение. Helper в эпоху 2 не копируется (текущий `CopyToShadowDirectory` — DLL+PDB; PDB optional). Поколение с тем же hash главной DLL будет переиспользовано и не содержит нового helper.

Failure scenario:
1. Эпоха 2 принята: hash(main.dll) совпал, готовый каталог reused.
2. Helper на диске уже другой; в каталоге поколения helper нет либо старый (его там нет вовсе).
3. Эпоха 3 «изменение только helper» стартует с reused V1 generation и выглядит как баг загрузчика.

Suggested change:
Убрать из эпохи 2 требование «helper меняет поколение». Либо копировать и хешировать helper уже в эпоху 2 (тогда это не «после эпохи 3»). Не оставлять оба предложения.

Confidence:
High

---

ID: E2-02
Severity: High
Category: Identity

Target:
epoch-2-immutable-shadow-copies.md / line 54-55, 63-64

Claim:
Timestamp и размер допустимы как оптимизация, но не как доказательство идентичности при явном reload. Критерий: изменённая DLL с сохранённым timestamp получает новое поколение.

Evidence:
Оптимизация «не хешировать, если timestamp+size совпали» на cache-hit `load_workspace` (типичный «reload», см. E1-02) пропускает hash. Тогда изменённая DLL с сохранённым timestamp не получает новое поколение — прямое отрицание критерия на строке 64. «Явный reload» не определён; cache hit скорее всего попадёт под оптимизацию.

Failure scenario:
1. Копирование/FAT/`cp --preserve=timestamps` обновляет байты, ticks те же.
2. `load_workspace` cache hit, оптимизация skip hash.
3. Overlay продолжает указывать на старое поколение; критерий 64 красный, если его вообще гоняют на этом пути.

Suggested change:
Запретить timestamp/size как skip hash на любом пути, который спецификация называет reload. Оптимизация только для document edit после epoch-2 mapping уже в памяти (тогда hash вообще не нужен).

Confidence:
High

---

ID: E2-03
Severity: High
Category: Publish

Target:
epoch-2-immutable-shadow-copies.md / line 28-38

Claim:
Сформировать manifest, опубликовать каталог переименованием. Если другой участник уже опубликовал то же поколение, проверить его manifest и использовать готовый результат.

Evidence:
Шаг 3 — «сформировать manifest» (можно прочитать как in-memory). Шаг 4 — rename. Запись manifest в staging до rename не указана. Если dest уже есть, «проверить manifest»: не определено поведение, когда каталог есть, а файла manifest нет (rename чужого staging, частичный каталог, старый layout без manifest). `Directory.Move` на Windows не даёт POSIX-atomic rename semantics для непустых каталогов во всех случаях; пересечение с антивирусом/индексатором не описано.

Failure scenario:
1. Процесс A пишет файлы в staging, ещё без manifest, Move в dest.
2. Процесс B видит dest, manifest отсутствует.
3. B либо падает, либо использует dest без проверки содержимого — критерий «не заменять существующий» соблюдён, критерий «пригодная копия» нет.

Suggested change:
Контракт публикации: dest существует iff есть валидный manifest той же версии формата; иначе dest непригоден и не переиспользуется. Write manifest last in staging, затем один Move. Неполный dest не «использовать», а отклонять (не удалять чужой).

Confidence:
High

---

ID: E2-04
Severity: High
Category: OverlayApply

Target:
epoch-2-immutable-shadow-copies.md / line 47-53, 67-69

Claim:
При неудаче на первой загрузке сохранить исходную ссылку. Если ранее было готовое поколение, его сохранение допустимо только со статусом устаревшего. Подготовка в контролируемых точках; в памяти хранится результат. Document edit/disk sync сохраняют генерацию. Файловые операции вынесены из преобразования `Solution`.

Evidence:
Сегодня единственный reoverlay — полный прогон copier от `workspace.CurrentSolution` (`ApplyShadowCopyOverlayIfEnabled`). После эпохи 2, если edit всё ещё вызывает prepare: неудача recopy обязана либо вернуть stale generation (строка 49), либо original path (строка 48). Original path на missing-path repro = потеря генерации, что противоречит строке 67. Stale generation без статуса в MCP response нарушает инвариант 5 серии. Вынос I/O из transform подразумевает mapping в памяти; тогда prepare на document edit не должен происходить вообще (это уже контракт эпохи 6 lines 20-21), но эпоха 2 всё ещё говорит о «повторной подготовке идентичного поколения» (line 23-24).

Failure scenario:
1. Поколение опубликовано, генератор загружен.
2. Edit запускает prepare «на всякий случай», source временно locked сборкой шага 6.
3. Prepare fail → original refs → CS0103. Либо silent stale без статуса.

Suggested change:
Точки prepare: load, явный reopen, opt-in recopy. Document edit / watcher только применяют сохранённый mapping. Fail prepare на edit не существует, если I/O вынесен. Статус stale — только для reload, который пытался обновиться и не смог.

Confidence:
High

---

ID: E2-05
Severity: Medium
Category: Concurrency

Target:
epoch-2-immutable-shadow-copies.md / line 36-37, 65

Claim:
Конкурентные подготовки одного поколения завершаются одной пригодной копией. Если другой участник уже опубликовал — использовать готовый.

Evidence:
`SolutionManager` сериализует workspace операции `_workspaceLock`. Внутри одного процесса «два участника» почти не встречаются. Реальный участник — второй процесс MCP на тот же `GetDefaultShadowRootDirectory(loadedPath)` (hash полного пути sln). Эпоха 1 требует изолированный shadow root в тестах (epoch-1 line 54) → этот путь никогда не тестируется. Acceptance 65 будет закрыт unit-тестом двух потоков на одну папку, что не модель производства.

Failure scenario:
1. Два Cursor MCP, один repo, один shadow root.
2. Оба публикуют одно поколение; один видит dest mid-move.
3. Unit-тест потоков зелёный, поле — частичный каталог (E2-03).

Suggested change:
Либо тестировать межпроцессный publish на общем root, либо вычеркнуть «другой участник» и держать per-process root (тогда алгоритм с чужим dest лишний). Не делать и то и другое.

Confidence:
High

---

ID: E2-06
Severity: Medium
Category: Resources

Target:
epoch-2-immutable-shadow-copies.md / line 57-59

Claim:
Автоудаление опубликованных поколений вне эпохи. Нельзя удалять поколения, которые могут использоваться другим процессом.

Evidence:
Сейчас overwrite хотя бы переиспользует `{ticks}` при том же timestamp. Immutable + content hash = новый каталог на каждый rebuild. Loader держит файлы открытыми (`LoadFrom`). Даже если cleanup позже разрешат, locked gens на Windows не удалятся. `ARCHITECTURE.md` уже предупреждает о росте temp. Эпоха 2 делает рост линейным по числу сборок генератора за жизнь машины, не за жизнь поколения.

Failure scenario:
1. Агент в цикле rebuild+reload генератора.
2. Temp заполняется неизменяемыми копиями; старые всё ещё mapped.
3. Диск кончается раньше, чем эпоха 3 измерит «ограничения памяти и диска».

Suggested change:
Нельзя откладывать retention, если эпоха 2 запрещает overwrite. Минимум: уникальный root на PID/session и удаление собственного root при `ClearWorkspaceAsync`/`dispose`, не трогая чужие PID. Это не «удаление поколения, которое может использовать другой процесс».

Confidence:
High

---

ID: E2-07
Severity: High
Category: Loader

Target:
epoch-2-immutable-shadow-copies.md / line 7-9, 23-24, 63

Claim:
Проблема — повторная подготовка перезаписывает файл, плохо сочетается с блокировкой уже загруженной копии. Повторная подготовка идентичного поколения переиспользует копию без overwrite. Критерий: повторная подготовка уже загруженной DLL не пытается её перезаписать.

Evidence:
Даже при успешном overwrite `InProcessAnalyzerAssemblyLoader` вернёт ту же `Assembly` (`GetOrAdd` по пути). Запрет overwrite чинит sharing violation, но не «свежесть» при том же path. Эпоха 2 формулирует проблему как файловую. Для идентичного поколения reuse правилен. Для «того же path, другого содержимого» (текущая timestamp-схема при overwrite) reuse path — это и есть баг загрузчика, который эпоха 2 устраняет только если identity = content hash. Если реализация оставит timestamp в имени каталога и лишь перестанет overwrite — критерий 64 (новый content, старый timestamp) снова ломается.

Failure scenario:
1. Реализация: immutable, но id = ticks как сейчас.
2. Байты сменились, ticks нет, reuse каталога без overwrite.
3. Loader отдаёт V1 с Applied=true. Эпоха 2 «закрыта» по критерию 63.

Suggested change:
Критерий 63 привязать к content-id, не к «уже загруженной DLL». Явно: same content-hash → reuse path; different content → different path, даже если ticks совпали. Не считать файловый lock единственной причиной.

Confidence:
High
