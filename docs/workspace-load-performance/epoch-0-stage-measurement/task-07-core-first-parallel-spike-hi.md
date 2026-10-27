# D/E0/task-07 — Spike: сначала общее ядро, затем группы поверх ProjectMap

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Claude Opus 5.5.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — ошибка в плане групп снова даст повторную загрузку одного проекта в двух BuildHost, гонку на общих obj и молча неполное решение; ложная эквивалентность приведёт к неверному go.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_производительности`, `проектирование_проверок`.
- Статус: implemented, 2026-10-10; шаг 0 и 40 прогонов завершены, вердикт no-go; независимое ревью ACCEPT; формальная приёмка code/tests pending единого зелёного main CI (1362 passed / 1 прежний SDK-specific failed).
- Закрытие, 2026-10-27: **завершена, no-go принят** (решение владельца). Ожидание main CI снято: checkpoint 2026-10-21 с кодом spike прошёл main CI 1388/0/0 без прежнего SDK-specific сбоя ([validation](evidence/commit-2026-10-21.md)); последний main — Windows 1467/1467 на `285c6a5`.
- Depends on: — (переиспользует код и компаратор [task-04](task-04-parallel-loader-spike-hi.md), профиль стоимости [task-05.1](task-05.1-profile-retry-hi.md)).
- Модель, реализовавшая задачу: Codex (GPT-6, сабагент с унаследованной моделью сессии).
- Модели, проводившие ревью: Codex (GPT-6), два независимых новых сабагента; round 1 MUST-FIX, round 2 ACCEPT.
- Количество раундов ревью: 2.
- Количество исправлений после ревью: 1 фикс-раунд, устранены 3 must-fix замечания; дополнительно исправлен порядок доступности Redact.
- Выполненный scope: standalone spike, шаг 0, 40 прогонов, модель/прогноз и evidence; production и E1 не менялись. Принятый scope code/tests: ещё не принят, pending обязательного main CI.
- Отчёт о реализации, ревью и validation: [результат no-go](evidence/task-07-core-first-parallel-spike.md), [независимое ревью и финальная validation](evidence/task-07-core-first-parallel-spike/validation.md).

## Цель

Проверить параллельную загрузку без повторов проектов: сначала одним BuildHost
грузится общее ядро, затем N групп параллельно, каждая со своей копией
`ProjectMap`, где ядро уже есть. Итог — go или no-go для задачи E1 и измеренные
параметры модели времени для решений, которых у нас нет. Production не меняется.

## Почему это проверяется заново
да
[Task-04](evidence/task-04-parallel-loader-spike.md): round-robin даёт 1,51× при N=4,
но один проект грузится в нескольких группах, и конкурирующие design-time builds
на общих obj теряют generated documents. Cluster без повторов корректен, но на
PowerToys даёт 1,03×: одна компонента содержит почти всё решение.
[Ревью task-05.1](evidence/task-05.1-profile-retry.md#ревью-2026-10-10-claude-opus-55)
закрыло ветку выводом «убрать повтор ядра публичным API нельзя». Этот вывод
неверен (поправка там же).

Декомпиляция `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0, 2026-10-10:

- `ProjectMap.Create(Solution)` публичный; для каждого проекта вызывает `Add(Project)`,
  который записывает id, путь, `OutputFilePath`/`OutputRefFilePath` и
  `project.State.ProjectInfo`.
- `MSBuildProjectLoader.Worker.LoadProjectInfosFromPathAsync` сначала проверяет
  `_projectMap.TryGetProjectInfosByProjectPath`; при попадании возвращает готовые
  `ProjectInfo` и не обращается к BuildHost.
- `LoadSolutionInfoAsync` передаёт в `LoadInfoAsync` `projectMap: null`; карту
  принимает только `LoadProjectInfoAsync(path, projectMap, …)`, по одному корню
  на вызов. Каждый вызов `LoadInfoAsync` создаёт свой `BuildHostProcessManager`,
  то есть новый холодный BuildHost.
- `Worker.ResolveReferencesAsync`: для ссылки с `ReferenceOutputAssembly=false`
  вызывается `LoadProjectInfosFromPathAsync` — проект загружается, ссылка не
  добавляется.
