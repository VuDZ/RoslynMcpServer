# D/E0/task-04 — Spike: параллельная загрузка несколькими MSBuildProjectLoader

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Claude Opus 5.5.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: средний — ложная эквивалентность слитого решения или замер, искажённый общим процессом и прогревом, дадут неверный go.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_производительности`, `проектирование_проверок`.
- Статус: implemented, 2026-10-10; матрица замеров завершена, вердикт no-go; формальная приёмка ожидается из-за несовместимых SDK-профилей существующих тестов в едином main CI.
- Depends on: —.
- Модель, реализовавшая задачу: Codex (GPT-6).
- Модели, проводившие ревью: Codex (GPT-6), проверка исполнителем; Claude Opus 5.5, ревью кода spike и отчёта 2026-10-10 — параллельность, слияние, замер и применение порога подтверждены, вердикт no-go принят.
- Количество раундов ревью: 2 (проверка исполнителем; ревью Claude Opus 5.5).
- Количество исправлений после ревью: 2 — около 64 MB снимков, предварительных прогонов и TRX вынесено из `docs` в неверсионируемый `artifacts/`; строка в `docs/README.md` обновлена.
- Выполненный scope: standalone spike, Compile/None исключение, область SourceStructure, 44 основных прогона и evidence; production load и E1 не менялись. Принятый scope code/tests: ещё не принят, pending единого зелёного main CI.
- Отчёт о реализации, ревью и validation: [task-04-parallel-loader-spike](evidence/task-04-parallel-loader-spike.md).

## Цель

Измерить, во сколько раз сокращается время MSBuild open, если решение грузить
несколькими публичными `MSBuildProjectLoader` одновременно, и даёт ли слитый
результат то же решение, что обычная загрузка. Итог — go или no-go для задачи E1
с этой оптимизацией. Production-код сервера не меняется.

## Почему это проверяется

[Замер прокси](evidence/task-00-current-version.md): open — 94–98 % `load_workspace`,
BuildHost занят примерно одним ядром, ограничение сервера четырьмя потоками
время не меняет. Декомпиляция `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0:

- `MSBuildProjectLoader.LoadSolutionInfoAsync` и `LoadProjectInfoAsync` вызывают
  `LoadInfoAsync`, который на каждый вызов создаёт свой `BuildHostProcessManager`,
  то есть свой процесс BuildHost, и освобождает его после загрузки;
- `MSBuildProjectLoader.Worker.LoadAsync` обходит запрошенные проекты циклом,
  по одному `await` на проект;
- `SolutionFileReader.ReadSolutionFileAsync` понимает solution filter (`.slnf`)
  через `SolutionFilterReader`.

Значит, N одновременных `LoadSolutionInfoAsync` по N временным `.slnf` дают
N BuildHost параллельно без изменения Roslyn. Неизвестны выигрыш, повторная
загрузка общих зависимостей, память и эквивалентность результата.

## Входы и границы

[Тема](../README.md), [карта эпохи](README.md), [замер прокси](evidence/task-00-current-version.md).
Перед первой C# правкой — [AGENTS.md](../../../AGENTS.md) и полный [code style](../../code-style.md).

Решения для замера — вне репозитория, в каталоге, который исполнитель передаёт
параметром:

- microsoft/PowerToys `7b6789c576b304c7e1eda8ce571cc1b0afd280ab`, `PowerToys.slnx`, Debug|x64;
- files-community/Files `1994d9a4269f5144c70bc67d6a48de4a20616912`, `Files.slnx`, Debug|x64.

Restore — `MSBuild.exe <sln> -t:restore -p:Platform=x64 -p:Configuration=Debug` из
Visual Studio; сборка решений не нужна. Команды и пути без имени машины и профиля
записываются в отчёт.

Границы:

- Только публичный API Roslyn. Без reflection во внутренние типы, без fork и без
  патча BuildHost.
- Код spike — консольный проект `spikes/ParallelLoad/ParallelLoad.csproj`, `net10.0`, те же
  версии `Microsoft.CodeAnalysis.*` и `Microsoft.Build.Locator`, что в `RoslynMcpServer.csproj`.
  В `RoslynMcpServer.sln` не добавляется, в CI не входит, не публикуется.
- Корневой `RoslynMcpServer.csproj` глобирует все `.cs` под корнем, поэтому в него
  добавляются `Compile Remove="spikes\**"` и `None Remove="spikes\**"` рядом
  с исключением `samples\**`. Других правок production нет; version bump и
  `CHANGELOG.md` не нужны.
- Global properties повторяют
  [`MsBuildWorkspaceProperties`](../../../Services/Workspace/MsBuildWorkspaceProperties.cs)
  для Debug|x64. Код сервера не переиспользуется ссылкой на сборку: значения
  переносятся, и в отчёте указано откуда.
- MSBuild регистрируется через `MSBuildLocator` так же, как в
  [`MsBuildBootstrapper`](../../../Diagnostics/MsBuildBootstrapper.cs): SDK по `global.json`
  целевого решения, при его отсутствии — как делает сервер.

## Что сделать

### Режимы

Один запуск spike — один замер в новом процессе. Режим задаётся аргументами:

1. `baseline` — один `LoadSolutionInfoAsync(<sln>)` без логгера.
2. `baseline-binlog` — то же с `BinaryLogger` как в
   [`AnalyzerProvenanceCaptureService`](../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs)
   (`Verbosity = Normal`, `ProjectImports=None`). Разница с `baseline` — стоимость
   записи binlog внутри open; replay не входит.
