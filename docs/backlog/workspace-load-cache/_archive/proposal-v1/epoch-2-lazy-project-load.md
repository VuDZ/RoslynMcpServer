# Epoch 2 — Ленивая загрузка графа проектов

Status: **planned**. Делать только после shipped Epoch 1 (или явного решения
пропустить Epoch 1, зафиксированного в handoff).

## Mission

Дать агенту режим `load_workspace`, который **не открывает все 100+ проектов**,
а только нужный конус: один `.csproj`, closure файла, или явно перечисленные
проекты. Остальное — по требованию. Аналоги: OmniSharp `LoadProjectsOnDemand`,
csharp-ls. Диск-кеш (Epoch 3) всё ещё не делать.

## Prerequisites

- [README.md](README.md), [Epoch 1](epoch-1-msbuild-fast-open.md) как base truth.
- `SolutionManager`: один workspace, `OpenSolutionAsync` vs `OpenProjectAsync`.
- Навигация: `find_usages`, `find_implementations`, `find_symbol_definition`
  ходят по текущему `Solution`. Неоткрытый проект = символа «нет».

## Baseline / problem

Даже с metadata-ref Epoch 1 solution-wide `OpenSolutionAsync` оценивает много
проектов. Типичный запрос агента касается одного сервиса в монорепе.

## Decisions

- Новый параметр режима загрузки (имя в стиле tools), например `graphScope`:
  - `solution` (default) — как сейчас, полный `.sln`/`.slnx`;
  - `project` — открыть только указанный `.csproj` (уже есть путь
    `OpenProjectAsync`, если `workspacePath` сам `.csproj`; для `.sln` нужен
    дополнительный `projectName` / путь проекта);
  - опционально позже: `file` — проект, владеющий файлом, + project refs.
    Если в этой эпохе не влезает — не делать, описать в handoff.
- Default **`solution`**: без аргумента поведение не меняется.
- Ответ `load_workspace` / health **обязан** сказать, что граф частичный:
  число открытых проектов vs записей в sln, список имён, что `find_usages`
  не ищет в неоткрытых проектах. Иначе агент врёт пользователю.
- `run_dotnet_build` / `run_dotnet_test` **не** зависят от полноты workspace:
  они запускают `dotnet` по sln/csproj на диске. Не сужать CLI из-за lazy graph.
- Expand: либо повторный `load_workspace` с другим scope / полный solution,
  либо отдельный узкий инструмент не плодить в этой эпохе без нужды
  (предпочтительно повторный load / `reset_workspace` + load).
- RAM-кеш: частичный и полный граф — **разные** ключи (иначе cache hit вернёт
  неполный snapshot). Включить scope в сравнение рядом с path+props.
- Graph-stale watcher: без изменений по смыслу; stale частичного графа не
  догружает остальные проекты сам.

## Scope

- Параметры `load_workspace` + Description + health.
- `MsBuildWorkspaceProperties.IsSameLoadCache` (или соседний ключ) + тесты.
- Документация агента: когда звать полный sln (rename across solutions,
  implementations по всей монорепе) vs один csproj.
- Version: **minor**, если новый явно аддитивный режим; иначе patch при
  строго opt-in без смены default.

## Non-goals

- Диск-кеш Epoch 3.
- Автодогрузка проекта при `find_document` по пути вне графа — соблазнительно,
  но легко сделать «тихую» магию и гонки с `_workspaceLock`. Если делать —
  отдельное решение в handoff, не в первом PR эпохи.
- Фоновый open остальных проектов.
- Изменение `search_code` (диск, не Roslyn graph).

## Risks

- Ложный «symbol not found» на частичном графе — главная UX-дыра. Mitigation:
  явный баннер в каждом semantic tool или хотя бы в load health +
  `GetProjectGraphStaleHint`-подобный hint «graph is partial».
- Агент загрузил один csproj, правит интерфейс в другом — usages пустые.
- Смешение Epoch 1 metadata-ref и lazy: на одном проекте metadata-ref почти
  бесполезен; на closure с refs — полезен. Комбинация допустима.

## Verification

- Load `.csproj` этого репозитория vs sln: число Projects, find тип из
  Tests при загруженном только Worker — ожидаемо miss + hint.
- Load полного sln — как сейчас.
- Build/test MCP tools на sln при загруженном одном проекте — зелёные, если
  код на диске собирается.

## Exit / handoff

- Default solution-wide сохранён.
- Partial graph виден агенту.
- Ключ RAM-кеша различает scope.
- Следующая эпоха может гидратить Adhoc/MSBuild только для открытого конуса.
