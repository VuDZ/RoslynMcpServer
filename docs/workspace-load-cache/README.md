# Кеш загрузки больших решений: эпохи реализации

Каталог — **план**, не описание текущего runtime. Документы рассчитаны на отдельные
чаты реализации **по порядку**. Состояние репозитория после предыдущей эпохи —
base truth; следующие эпохи не начинать заранее. После каждой shipped-эпохи
чат пишет `epoch-N-handoff.md` (как в `docs/compact-tools/`) и синхронизирует
`docs/ARCHITECTURE.md`. Пока эпохи не shipped, current-state остаётся только в
`docs/ARCHITECTURE.md`: in-process кеш `MSBuildWorkspace` на время жизни процесса.

Язык этих файлов — русский. Код, `[Description]`, README продукта и
`ARCHITECTURE.md` при реализации остаются на том языке, на котором они сейчас.

## Зачем

Холодный `load_workspace` на 100+ проектов — это N× design-time MSBuild
(`OpenSolutionAsync`). Visual Studio ускоряет reopen через CPS-кэш в `.vs/`;
`MSBuildWorkspace` его **не читает**. Публичный Roslyn `IPersistentStorageService`
удалён. Свой диск-кеш строится на слое **результата evaluation + индекса файлов**,
не на pickle `Solution`/`Compilation`.

Сессионный RAM-кеш (`SolutionManager.LoadCoreAsync`, ключ path + Configuration +
Platform + TargetFramework) уже есть и **умирает вместе с процессом** (Reload MCP,
publish, рестарт Cursor/OpenCode, `reset_workspace`). Повторный `load_workspace` в
том же PID дешёвый. Боль — новый PID.

Целевая среда: монорепа (в т.ч. VCS **ast**, форк git с другим CLI). Инвалидация
диск-кеша **не** вызывает git/ast.

## Статус эпох

| Эпоха | Файл | Статус |
|-------|------|--------|
| 1 | [epoch-1-msbuild-fast-open.md](epoch-1-msbuild-fast-open.md) | planned |
| 2 | [epoch-2-lazy-project-load.md](epoch-2-lazy-project-load.md) | planned |
| 3 | [epoch-3-evaluation-snapshot.md](epoch-3-evaluation-snapshot.md) | planned — центр фичи после рестарта процесса |
| 4 | [epoch-4-symbol-index.md](epoch-4-symbol-index.md) | planned — опционально, после 3 |

## Фиксированные решения (не пересматривать без новой эпохи)

- Не читать и не писать VS `.vs` / `.dtbcache`.
- Не подключать внутренний Roslyn SQLite / `IChecksummedPersistentStorageService`.
- Не сериализовать `Solution`, `Compilation`, SyntaxTree, тексты исходников.
- Инвалидация диск-кеша: индекс `(path, LastWriteTimeUtc, Length, content hash)` +
  Merkle каталогов. VCS (`git.exe`, `ast`, `GitChangedFilesHelper`) в этот путь
  не входит. `get_changed_files` не менять ради кеша.
- Файлы графа (`.sln`/`.slnx`, `.csproj`, walk-up `Directory.Build.*` /
  `Directory.Packages.props` / `nuget.config` / `global.json`, `project.assets.json`)
  на холодном open **всегда** перехешировать. Для `.cs`/`.razor`/`.cshtml` зонд
  size+mtime, затем hash при miss.
- Список документов из snapshot брать только если Merkle исходников проекта совпал.
- Analyzer shadow-copy overlay (`GetCurrentSolution`) остаётся in-memory **после**
  гидрации; shadow-пути в диск-кеш не писать.
- CrossTargeting: ключ кеша включает inner `TargetFramework`. Outer evaluation не кешировать.
- Ключ кеша = абсолютный путь загруженного `.sln`/`.csproj` + Configuration +
  Platform + TFM. Не один файл на корень монорепы.
- Обход только конуса решения. Не сканировать весь `…/monorepo`.
- Prune каталогов через общий [`WorkspaceDiskPathFilter`](../../Services/WorkspaceDiskPathFilter.cs)
  (рекурсию обрывать). Не индексировать `node_modules` и аналог. Allowlist расширений
  для Merkle исходников; не вычёркивать целиком `wwwroot`/`ClientApp`/`dist` по имени.
- Не пропускать поддерево по `Directory.LastWriteTime` (Windows).
- Сомнение → cache miss и DTB. Явный force-reload. Индекс не коммитить.
- Правило: лучше медленный DTB, чем тихий рассинхрон символов.

## Сессия vs диск

| Слой | Живёт | Что даёт |
|------|-------|----------|
| RAM `MSBuildWorkspace` | PID процесса | повторный `load_workspace` без `OpenSolutionAsync` |
| Watcher + `WithDocumentText` | тот же PID | saved `.cs` без reopen |
| Epoch 3 snapshot + индекс | между процессами | пропуск DTB после Reload MCP, если дерево не изменилось |

## Правила для чата реализации

1. Прочитать этот README и **только назначенную** эпоху.
2. Не реализовывать следующие эпохи.
3. Base truth — текущий код (`SolutionManager`, `WorkspaceTools`, фильтр путей).
4. После кода: тесты, version bump по правилу репозитория, `ARCHITECTURE.md`,
   `[Description]` / README «Agent tools by version» если меняется поверхность tools.
5. Остановиться и написать handoff: что сделано, что осталось следующей эпохе.

## Указатели в shipped-доках

- `docs/ARCHITECTURE.md` — current-state; этот каталог — план.
- Корневой `README.md` — одна строка на этот каталог, не дублировать эпохи.
