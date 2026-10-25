# Roslyn BuildHost fork

Контракт: [BuildHost-исследование](../../docs/workspace-load-performance/research-buildhost-fork/README.md),
[task-00](../../docs/workspace-load-performance/research-buildhost-fork/task-00-buildhost-fork-baseline-hi.md).
Источник и хеши: [UPSTREAM.md](UPSTREAM.md).

Форк процесса BuildHost, которым `MSBuildWorkspace` из Roslyn 5.9.0 загружает проекты.
Клиент (`Microsoft.CodeAnalysis.Workspaces.MSBuild.dll` в сервере) не форкается: он запускает тот
BuildHost, что лежит в папке `BuildHost-netcore` рядом с ним, и общается с ним JSON по каналу.
Поэтому форк подменяется копированием своей сборки в эту папку, пока протокол не меняется.

- `upstream/` — дословная копия исходников Roslyn на ревизии пакета; наши изменения — отдельными
  коммитами поверх неё.
- `build/` — проекты для обычного .NET SDK с именами upstream-проектов:
  `Contracts/Microsoft.CodeAnalysis.Workspaces.MSBuild.Contracts.csproj` и
  `BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj` (только netcore, net8.0,
  `rollForward: Major`, как в пакете). Имя проекта совпадает с именем сборки: иначе SDK пишет в
  `deps.json` лишнюю запись `Contracts.Reference`.
- Код следует стилю и `.editorconfig` Roslyn, а не `docs/code-style.md` этого репозитория: изменения
  должны уходить в upstream с минимальным диффом (см. `AGENTS.md`).
- Production сервера форк не использует. Эксперименты подключают его явно:
  `spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1` сравнивает оригинал и форк на одном решении.

Изменения форка поверх upstream:

- [task-01](../../docs/workspace-load-performance/research-buildhost-fork/task-01-buildhost-graph-and-timings-hi.md):
  трассировка таймингов. Новый файл `BuildHost/Tracing/BuildHostTrace.cs` и spans в `Program`, `RpcServer`,
  `AbstractBuildHost`, `ProjectBuildManager`, `ProjectFile`. Включается переменной окружения
  `ROSLYN_BUILDHOST_TRACE_DIRECTORY` (host пишет `buildhost-<pid>.jsonl` в этот каталог); без неё span не
  создаются и ничего не пишется, `RpcServer` только читает часы на каждый запрос. Ошибка создания или записи файла
  выключает трассировку до конца процесса и один раз пишется в stderr; ответ и работа host от неё не меняются.
  Протокол RPC не меняется. Собирается и трассируется только netcore-host; `BuildHost-net472` в экспериментах —
  из пакета.
- [task-02](../../docs/workspace-load-performance/research-buildhost-fork/task-02-buildhost-optimization-spike-hi.md),
  часть A: ссылки собираются раньше проекта. Новый файл `BuildHost/Build/ProjectBuildManager_ReferencesFirst.cs`,
  `ProjectBuildManager` стал `partial`, `ProjectFile` вызывает `GetProjectInstancesAsync`. Включается переменной
  окружения `ROSLYN_BUILDHOST_REFERENCES_FIRST=1`; без неё запрос идёт прежним путём. Запрос сборки проекта сначала
  загружает и собирает ещё не собранные ссылки C#/VB (самые глубокие первыми); первый запрос клиента к такой ссылке
  получает готовый результат с её DiagnosticLog или её исключением. Известное различие: предупреждения evaluation
  ссылки, которые upstream показывает через её повторную evaluation внутри сборки ссылающегося проекта, при A не
  появляются ([этап 1](../../docs/workspace-load-performance/research-buildhost-fork/evidence/task-02-stage-1-references-first.md)).

Сборка:

```text
dotnet build forks/roslyn-buildhost/build/BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj -c Release
```

Обновление Roslyn: заново скопировать перечисленные в `UPSTREAM.md` пути с ревизии нового пакета,
перенести поверх них изменения форка и повторить сравнение task-00.
