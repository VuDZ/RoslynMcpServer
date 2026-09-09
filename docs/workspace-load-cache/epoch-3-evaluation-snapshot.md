# Epoch 3 — Evaluation snapshot и индекс файлов между процессами

Status: **planned**. Центр фичи. Делать после Epoch 1–2 (хотя бы после 1, если
2 сознательно отложен). Не делать Epoch 4 в том же чате.

## Mission

Пережить **рестарт MCP**: после закрытия агентов, sync монорепы (git или ast),
открытия Cursor/OpenCode не гонять N× DTB, если граф и дерево исходников те же.
Хранить локально: (1) снимок MSBuild evaluation, (2) индекс файлов/каталогов.
Не вызывать VCS. Не pickle Roslyn `Solution`.

## Prerequisites

- [README.md](README.md) — запреты и матрица.
- Base truth: `SolutionManager.LoadCoreAsync`, `WorkspaceDiskPathFilter`,
  in-memory analyzer overlay (`GetCurrentSolution`, никогда
  `TryApplyChanges` с overlay — см. `docs/analyzer-shadow-copy/`).
- Сессионный RAM-кеш оставить: если PID жив и ключ совпал — по-прежнему
  `workspace_load_cached`, диск-snapshot не читаем.

## Baseline / problem

RAM умирает с процессом. Watcher тоже. Наивный «хеш только csproj» пропускает
новый `.cs` под `**/*.cs`. Привязка к `git` ломается на монорепе **ast**.
Обход всего монорепо + `node_modules` уничтожает бюджет «доли секунды».

## Decisions

### Что хранить

**Evaluation (per project, после успешного DTB или эквивалента):**

- Configuration, Platform, TargetFramework, версия SDK / schema snapshot
- пути MetadataReference, project refs, analyzer **identity** (не temp shadow)
- glob-шаблоны Compile / AdditionalFiles / Razor, ParseOptions / CompilationOptions
  (то, без чего нельзя собрать `AdhocWorkspace` / `ProjectInfo`)
- schema version файла кеша

**Индекс (конус решения, не вся монорепа):**

- файл: относительный путь, `LastWriteTimeUtc`, `Length`, content hash
- каталог: Merkle `H(sorted (name, kind, childHash))` только по **включённым**
  детям (после prune и allowlist)

**Не хранить:** тексты `.cs`, Compilation, SyntaxTree, HEAD, git/ast, shadow-пути
анализаторов, содержимое `obj/` generated, `node_modules`, `.js` из wwwroot
(пока файл не в document set Roslyn).

### Ключ и место

- Ключ: абсолютный путь загруженного `.sln`/`.csproj` + Configuration + Platform + TFM.
- Каталог: `%LOCALAPPDATA%/RoslynMcpServer/eval-cache/` **или** gitignored папка
  рядом с решением (не корень всей монорепы). Выбрать одно в реализации и
  зафиксировать в ARCHITECTURE. Не коммитить. Schema version в файле; неизвестная
  версия = miss.
- Force: параметр `load_workspace` (игнорировать диск) и/или `reset_workspace`
  чистит RAM; отдельный «wipe disk cache» можно совместить с force.

### Инвалидация — без VCS

Не вызывать `git` / `ast`. Не копировать `GitChangedFilesHelper`.

Холодный open (RAM miss):

1. Обход **конуса**: директории проектов + walk-up props/`global.json`/`nuget.config`.
2. **Prune:** не рекурсировать каталог, если сегмент пути в ignore-list
   (`WorkspaceDiskPathFilter`). Расширить список минимум: `.vs`,
   `bower_components`, `jspm_packages`, `.next`, `.nuxt`, `.angular`, `coverage`
   (уже есть `bin`, `obj`, `.git`, `node_modules`, `TestResults`, `artifacts`).
   Watcher и кеш — **один** список.
3. **Allowlist файлов для индекса исходников:** `.cs`, `.razor`, `.cshtml` + файлы
   графа (`.csproj`, `.sln`/`.slnx`, `.props`, `.targets`, `global.json`,
   `nuget.config`, `project.assets.json`). Не вычёркивать `wwwroot` / `ClientApp`
   / `dist` целиком. `.js`/`.ts`/картинки не хешировать, кроме точечного
   AdditionalFiles из evaluation.
4. Stat: `(path, length, mtime)` vs индекс. Совпало на исходнике → взять hash
   из индекса. Файлы **графа всегда перехешировать**.
5. Merkle каталогов снизу вверх. **Не** отсекать поддерево по
   `Directory.LastWriteTime`.
