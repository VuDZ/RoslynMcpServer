# Epoch 1 — Ускорение холодного MSBuildWorkspace без своего формата

Status: **planned**. Не начинать Epoch 2–4 в том же чате.

## Mission

Сократить стоимость **первого** `OpenSolutionAsync` / `OpenProjectAsync` в уже
существующем `MSBuildWorkspace`, не вводя диск-кеш и не меняя модель «один
workspace на процесс». Это не переживает рестарт MCP; это ускоряет холодный open
внутри процесса и готовит почву для Epoch 2–3.

## Prerequisites

- Прочитать [README.md](README.md) этого каталога.
- Base truth: [`Services/SolutionManager.cs`](../../Services/SolutionManager.cs)
  `LoadCoreAsync` (создание workspace около `MSBuildWorkspace.Create`, затем
  `OpenSolutionAsync` / `OpenProjectAsync`).
- [`Tools/WorkspaceTools.cs`](../../Tools/WorkspaceTools.cs) `LoadWorkspace`.
- Свойство Roslyn: `MSBuildWorkspace.LoadMetadataForReferencedProjects`
  (документация / gist Dustin Campbell: если output DLL зависимого проекта
  существует, подставить metadata reference вместо recursive design-time build).

## Baseline / problem

Сейчас после `Create` флаг **не выставляется** — открывается полный граф проектов.
На 100+ проектах DTB каждого — доминирующее время. Сессионный RAM-кеш уже
пропускает повторный open; эта эпоха про **cache miss / новый PID**.

## Decisions

- `LoadMetadataForReferencedProjects` — **opt-in** параметр `load_workspace`
  (имя согласовать с существующим стилем: camelCase как `shadowCopyInSolutionAnalyzers`).
  Default **`false`**: поведение 1.3.x без флага не меняется (patch, не minor).
- Если DLL нет или не читается — не падать: оставить project reference / полный
  load этого ребра (поведение Roslyn). Залогировать skip/fallback.
- Отдельный «fast load: выключить generators/analyzers на open» в этой эпохе
  **не делать**, если нельзя безопасно через публичный API без ломки
  `get_diagnostics_for_file` / overlay. Если появится дешёвый публичный рычаг —
  тоже opt-in и явный текст в Description про generated-код. Иначе отложить в
  handoff как non-goal.
- Не менять ключ RAM-кеша из-за этого флага без нужды: тот же path+props.
  Смена флага на уже загруженном workspace → либо игнор до `reset_workspace`,
  либо полный reopen; выбрать одно и описать в Description (предпочтительно:
  флаг действует только на cache miss / полный open).
- Не читать `.vs`. Не писать файлы кеша.

## Scope

- Выставить свойство на экземпляре `MSBuildWorkspace` сразу после `Create`,
  до `OpenSolutionAsync`.
- Параметр MCP + `[Description]` + health/metadata в ответе `load_workspace`
  (`LoadMetadataForReferencedProjects: true/false`).
- Лог: сколько проектов открыто как project vs metadata (если API это даёт;
  иначе хотя бы флаг и число `Solution.Projects`).
- Тесты: по возможности без живого MSBuild на 100 проектов — unit на то, что
  свойство выставляется при флаге; если есть тестовый sln из двух проектов с
  `ProjectReference` и существующим output — интеграционный smoke.
- README «Agent tools by version» + Reference `load_workspace` при изменении
  публичного параметра.
- Version: **patch**, пока default false.

## Non-goals

- Диск-кеш, Merkle, AdhocWorkspace (Epoch 3).
- Lazy open подмножества проектов (Epoch 2).
- Смена default на `true`.
- `run_dotnet_build` / test tools: они не ходят в MSBuildWorkspace.
- Изменение `GitChangedFilesHelper`.

## Implementation delta (ориентир)

1. `SolutionManager.LoadCoreAsync` / API загрузки: прокинуть bool.
2. `WorkspaceTools.LoadWorkspace`: новый аргумент, default false.
3. `McpToolCatalog` не требует нового tool name.
4. Тесты рядом с `MsBuildWorkspacePropertiesTests` / workspace tests.

## Risks

- `find_usages` / implementations **в исходниках** зависимого проекта: если
  подставлен только DLL, исходников проекта в workspace нет. Для агента на
  app+lib это нормально, если смотрят API по metadata; для правок в referenced
  project — нужен полный load или `load_workspace` на его `.csproj`.
- Протухший `bin/`: символы не совпадут с исходниками. Description: флаг
  имеет смысл после хотя бы одной сборки; при сомнении — false или
  `reset_workspace`.
- Analyzer/generator projects как `OutputItemType=Analyzer`: metadata-ref на
  «обычный» ProjectReference не должен ломать overlay Epoch 1; не трогать
  shadow-copy логику.

## Verification

- Этот репозиторий (2 проекта): load с флагом true/false, `find_symbol_definition`
  на тип из test/app.
- По возможности крупное sln: замер времени open и working set в логе
  (`workspace_load_cached` vs полный open).
- MCP: `run_dotnet_build` / `run_dotnet_test` по правилам репозитория, не shell
  `dotnet build`.

## Exit / handoff

- Флаг работает, default false, Description честный.
- `ARCHITECTURE.md`: одно предложение в Workspace lifecycle (opt-in metadata
  refs), без диск-кеша.
- Handoff: измерить, нужен ли default true в minor; не начинать Epoch 2, пока
  не ясно, что metadata-ref не ломает overlay.
