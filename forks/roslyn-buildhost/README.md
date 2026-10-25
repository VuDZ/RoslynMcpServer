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
  `ProjectBuildManager` стал `partial`, его конструктор вызывает `AddReferencesFirstLoggers`, `ProjectFile` вызывает
  `GetProjectInstancesAsync`. Включается переменной окружения `ROSLYN_BUILDHOST_REFERENCES_FIRST=1`; без неё
  логгеры не подключаются и запрос идёт прежним путём. Запрос сборки проекта сначала загружает и собирает ещё не
  собранные ссылки C#/VB (самые глубокие первыми); первый запрос клиента к такой ссылке получает готовый результат с
  её исключением, если оно было. Лог каждой сборки при выдаче, в порядке клиента, пишется так, как его записал бы
  upstream: в этом режиме host записывает ошибки и предупреждения всех evaluation (у `ProjectCollection` логгер
  только критических событий) и дерево запросов каждой сборки к конфигурациям и вставляет предупреждения
  evaluation и содержимое первого запуска целей туда, где upstream впервые до них дошёл бы
  ([этап 1](../../docs/workspace-load-performance/research-buildhost-fork/evidence/task-02-stage-1-references-first.md)).
- [task-02](../../docs/workspace-load-performance/research-buildhost-fork/task-02-buildhost-optimization-spike-hi.md),
  часть B: evaluation наперёд. Новый файл `BuildHost/Build/ProjectBuildManager_EvaluateAhead.cs`; конструктор
  `ProjectBuildManager` вызывает `InitializeEvaluateAhead`, `LoadProjectAsync` — `WaitForEvaluationAheadAsync` и
  `OnEvaluatedOnRequest`, `LoadProject` из присланного содержимого — `UnloadEvaluationAhead`, `Dispose` —
  `StopEvaluateAhead`. Включается переменной `ROSLYN_BUILDHOST_EVALUATE_AHEAD=<число рабочих потоков>`, независимо
  от A; без неё рабочие потоки не создаются и загрузка идёт прежним путём. Рабочие потоки оценивают в коллекции
  host и с его настройками ссылки уже оценённых проектов и записи решения; берут первый не начатый проект в
  прямом порядке DFS от записей решения по уже известным ссылкам. Запрос к проекту, который оценивает рабочий,
  ждёт эту evaluation; к ещё не начатому — оценивает его сам. Неудачная evaluation наперёд не сохраняется: запрос
  клиента повторяет её и получает ту же ошибку. Какое решение грузится, host узнаёт от своего клиента: глобальное
  свойство `RoslynMcpSolutionPath` с путём к открытому `.sln`, `.slnx` или `.slnf`. Host забирает это свойство из
  глобальных свойств до создания `ProjectCollection` (и когда B выключена), так что evaluation его не видит.
  Решение читается, как его читает клиент, фильтр — через его JSON. Записи берутся, если первый запрос клиента —
  первая запись, которую клиент отправит этому host (C#/VB, файл есть, по правилам
  `BuildHostProcessManager.GetKindForProject` — .NET host); без свойства — только ссылки. Наперёд оцениваются
  только такие проекты. `ROSLYN_BUILDHOST_EVALUATE_AHEAD_SOLUTION`: `all` (по умолчанию),
  число записей после последней запрошенной или `off`.

Сборка:

```text
dotnet build forks/roslyn-buildhost/build/BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj -c Release
```

Обновление Roslyn: заново скопировать перечисленные в `UPSTREAM.md` пути с ревизии нового пакета,
перенести поверх них изменения форка и повторить сравнение task-00.