6. Сравнение:
   - граф проекта (хеши csproj/props/assets/SDK) miss → DTB **этого** проекта;
   - граф hit, Merkle исходников hit → список документов из snapshot + тексты с диска;
   - граф hit, Merkle miss → DTB не нужен; членство с текущего обхода, фильтр
     закешированными glob; тексты с диска;
   - сомнение → miss.
7. После DTB / гидрации: записать evaluation + индекс. Overlay analyzers —
   после гидрации, как сейчас.

`npm install` при неизменном C# не меняет индекс (не заходили в `node_modules`).

### Сборка workspace на hit

Публичного «resume MSBuildWorkspace» нет. На hit графа собирать snapshot через
`AdhocWorkspace` / `ProjectInfo` из evaluation **или** открывать MSBuildWorkspace
только для stale-проектов и мержить. Не подменять overlay через `TryApplyChanges`.

Если гидрация из snapshot не сходится (нет DLL, битый JSON) — полный
`OpenSolutionAsync` для затронутого конуса, затем перезапись кеша.

### Параллелизм и I/O

- Bounded (`ProcessorCount` или лимит 4–8 на сетевом диске).
- Junction/symlink: не выходить из конуса решения.
- Windows: пути без учёта регистра в ключе индекса.
- Хеш: SHA256 из BCL, без новой зависимости, пока замер не потребует XXH3.

### Строгость зонда

Default: `.cs` доверяет size+mtime. Остаточный риск (копир сохранил дату).
Граф всегда hash. Опциональный strict (всегда hash исходников) — env или
параметр; не обязателен в первом PR, но место заложить.

## Scope

- Сервис индекса + чтения/записи snapshot (DI, не `new` тяжёлого I/O в tools).
- Встройка в `LoadCoreAsync` после проверки RAM-кеша.
- Расширение `WorkspaceDiskPathFilter` + тесты.
- Параметр force-reload на `load_workspace`.
- Лог: hit/miss per project, время stat/hash/DTB, prune skipped dirs.
- Тесты без монорепы: temp tree с `node_modules` (не должен хешироваться),
  новый `.cs` при том же csproj → Merkle miss без DTB-мока если возможно,
  смена csproj → miss графа, смена только mtime+size `.cs` → перехеш.
- `.gitignore` / docs: кеш не в VCS.
- `ARCHITECTURE.md` Workspace lifecycle — **current-state после шипа**.
- Version: **minor** (новое поведение load + параметры).

## Non-goals

- `IWorkspaceVcsProbe`, git, ast.
- Индекс символов (Epoch 4).
- Чтение VS `.vs`.
- Кеш CLI `dotnet build` (его нет и не будет в этой эпохе).
- Сканирование всего корня монорепы «на всякий случай».
- Сериализация generated `obj/` как замена `reset_workspace`.

## Risks

- Тихий stale после size+mtime collision на `.cs` — strict / miss при сомнении.
- Неполный `ProjectInfo` (анализаторы, nullable, langversion) → кривая семантика.
  Лучше miss, чем угадать options.
- Overlay + Adhoc: все чтения через `GetCurrentSolution()`.
- CrossTargeting: в ключе inner TFM; не кешировать outer.
- Частичный граф Epoch 2: snapshot только открытых проектов; не выдавать hit
  на полный sln.
- Память: индекс на диске, не держать все хеши исходников монорепы в RAM дольше
  open.

## Матрица (перенести в ARCHITECTURE при шипе)

| Событие | DTB | Документы |
|---------|-----|-----------|
| Правки `.cs`, зонд совпал | hit | чтение с диска |
| Sync: новые `.cs`, csproj тот же | hit графа | Merkle miss, обход |
| Корневой `Directory.Build.*` / assets / SDK | miss затронутых | после DTB |
| `npm install` | hit | без изменений |
| Другой worktree path | другой ключ | — |
| `obj/` generators | как сейчас, reset | не в индексе |

## Verification

- Юниты фильтра, Merkle, зонд, prune `node_modules`.
- Интеграция: два проекта, restart симулировать новым `SolutionManager` /
  dispose workspace, второй load — без повторного DTB если дерево то же
  (мок evaluation или замер времени / счётчик OpenSolution).
- MCP `run_dotnet_build` / тесты проекта.
- Ручная проверка: load → убить процесс в голове (dispose) → git-неважно
  тронуть `.cs` → load → символ виден; добавить файл → виден; тронуть
  csproj → DTB.

## Exit / handoff

- Холодный open с hit не вызывает полный `OpenSolutionAsync` на неизменном дереве.
- VCS в коде эпохи нет.
- `node_modules` не в индексе.
- Overlay не пишется на диск через `TryApplyChanges`.
- Handoff: путь кеша, schema version, что измерено на большом sln.
  Epoch 4 не обязателен.