- `LoadMetadataForReferencedProjects` не помогает: `VerifyProjectOutputExistsAsync`
  всё равно выполняет `LoadProjectFileInfosAsync`, то есть полный design-time build.

Отсюда схема: ядро — один `LoadSolutionInfoAsync` по `.slnf` ядра; группа —
один `LoadProjectInfoAsync` по временному проекту-агрегатору, который ссылается
на корни группы с `ReferenceOutputAssembly=false`. Один BuildHost на группу,
ядро повторно не собирается.

## Входы и границы

[Тема](../README.md), [карта эпохи](README.md), [task-04](task-04-parallel-loader-spike-hi.md)
и [её отчёт](evidence/task-04-parallel-loader-spike.md), [профили task-05.1](evidence/task-05.1-profile-retry.md).
Перед первой C# правкой — [AGENTS.md](../../../AGENTS.md) и полный [code style](../../code-style.md).

Решения, commits, restore, конфигурация Debug|x64, global properties и регистрация
MSBuild — как в task-04. HEAD и restore проверяются заново перед серией.

Границы:

- Только публичный API Roslyn. Без reflection, fork и патча BuildHost.
- Код — новая стратегия в `spikes/ParallelLoad`; корневой csproj уже исключает
  `spikes\**`. В solution, CI и publish не входит. Version bump и `CHANGELOG.md` не нужны.
- Агрегаторы и `.slnf` пишутся только во временный каталог вне дерева решения и
  удаляются после замера. В дерево решения spike ничего не пишет; obj меняет
  только сам design-time build, как при обычной загрузке.
- Общий изменяемый `ProjectMap` между потоками не используется: каждая группа
  получает свою карту через `ProjectMap.Create(coreSolution)` от одного неизменяемого
  `Solution` ядра.

## Что сделать

### Шаг 0 — проверка агрегатора

До замеров на одном решении (Files достаточно) проверить, что агрегатор
загружает все корни группы и их замыкание одним BuildHost и ничего не пишет
в obj чужих проектов сверх обычного design-time build. Варианты по порядку:

1. SDK-style `.csproj` без restore: `ProjectReference` с `ReferenceOutputAssembly=false`,
   `SkipGetTargetFrameworkProperties=true`, `Private=false`; в каталоге агрегатора —
   копия `global.json` решения (если есть) и пустые `Directory.Build.props/targets`,
   чтобы walk-up не подхватил чужие файлы.
2. То же с restore только самого агрегатора (`RestoreRecursive=false`), если без
   assets BuildHost не отдаёт `ProjectReference`.
3. Последовательные `LoadProjectInfoAsync` по каждому корню группы с одной картой
   группы — контроль, а не цель: каждый вызов платит холодный BuildHost.

Выбранный вариант, отвергнутые и причины — в отчёт. Если ни 1, ни 2 не работают,
матрица снимается с вариантом 3, и это прямо указано в вердикте.

Отдельно проверить по декомпиляции и на деле, что возвращает `LoadProjectInfoAsync`
при карте с ядром: только загруженные в этом вызове проекты или и проекты из карты.
От этого зависит слияние.

### План групп

Источник графа задаётся аргументом `--graph`:

- `xml` — `ProjectReference` из XML `.csproj`, как у cluster в task-04;
- `snapshot` — project references из baseline-снимка этого же решения (формат
  task-04). Это точный граф прошлой загрузки; он моделирует план, который в
  production дали бы host (C) или снимок (B).

По графу:

1. Корни — проекты, на которые никто не ссылается; native и незагружаемые проекты
   не участвуют.
2. Корни распределяются по N группам жадно по стоимости собственного замыкания
   (профиль task-05.1; при его отсутствии — по числу проектов).
3. Ядро — проекты, которые входят в замыкания корней двух и более групп. Оно
   замкнуто вниз по построению; отчёт проверяет это явно.
4. Проекты с общим каталогом проекта целиком попадают в одну группу либо в ядро
   (общий obj).

Отчёт выдаёт для каждого N: размер ядра в проектах и долю стоимости, размеры
и стоимость групп.

### Загрузка и слияние

1. Фаза 1: `LoadSolutionInfoAsync` по `.slnf` ядра → `AdhocWorkspace` с
   `MSBuildMefHostServices.DefaultServices` → `coreSolution`.
