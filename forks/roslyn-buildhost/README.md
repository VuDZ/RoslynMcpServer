# Roslyn BuildHost fork

Контракт: [BuildHost-исследование](../../docs/workspace-load-performance/research-buildhost-fork/README.md),
[task-00](../../docs/workspace-load-performance/research-buildhost-fork/task-00-buildhost-fork-baseline-hi.md).
Источник и хеши: [UPSTREAM.md](UPSTREAM.md).
Документы исследования и отчёты по ссылкам ниже лежат в Git-ветке `research/workspace-load-performance`; в `main` их нет.

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
- Сервер использует форк как свой `BuildHost-netcore`
  ([task-03](../../docs/workspace-load-performance/research-buildhost-fork/task-03-buildhost-fork-server-integration-hi.md)):
  `build/BuildHostFork.targets`, который импортирует `RoslynMcpServer.csproj`, собирает форк вместе с сервером и
  подменяет им элементы `Content` пакета, так что build, publish и pack получают одни и те же файлы, вместе с
  `License.txt` и `THIRD-PARTY-NOTICES.txt` Roslyn. Сборка падает, если версия пакета
  `Microsoft.CodeAnalysis.Workspaces.MSBuild` не 5.9.0. A, B и C сервер включает глобальными свойствами workspace
  `RoslynMcpReferencesFirst`, `RoslynMcpEvaluateAhead` и `RoslynMcpSharedSdkCache`: host забирает их до evaluation, как `RoslynMcpSolutionPath`,
  и они побеждают переменные окружения форка. Эксперименты подключают форк явно: `spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1`
  сравнивает оригинал и форк на одном решении, их сторона оригинала — host из пакета.

Изменения форка поверх upstream:

- [task-01](../../docs/workspace-load-performance/research-buildhost-fork/task-01-buildhost-graph-and-timings-hi.md):
  трассировка таймингов. Новый файл `BuildHost/Tracing/BuildHostTrace.cs` и spans в `Program`, `RpcServer`,
  `AbstractBuildHost`, `ProjectBuildManager`, `ProjectFile`. Включается переменной окружения
  `ROSLYN_BUILDHOST_TRACE_DIRECTORY` (host пишет `buildhost-<pid>.jsonl` в этот каталог); без неё span не
  создаются и ничего не пишется, `RpcServer` только читает часы на каждый запрос. Ошибка создания или записи файла
  выключает трассировку до конца процесса и один раз пишется в stderr; ответ и работа host от неё не меняются.
  Протокол RPC не меняется. Собирается и трассируется только netcore-host; `BuildHost-net472` в экспериментах —
  из пакета.
- [task-04](../../docs/workspace-load-performance/research-buildhost-fork/task-04-buildhost-projectfileinfo-extraction-hi.md),
  шаг 0: под-span в `ProjectInstanceReader` внутри `extract`: аргументы компилятора и их fallback, свойства,
  документы с метаданными, additional/editorconfig, остальные элементы, `GetAllGlobs` и создание результата со
  ссылками на проекты. В span — счётчики готовых элементов, для globs ещё patterns и число импортов текущего
  `Project`; TFM результата — у родительского `extract`. Расчёт полей и его порядок сохранены. Включается той же
  `ROSLYN_BUILDHOST_TRACE_DIRECTORY`; без трассы счётчики не вычисляются. `trace-report` и `trace-summary` стенда
  показывают части отдельными категориями собственного времени; родительский `extract` остаётся остатком.
- [task-02](../../docs/workspace-load-performance/research-buildhost-fork/task-02-buildhost-optimization-spike-hi.md),
  часть A: ссылки собираются раньше проекта. Новый файл `BuildHost/Build/ProjectBuildManager_ReferencesFirst.cs`,
  `ProjectBuildManager` стал `partial`, его конструктор вызывает `InitializeReferencesFirst` и
  `AddReferencesFirstLoggers`, `ProjectFile` вызывает `GetProjectInstancesAsync`. Включается глобальным свойством
  `RoslynMcpReferencesFirst=1` (так включает сервер; host забирает свойство до evaluation) или, без него, переменной
  окружения `ROSLYN_BUILDHOST_REFERENCES_FIRST=1`; без них
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
  `StopEvaluateAhead`. Включается глобальным свойством `RoslynMcpEvaluateAhead=<число рабочих потоков>` или, без
  него, переменной `ROSLYN_BUILDHOST_EVALUATE_AHEAD=<число рабочих потоков>`, независимо
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
- [task-04](../../docs/workspace-load-performance/research-buildhost-fork/task-04-buildhost-projectfileinfo-extraction-hi.md),
  кандидат C: общий кеш SDK resolution на проект. Новый файл `BuildHost/Build/ProjectBuildManager_SharedSdkCache.cs`.
  Где он подключён:
  - конструктор `ProjectBuildManager` вызывает `InitializeSharedSdkCache`;
  - `LoadProjectCore` создаёт проект через `CreateProject`;
  - смена `TargetFramework` и возврат (в `BuildProjectInstancesAsync` и `RestoreGlobalTargetFramework` части A)
    идут через `ReevaluateIfNecessary(project)`;
  - `ProjectFile.GetProjectFileInfosAsync` после сборок вызывает `EvaluateForGlobs`.

  Включается глобальным свойством `RoslynMcpSharedSdkCache=1` или, без него, переменной окружения
  `ROSLYN_BUILDHOST_SHARED_SDK_CACHE=1`, независимо от A и B. Без них проект создаётся прежним конструктором,
  evaluation идут без контекста, `EvaluateForGlobs` ничего не делает. Сервер передаёт свойство всегда, вместе с
  переключателями A и B: 1 по умолчанию, 0 при выключенных оптимизациях.

  Как работает:
  - все evaluation одного проекта получают один `EvaluationContext` с `SharingPolicy.SharedSDKCache`: корневая,
    при смене TFM, при возврате и поздняя evaluation первого `GetAllGlobs`. Общие у них только сервис SDK
    resolver, его результаты и состояние resolver (workload-манифесты). Кеш файловой системы у каждой evaluation
    свой;
  - позднюю evaluation `GetAllGlobs` host делает сразу после сборок проекта, до чтения `ProjectFileInfo`, —
    там, где её сделал бы первый `GetAllGlobs`;
  - сервис кеширует результат только по имени SDK. Поэтому после каждой evaluation в общем контексте host
    проверяет, что каждый SDK-import получил результат, разрешённый для того же имени, версии и минимальной
    версии. Если нет, проект оценивается заново без контекста и больше кеш не делит. Отброшенная evaluation до
    клиента не доходит: сборка видит только ту evaluation, из которой создана.

  Трасса: `evaluate-globs` (поздняя evaluation в общем контексте) и `evaluate-isolated` (повтор без контекста).

Сборка:

```text
dotnet build forks/roslyn-buildhost/build/BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj -c Release
```

Обновление Roslyn: заново скопировать перечисленные в `UPSTREAM.md` пути с ревизии нового пакета,
перенести поверх них изменения форка и повторить сравнение task-00.
