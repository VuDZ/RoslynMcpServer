# D/E0/task-07 — Core-first parallel spike

Дата: **2026-10-10**.

Вердикт: **no-go**. Production, Version и CHANGELOG не менялись; scope — standalone spike и evidence.

## Шаг 0 и публичный API

Выбран вариант 1: SDK-агрегатор без restore, ReferenceOutputAssembly=false, SkipGetTargetFrameworkProperties=true, Private=false. global.json скопирован; пустые Directory.Build.props/targets останавливают walk-up; все временные файлы находятся вне clone и удаляются после нагрузки.

| Вариант Files N=2 | Status | Wall, с | Проекты | BuildHosts | Plan violations |
|---|---|---:|---:|---:|---:|
| no-restore | ok | 8.817 | 11 | 3 | 0 |
| restore | ok | 10.085 | 11 | 3 | 0 |
| sequential | ok | 9.295 | 11 | 5 | 0 |

Restore только агрегатора (RestoreRecursive=false) и последовательный контроль загружают то же решение. Вариант 2 не нужен: отсутствие assets не мешает ProjectReference; вариант 3 добавляет холодные BuildHost на каждый корень. Сравнение diagnostics остаётся строгим; результаты не используются для расширения естественной вариации.

Декомпиляция Microsoft.CodeAnalysis.Workspaces.MSBuild 5.9.0: ProjectMap.Create(Solution) сохраняет ProjectInfo; Worker.LoadProjectInfosFromPathAsync возвращает попадание карты до BuildHost. Worker.LoadAsync возвращает requested и собственный discovered dictionary, готовое ядро из карты в этот dictionary не добавляется. В Files ядро присутствует только в фазе 1, обе группы вернули 0 проектов ядра. Слияние сохраняет ProjectId ядра, aliases/EmbedInteropTypes, отдельные TFM и opaque native targets; агрегатор и ссылки на него удаляются.

Obj witness: по 212 файлов до/после, ordinary изменил 28 путей, агрегатор — 28; новых относительно ordinary путей: 0. Сняты SHA-256, size и mtime. Это свидетельство сохранённых изменений для Files, transient writes не наблюдаются. Полные inventory в artifacts.

## Протокол и медианы

Свежая проверка подтвердила чистые clones и commits task-04: PowerToys `7b6789c576b304c7e1eda8ce571cc1b0afd280ab`, Files `1994d9a4269f5144c70bc67d6a48de4a20616912`. Перед серией оба restore завершились exit 0: `<VS>/MSBuild/Current/Bin/amd64/MSBuild.exe <solution.slnx> -t:restore -p:Platform=x64 -p:Configuration=Debug -verbosity:minimal`; restore logs сохранены в artifacts. Машина: AMD Ryzen 7 9800X3D, 8 cores / 16 logical processors, RAM 100562268160 bytes; Kaspersky оставлен включённым. Установлены SDK 9.0.314, 10.0.201, 10.0.300 и 10.0.401; регистрация loader повторяет task-04, фиксируется в каждом raw JSON. Подробное environment.json находится в artifacts и включено в SHA inventory.

HEAD/restore проверены заново, Debug|x64 и global properties task-04. В обеих сериях loader зарегистрировал `<dotnet>/sdk/10.0.401`; restore выполнен Visual Studio MSBuild. Каждый прогон — новый процесс MCP run_dotnet_run; по комбинации прогрев 0 и n=3 (1/2/3). Cache ОС и антивирус сохранены. Plan/aggregator setup входят в wall; snapshot observation и завершение sampler — после wall. Graph snapshot берёт точные references baseline этой серии; XML — буквальные XML edges с baseline inventory загружаемых проектов. Ключи профиля task-05.1 нечувствительны к регистру, запросы сохраняют исходное написание путей solution.