2. Фаза 2: N одновременных `LoadProjectInfoAsync(<агрегатор группы>, ProjectMap.Create(coreSolution))`,
   по отдельному `MSBuildProjectLoader` на группу.
3. Слияние: проекты ядра из фазы 1; проекты групп из фазы 2 без копий ядра и без
   агрегатора. `ProjectId` ядра общие для всех групп; ссылки на них не переписываются.
   Ссылки на сам агрегатор удаляются.
4. **Нарушение плана**: путь проекта вне ядра загружен более чем одной группой,
   либо группа загрузила проект ядра заново. Такой прогон помечается
   `plan-violation`, его результат не считается корректным, проекты и группы
   перечисляются в отчёте. Это отказ, не natural variance.

### Эквивалентность

Компаратор и поля task-04 без изменений. Сравнение с baseline той же серии;
два baseline сначала сравниваются между собой. Объяснённым остаётся только
регистр путей Windows, как в task-04.

Ожидаемый источник новых расхождений: корни групп грузятся как discovered, а не
requested, и `_discoveredProjectOptions` может отличаться от режима
`LoadSolutionInfoAsync`. Любое такое расхождение в diagnostics — unexplained,
пока не показано, что оно сводится к этому режиму, и не описано, как E1 его устранит.

### Замер

Прогрев и n = 3, каждый замер — новый процесс spike.

- PowerToys: `baseline`; `core-first` с N ∈ {2, 4, 8} и `--graph` ∈ {`xml`, `snapshot`}.
- Files: `baseline`; `core-first` с N ∈ {2, 4} и `--graph snapshot`.

В каждом замере, кроме метрик task-04 (wall, BuildHost, пик памяти, busy cores,
diagnostics): время фазы 1, время каждой группы фазы 2, время старта первого
BuildHost каждой фазы (до первого события progress), слияние, число
`plan-violation`.

### Модель для больших решений

По медианам: `T ≈ h₁ + core + h₂ + max(group) + merge`, где `h` — холодный BuildHost
фазы. Отчёт выдаёт измеренные параметры и прогноз для загрузки 120 с и 300 с при
доле ядра 5 %, 22 % и 50 %. Это прогноз с явной оговоркой, не результат.

## Результат

`evidence/task-07-core-first-parallel-spike.md`:

- выбранный вариант агрегатора и результат шага 0;
- таблица медиан: wall, ускорение к baseline, доля ядра, время фаз, пик памяти,
  busy cores, `plan-violation`, эквивалентность;
- модель и прогноз;
- вердикт go или no-go.

Порог go задан заранее. Владелец 2026-10-10: корректный прирост 1,5× лучше,
чем ничего, поэтому порог task-04 (50 % baseline) здесь снижен:

1. ни одного unexplained расхождения и ни одного `plan-violation` во всех
   измеренных прогонах обоих решений — без этого no-go при любой скорости;
2. на PowerToys есть комбинация с N ≤ 4, у которой медиана wall не больше 75 %
   baseline;
3. пиковая память BuildHost не больше 4 GB.

На Files вердикт не выносится; если `core-first` там медленнее baseline, отчёт
предлагает условие автоматического отказа от параллели (по доле ядра, числу
проектов или оценке выигрыша).

Если go — раздел для задачи E1, без её реализации: свой наследник `Workspace`
(как в task-04), источник графа (прошлая загрузка, снимок B, host C) и откат
на последовательную загрузку при `plan-violation`, provenance binlog каждой
фазы, отмена и ошибка одной группы, ограничение N памятью.

## Проверки

- Spike собирается и запускается через MCP `run_dotnet_build` / `run_dotnet_run`
  на `spikes/ParallelLoad/ParallelLoad.csproj`.
- Production не меняется; перед коммитом — Release build решения и полный
  `Category!=AnalyzerLifecycle`. AnalyzerLifecycle не нужен.
- Нулевой замер, timeout, упавшая группа или `plan-violation` — результат с
  причиной, а не пропуск строки таблицы.
- Тяжёлые артефакты (снимки, binlog, TRX) — в неверсионируемый
  `artifacts/parallel-load/task-07-evidence/`, в docs — сводки и хэши.