3. `parallel --workers N --strategy S` — проекты solution делятся на N групп,
   на каждую пишется временный `.slnf`, N вызовов `LoadSolutionInfoAsync` идут
   одновременно, по отдельному `MSBuildProjectLoader` на группу, результаты сливаются.

Стратегии групп:

- `round-robin` — по порядку проектов в solution;
- `cluster` — связные компоненты по `ProjectReference` из XML `.csproj`, без evaluation,
  разложенные по группам жадно по числу проектов. Ссылки из `Directory.Build.*`
  этим способом не видны; отчёт это оговаривает.

`.slnf` пишется во временный каталог с абсолютным путём к solution. Если Roslyn
такой файл не принимает, `.slnf` кладётся рядом с solution и удаляется после
замера; выбранный вариант фиксируется.

### Слияние

- Ключ проекта — `FilePath` и `Name` (`Name` различает TFM-варианты).
- Для каждого ключа берётся один `ProjectInfo`: из группы, где проект был
  запрошен, иначе первый найденный. Его `ProjectId` и `DocumentId` остаются.
- `ProjectReference` всех оставленных проектов переписываются на канонический
  `ProjectId` по ключу целевого проекта; aliases и `EmbedInteropTypes` сохраняются.
- Общий `ProjectMap` между параллельными загрузчиками не используется: его
  потокобезопасность не установлена.
- Итог — `SolutionInfo`, добавленный в `AdhocWorkspace` с
  `MSBuildMefHostServices.DefaultServices`; на нём выполняется сравнение.

### Эквивалентность

Сравнение слитого решения с `baseline` по каждому ключу проекта:

- множество ключей проектов, `Language`, `AssemblyName`, `OutputFilePath`,
  `OutputRefFilePath`, `CompilationOutputInfo.AssemblyPath`, `DefaultNamespace`;
- пути documents, additional documents, analyzer config documents — как множества
  и отдельно как упорядоченные списки;
- пути и aliases metadata references, `FullPath` analyzer references;
- project references как ключи целей с aliases;
- `CompilationOptions`: `OutputKind`, `Platform`, `NullableContextOptions`,
  `AllowUnsafe`, `OptimizationLevel`, `CheckOverflow`, `SpecificDiagnosticOptions`;
  `ParseOptions`: `LanguageVersion`, `PreprocessorSymbolNames`, `DocumentationMode`, `Features`;
- workspace diagnostics как множество (kind, message) — расхождения считаются отдельно.

Сначала сравниваются два `baseline` между собой: всё, что меняется уже там,
отмечается как естественная вариативность и не засчитывается параллельному режиму.
Любое другое расхождение — unexplained, пока исполнитель не найдёт причину;
отчёт выдаёт первые 20 расхождений по категориям.

### Замер

На каждую комбинацию — прогрев и n = 3 замера, каждый в новом процессе spike.
Комбинации на PowerToys: `baseline`, `baseline-binlog`, `parallel` с N ∈ {2, 4, 8}
и обеими стратегиями. На Files: `baseline`, `parallel` с N ∈ {2, 4} и `round-robin`.

В каждом замере:

- wall time общий, каждой группы и слияния — через `TimeProvider` или `Stopwatch`;
- число процессов BuildHost, пиковый суммарный working set процессов BuildHost и
  CPU сервера плюс потомков (опрос не реже раза в секунду; как в
  [`Measure-WorkspaceLoad.ps1`](../../../scripts/Measure-WorkspaceLoad.ps1)) и busy cores;
- сумма проектов по группам, число уникальных ключей и повторно загруженных;
- число workspace diagnostics.

Время машины, CPU, SDK и антивирус пишутся в отчёт. Kaspersky не выключается.

## Результат

`evidence/task-04-parallel-loader-spike.md`:

- таблица медиан по комбинациям: wall, ускорение к `baseline`, повторные загрузки,
  пиковая память BuildHost, busy cores;
- стоимость binlog: `baseline-binlog` − `baseline`;
- итог эквивалентности по каждой комбинации: identical / natural variance only /
  unexplained (с примерами);
- вердикт go или no-go и его основание.

Порог go задан заранее: на PowerToys есть комбинация с N ≤ 4, у которой медиана
wall не больше 50 % `baseline`, unexplained расхождений нет, а пиковая память
BuildHost не больше 4 GB. Иначе no-go с указанием, что помешало. На Files
вердикт не выносится: одиннадцать проектов с одним крупным проверяют, что
режим не делает хуже.

Если go — в отчёте раздел для будущей задачи E1, без её реализации:

- `MSBuildWorkspace` `sealed` и не принимает `SolutionInfo`; нужен свой
  наследник `Workspace` и запись текста документов на диск с сохранением
  кодировки и BOM (pitfalls 24–25);
- provenance capture: binlog каждой группы и replay всех файлов;
- отмена, ошибки одной группы, ограничение N памятью;
- disk watcher и input map строятся по слитому решению.

## Проверки

- Spike собирается и запускается через MCP `run_dotnet_build` / `run_dotnet_run`
  на `spikes/ParallelLoad/ParallelLoad.csproj`. Если MCP не может выбрать проект вне
  загруженного solution, допускается shell `dotnet run` только для этого проекта;
  это записывается в отчёт.
- После правки корневого csproj: Release build решения, весь SourceStructure и
  полный `Category!=AnalyzerLifecycle` в `RoslynMcpServer.Tests`. AnalyzerLifecycle
  не нужен: путь загрузки сервера не меняется.
- Нулевой замер, timeout или упавшая группа — это результат с причиной, а не
  пропуск строки таблицы.