| Решение / комбинация | Wall, с | × baseline | Planned/actual core count; planned cost | Фаза 1, с | Critical group, с | Merge, мс | Peak median/max, GiB | Busy cores | Violations | Эквивалентность |
|---|---:|---:|---|---:|---:|---:|---|---:|---:|---|
| powertoys / baseline | 41.350 | 1.000 | — | — | 41.113 | 205.749 | 1.058 / 1.059 | 1.424 | 0 | identical ({'ok': 3}) |
| powertoys / core-first-xml-2 | 31.641 | 1.307 | 29/29; 17.01% | 7.238 | 23.820 | 138.725 | 1.282 / 1.290 | 2.478 | 0 | unexplained ({'ok': 3}) |
| powertoys / core-first-xml-4 | 22.453 | 1.842 | 35/35; 21.11% | 7.593 | 14.296 | 106.797 | 1.669 / 1.690 | 3.623 | 0 | unexplained ({'ok': 3}) |
| powertoys / core-first-xml-8 | 25.579 | 1.617 | 48/48; 26.79% | 9.685 | 15.193 | 90.236 | 2.413 / 2.489 | 4.694 | 0 | unexplained ({'ok': 3}) |
| powertoys / core-first-snapshot-2 | 27.324 | — | 28/28; 16.79% | 6.312 | 20.467 | 111.091 | 1.286 / 1.286 | 2.435 | 6 | unexplained ({'plan-violation': 3}) |
| powertoys / core-first-snapshot-4 | 23.677 | — | 35/35; 22.61% | 8.154 | 15.016 | 107.354 | 1.690 / 1.718 | 3.542 | 12 | unexplained ({'plan-violation': 3}) |
| powertoys / core-first-snapshot-8 | 27.331 | — | 48/48; 26.72% | 11.088 | 15.680 | 107.825 | 2.487 / 2.639 | 4.910 | 12 | unexplained ({'plan-violation': 3}) |
| files / baseline | 10.411 | 1.000 | — | — | 10.323 | 55.627 | 0.188 / 0.195 | 1.441 | 0 | identical ({'ok': 3}) |
| files / core-first-snapshot-2 | 8.134 | 1.280 | 1/2; 14.55% | 1.869 | 5.894 | 20.227 | 0.295 / 0.299 | 1.656 | 0 | unexplained ({'ok': 3}) |
| files / core-first-snapshot-4 | 11.685 | 0.891 | 3/4; 58.36% | 6.902 | 4.388 | 24.145 | 0.464 / 0.465 | 1.548 | 0 | unexplained ({'ok': 3}) |

Failed/timeout/plan-violation сохраняются в строках и comparisons; ускорение для неполных или некорректных комбинаций не объявляется. MaxSampleGapMs>1000 мс отвергает measurement: требование task-04 — хотя бы один опрос в секунду. Естественная вариация — только значения baseline 1/2. Неизменённый compare.py сравнивает поля task-04 и diagnostics; объясняется только регистр Windows paths в прежних категориях. Никакие новые diagnostics не подавляются.

Размеры, стоимости каждой группы, полные планы, phase timings каждого прогона, статусы и находки — [summary.json](task-07-core-first-parallel-spike/summary.json).

## План и различия

- powertoys/core-first-xml-2: core=29, group counts=[114, 105], group costs=[15958.369599999993, 16018.1624]; unexplained={'OutputFilePath': 45, 'Diagnostics': 282}; additional core=[[], [], []].
- powertoys/core-first-xml-4: core=35, group counts=[51, 40, 64, 58], group costs=[6746.670899999999, 6573.389599999999, 8633.466, 8446.147699999998]; unexplained={'OutputFilePath': 45, 'Diagnostics': 282}; additional core=[[], [], []].
- powertoys/core-first-xml-8: core=48, group counts=[15, 29, 24, 25, 25, 29, 19, 34], group costs=[2443.2084, 3680.823699999999, 3433.5798999999997, 4416.734100000001, 3979.6554, 3788.2439000000013, 2541.1174000000005, 3926.7651000000005]; unexplained={'OutputFilePath': 45, 'Diagnostics': 282}; additional core=[[], [], []].
- powertoys/core-first-snapshot-2: core=28, group counts=[115, 105], group costs=[16044.997599999993, 16018.911899999997]; unexplained={'OutputFilePath': 45, 'Diagnostics': 282}; additional core=[[], [], []].
- powertoys/core-first-snapshot-4: core=35, group counts=[52, 44, 63, 54], group costs=[6825.5346, 6382.930299999999, 8596.546599999998, 8014.854999999998]; unexplained={'OutputFilePath': 45, 'Diagnostics': 282}; additional core=[[], [], []].
- powertoys/core-first-snapshot-8: core=48, group counts=[13, 29, 24, 25, 26, 29, 20, 34], group costs=[2396.4096, 3676.9180999999994, 3555.6661999999997, 4212.6124, 4069.929600000001, 3793.9145000000008, 2393.7717999999995, 4137.644899999999]; unexplained={'OutputFilePath': 45, 'Diagnostics': 282}; additional core=[[], [], []].
- files/core-first-snapshot-2: core=1, group counts=[7, 3], group costs=[5575.0724, 3543.0742]; unexplained={'Diagnostics': 15}; additional core=[['<solution>\\src\\Files.Core.SourceGenerator\\Files.Core.SourceGenerator.csproj'], ['<solution>\\src\\Files.Core.SourceGenerator\\Files.Core.SourceGenerator.csproj'], ['<solution>\\src\\Files.Core.SourceGenerator\\Files.Core.SourceGenerator.csproj']].
- files/core-first-snapshot-4: core=3, group counts=[4, 1, 1, 2], group costs=[745.093, 3417.2054, 154.6351, 125.8688]; unexplained={'Diagnostics': 15}; additional core=[['<solution>\\src\\Files.Core.SourceGenerator\\Files.Core.SourceGenerator.csproj'], ['<solution>\\src\\Files.Core.SourceGenerator\\Files.Core.SourceGenerator.csproj'], ['<solution>\\src\\Files.Core.SourceGenerator\\Files.Core.SourceGenerator.csproj']].

