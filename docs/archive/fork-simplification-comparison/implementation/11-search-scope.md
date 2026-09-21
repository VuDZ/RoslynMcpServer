# 11 — `search_code` multi-root scope

Pri **P2**. SemVer: **patch**. **После п. 6** (те же корни).
Разбор: [../README.md](../README.md#53-search_code-scope-и-ripgrep).

## Цель

Без `directoryPath` искать в каталоге загруженного файла **и** во внешних
project directories. Loose `.cs` у корня `.sln` не терять.

## Файлы

- `Tools/UtilityTools.cs` — `ResolveSearchRootDirectory` / список roots
- helper корней из п. 6 (не копировать `SearchScopeResolver`)
- тесты scope (новые)

Ripgrep **не** в этом пункте (P3, отдельный follow-up).

## Правка

`roots = ComputeWatchRoots`-эквивалент: sln/csproj dir ∪ project dirs, без
вложенных дублей. Managed line-scan по каждому root, тот же timeout/`maxResults`.
Явный `directoryPath` — как сейчас, один корень.

## Тесты

- workspace `.sln` + project вне дерева → оба корня
- файл у корня `.sln` всё ещё находится
- Unix: корни абсолютные (не `home/foo` без `/`)

## Acceptance

`search_code` после `load_workspace` на multi-root sln находит строки во внешнем
проекте без ручного `directoryPath`.

## Не копировать

`SearchScopeResolver` LCA-merge; `RipgrepRunner` как есть (`--stats` на stderr,
полный stdout в память).