Различия режима requested/discovered остаются unexplained. Отсутствие native-request Failure diagnostics у групп показано baseline/aggregator/sequential контролями Files и публичным Worker code: LoadSolutionInfoAsync запрашивает native проекты, loadable roots через LoadProjectInfoAsync не запрашивают исходный native inventory. В ResolveReferencesAsync первая проверка loadability использует Ignore, затем возможен metadata fallback; это другой путь, чем прямой requested native load. Для E1 потребовалось бы сохранить весь список original requested projects и диагностический режим, проверить семантику missing/unloadable targets и заново доказать эквивалентность. Такой адаптер не реализован и не считается объяснением для go. [Разбор ROA=false, snapshot ownership и реального изменения OutputFilePath](task-07-core-first-parallel-spike/graph-and-load-mode.md) содержит полный inventory: XML даёт 15 OutputFilePath и 94 diagnostics различия на прогон; необъяснённых document различий нет.
В Files planned core N2=1, N4=3; actual core=2/4: фаза 1 дополнительно обнаружила Files.Core.SourceGenerator через dependency load, не представленную compilation snapshot. Его WeightMs=0 в профиле task-05.1, поэтому planned cost share не меняется; count модели и фактический count различаются. Table labels и additionalCorePaths сохраняют эту разницу. Размеры групп в плане тоже planned; фактические counts с агрегатором сохранены отдельно. Прогрев 0 отражён в warmup каждой комбинации summary; speedup/gate вычисляются по measured1/2/3.

## Модель и прогноз

Комбинация core-first-xml-4, N=4: h1 proxy=585.740 мс, core residual=7006.951 мс, h2 proxy=513.152 мс, max(group) residual=13783.167 мс, merge=106.797 мс. Сумма ≈ 21.996 с, measured wall=22.453 с; setup/plan и scheduling объясняют разницу. Competition proxy=1.509.

h — время до первого progress callback и включает работу первого evaluation/build; точное время spawn публичный API не отдаёт. Core и group residual получены вычитанием этого proxy, не независимым профилированием. Прогноз ниже не измерен и не валидирован; предполагает постоянный host/merge, равномерные группы и переносимый competition proxy. При no-go он не обещает production ускорения.

| Baseline, с | Core share | Прогноз, с | Прогноз × |
|---:|---:|---:|---:|
| 120 | 5% | 49.975 | 2.401 |
| 120 | 22% | 62.617 | 1.916 |
| 120 | 50% | 83.438 | 1.438 |
| 300 | 5% | 123.487 | 2.429 |
| 300 | 22% | 155.185 | 1.933 |
| 300 | 50% | 207.392 | 1.447 |

## Gate, Files и validation

Gate: strict equivalence=False; PowerToys N≤4 wall≤75% baseline=True; max measured BuildHost peak≤4GB=True; matrix complete=True.

Для Files отдельный go не выносится. Условие отказа от параллели: оценка h1+core+h2+max(group)+merge+setup не меньше ordinary baseline либо память N не укладывается в лимит. Это предпочтительнее порога только по count: распределение стоимости существенно. При нарушении плана или несовпадении observation нужна обычная последовательная загрузка.

Матрица снята до исправлений failure handling. [Измеренные исходники](task-07-core-first-parallel-spike/measured-source-sha256.json) отделены от [финальных исходников](task-07-core-first-parallel-spike/source-sha256.json): после матрицы добавлены cached sampler finalization, durable group outcomes и regression injection. Последовательность успешной загрузки, граф, global properties, merger и границы wall сохранены; ошибочные ветки теперь сохраняют первоначальную причину и результаты соседних групп. Это не повтор всей матрицы на финальном runner. [Точная дельта C# хэшей](task-07-core-first-parallel-spike/post-matrix-source-delta.json) связывает обе версии. CoreFirstChecks проверяет сериализацию исходной ошибки после stop, отдельную sampler error и group failure/timeout с partial projects/progress. Standalone Release build и check-core-first/check-merge/check-profile прошли через Roslyn MCP. Финальные проверки координатора: Release solution build passed, полный main Category!=AnalyzerLifecycle — 1362 passed / 1 прежний SDK-specific failed / 0 skipped; весь SourceStructure в этом запуске — 123 passed. Независимое ревью round 2 ACCEPT, формальная приёмка code/tests pending зелёного единого main CI. AnalyzerLifecycle не нужен: production lifecycle/paths не менялись. [Финальная validation и история review](task-07-core-first-parallel-spike/validation.md) фиксируют scope, причины failure и отсутствие коммита. Контрольная загрузка финального runner на Files совпала с измеренным прогоном по всем полям Projects и Diagnostics.

Тяжёлые данные — artifacts/parallel-load/task-07-evidence; в docs только summary и [SHA-256 inventory](task-07-core-first-parallel-spike/artifact-sha256.json). Компаратор task-04 и поля SolutionObservation не изменены.
