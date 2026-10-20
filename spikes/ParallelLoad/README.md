# ParallelLoad — изолированный эксперимент

Task-07.1 добавляет допустимые loader inputs `SolutionDir` и native/unloadable
requests первой фазы, `--graph snapshot-load`, `--partition greedy|cluster`,
`--epsilon 0.15`, `--core-depth 1|2`, `--phase2 parallel|isolated` и per-BuildHost
sampled CPU/zero-CPU intervals. Ограничение баланса cluster строгое, со средним
по всем N; `infeasible-search` сохраняет план и не запускает загрузку.

```text
core-first --solution PATH --output PATH --workers 4 --graph xml --snapshot BASELINE --profile PROFILE --partition cluster
core-first --solution PATH --output PATH --workers 4 --graph snapshot-load --snapshot BASELINE --profile PROFILE --partition cluster --phase2 isolated
core-first --solution PATH --output PATH --workers 4 --graph xml --snapshot BASELINE --profile PROFILE --partition cluster --core-depth 2
core-plan --solution PATH --output PATH --workers 4 --graph xml --snapshot BASELINE --profile PROFILE --partition cluster
python spikes/ParallelLoad/check_core_first_continuation.py
python spikes/ParallelLoad/exhaustive_core_first_witness.py --solution PATH --snapshot BASELINE --profile PROFILE --output WITNESS
python spikes/ParallelLoad/summarize_core_first_continuation.py --artifacts ARTIFACTS --matrix ARTIFACTS --output SUMMARY
```

`core-plan` — offline export, без loader; настоящее планирование выполняется
внутри measured wall заново. JSON матрицы имеют префикс `powertoys-`/`files-` и
суффикс `-0.json` для warmup или `-1/2/3.json` для measured runs; комбинация
определяется по metadata. Step0 использует отдельные `step0-*` имена.

Контракт продолжения и текущая evidence:
[task-07.1](../../docs/workspace-load-performance/epoch-0-stage-measurement/task-07.1-core-first-equivalence-and-partition-hi.md),
[отчёт](../../docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-07.1-core-first-equivalence-and-partition.md).

Публичный MSBuildProjectLoader 5.9.0, Windows, net10.0. Проект не входит в
solution/CI сервера и исключён из его Compile/None items.

Основной протокол и результат:
[task-04](../../docs/workspace-load-performance/epoch-0-stage-measurement/task-04-parallel-loader-spike-hi.md),
[отчёт](../../docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-04-parallel-loader-spike.md).

Перед измерениями выполнить restore целевого solution через Visual Studio
MSBuild.exe, Debug|x64. Для каждого запуска использовать новый процесс и новый
output; сначала прогрев, затем три измерения. Не сбрасывать cache ОС и не
выключать антивирус. В одной серии не запускать комбинации одновременно.

Через MCP `run_dotnet_build` указать workspacePath этого csproj и
configuration Release; через `run_dotnet_run` передать один набор arguments:

```text
baseline --solution <absolute.slnx> --output <absolute.json>
baseline-binlog --solution <absolute.slnx> --output <absolute.json>
parallel --workers 4 --strategy round-robin --solution <absolute.slnx> --output <absolute.json>
parallel --workers 4 --strategy cluster --solution <absolute.slnx> --output <absolute.json>
check-merge
profile --solution <absolute.slnx> --output <fresh-directory>/profile.json
plan --profile <absolute-profile.json> --workers 4 --output <absolute-plan-4.json>
check-profile
```

Configuration и Platform загрузчика фиксированы: Debug|x64. Baseline принимает
форматы solution, поддерживаемые Roslyn; parallel читает XML slnx для двух
решений протокола. Filter пишется во временный каталог с абсолютным путём
исходного solution и удаляется вместе с binlog/файлами монитора после замера.
Cluster использует буквальные XML ProjectReference, без evaluation.

Режим sample-process — внутренний процесс монитора; пользователь его отдельно
не запускает. CPU самого монитора исключён из метрик загрузчика. Root CPU
снят точно на границах wall, CPU потомков — нижняя оценка по опросам 100 мс.
Timeout загрузки — 10 минут. Failure/timeout даёт JSON с причиной и exit code 2;
MSBuild Failure diagnostics могут присутствовать и при успешном завершении API.

Проверка check-merge охватывает ownership, TFM, fallback, IDs, aliases,
EmbedInteropTypes и отсутствующие native targets. Снимки сравниваются отдельно:

```text
python spikes/ParallelLoad/compare.py --directory <raw-output-directory> --evidence <evidence-directory>
```

Имена основных файлов: `powertoys-baseline-0.json`,
`powertoys-round-robin-4-1.json`, `files-round-robin-2-3.json` и т. п.
Скрипт сначала сравнивает baseline 1/2; он не игнорирует произвольные новые
значения в естественно меняющихся полях. Различия только регистра Windows
paths вынесены отдельно; строгие before/after сохраняются. На других файловых
системах нужно отдельно проверить допустимость этой интерпретации.

Task-05: `profile` выполняет baseline-binlog и replay; сохраняет `profile.load.json`
и каталог `profile-binlogs/` рядом с output. Использовать новый output: существующий
каталог binlog отвергается. `Status=failed|incomplete`, exit code 2 — измеренный
отказ профиля. Отсутствующие интервалы evaluation/build записываются как `null`,
не как нулевая стоимость. Keys и references в профиле обезличены одинаково.

Task-05.1 заменяет стоимость evaluation+build на `WeightMs`: разрыв от конца
предыдущего top-level build того же binlog до конца текущего. Первый интервал
записан как `FirstMs`, его `WeightMs=0` (в модельную сумму он не входит).
`EvaluationMs=null` допустим. Replay включает forward compatibility; recoverable
error в файле с проектами и перекрытие top-level builds дают failed. Пустые сессии
сохраняют формат, inventory событий и recoverable errors без стоимости.
Сумма first+weights должна отличаться от GroupMs[0] не больше чем на 10 %;
FancyZonesEditor и ColorPickerUI на PowerToys обязаны быть markup.

`replay-profile` с теми же `--solution`/`--output` пересчитывает профиль по уже
сохранённым `profile.load.json` и binlog; новый load не выполняется. Этот режим
используется для дополнительных replay-проверок одного свежего замера.
`comparison-partitions --solution PATH --output PATH` экспортирует порядок
solution и точные группы существующих round-robin/cluster без загрузки.

Калибровка и отрицательный результат:
[task-05.1](../../docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-05.1-profile-retry.md).
Две направленные проверки модели не прошли; планы N=2/4/8 остаются
`uncalibrated-model`. Gate для реальных загрузок закрыт.

```text
python spikes/ParallelLoad/calibrate_profile.py --artifacts <repo>/artifacts/parallel-load/task-05.1-evidence --task04 <repo>/artifacts/parallel-load/task-04-evidence --evidence <repo>/docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-05.1-profile-retry
```

Сначала скрипт пишет `model-profile.json`, затем режимом `plan` строятся планы
N=2/4/8 в evidence, после чего скрипт повторяется для их диагностической сводки.
Training — только measured runs 1/2/3 одной стратегии; held-out — другая
стратегия, без изменения параметров по её результатам. Расхождения числа
проектов между graph closure и реальными группами сохраняются отдельно.
Native Failure callbacks и публичный native progress записываются в load report;
если progress отсутствует, точная длительность попыток остаётся неизвестной,
сводка показывает верхние границы связанных межпроектных разрывов.

`plan` принимает только полный профиль. Корни, связанные через общий markup,
объединяются транзитивно; сортировка по стоимости замыкания, выбор минимальной
итоговой стоимости группы с бесплатными уже включёнными dependencies. Компоненты
циклов без корней добавляются явно. Без прошедшей калибровку модели результат
помечается `uncalibrated-model` и не разрешает реальные parallel-замеры. Режим
`parallel --strategy plan` не реализован: gate task-05 остановил работу до шага 2.

Исторический результат task-05 — [инструментальный blocker](../../docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-05-profile-guided-partition.md), снятый в task-05.1.
Проверка `check-profile` охватывает транзитивный общий markup, повторяемое ядро
без markup, циклы, полное покрытие и отказ для неполного/повреждённого профиля.
Сводка отказа и сравнение ordinary snapshots тем же компаратором task-04:

```text
python spikes/ParallelLoad/summarize_profile.py --artifacts <repo>/artifacts/parallel-load/task-05-evidence --task04 <repo>/artifacts/parallel-load/task-04-evidence --evidence <repo>/docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-05-profile-guided-partition
```

Task-07 добавляет `core-first`: общее замкнутое ядро грузится одним solution
load, группы — собственными loader и ProjectMap.Create(coreSolution).
`--snapshot` указывает baseline этой же серии; при graph=xml он используется
только как inventory загружаемых проектов, сами edges берутся из XML.
Исходное написание request paths берётся из solution, стоимость — из профиля
task-05.1; без `--profile` весом становится число проектов.

```text
check-core-first
core-first --solution <absolute.slnx> --output <absolute.json> --workers 4 --graph snapshot --snapshot <same-series-baseline.json> --profile <task-05.1-profile.json> --aggregator no-restore
core-first --solution <absolute.slnx> --output <absolute.json> --workers 4 --graph xml --snapshot <same-series-baseline.json> --profile <task-05.1-profile.json> --aggregator no-restore
```

`--aggregator restore` добавляет restore только временного агрегатора с
RestoreRecursive=false через VS MSBuild.exe; `sequential` — контроль с отдельным
LoadProjectInfoAsync на каждый root при одной карте группы. Restore/setup входят
в wall. Файлы агрегатора, global.json и пустые Directory.Build.props/targets
находятся в временном каталоге вне solution и удаляются после завершения.

Повтор пути вне ядра, новый ID ядра или progress ядра в группе дают
`Status=plan-violation`, exit code 2. Общие каталоги проектов принадлежат одной
группе или целиком промотируются в ядро с dependency closure. Snapshot observations
и compare.py task-04 не изменены; новые diagnostics не исключаются.
FirstProgressMs — публичный proxy (включает первую evaluation/build), а не
независимое время spawn BuildHost.

```text
python spikes/ParallelLoad/summarize_core_first.py --artifacts <repo>/artifacts/parallel-load/task-07-evidence --evidence <repo>/docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-07-core-first-parallel-spike
```

Имена матрицы: `<solution>-baseline-0..3.json` и
`<solution>-core-first-<xml|snapshot>-<N>-0..3.json`. Скрипт требует весь
заданный inventory: PowerToys N=2/4/8 оба графа, Files N=2/4 snapshot;
отсутствующие/неуспешные прогоны сохраняются с причиной, gate не проходит.
В docs сохраняются сводка и хэши, полные snapshots/obj witnesses — artifacts.
`task07_obj_inventory.py` снимает read-only SHA-256/size/mtime witness всех
obj файлов до/после обычной и агрегаторной загрузки.

Итоговые данные скрипт берёт из `{powertoys,files}-restored/`; предварительные
ordinary snapshots — из `{powertoys,files}/`. Он проверяет замкнутость обезличенного
графа, сравнивает поля snapshots и diagnostics, сохраняет хэши/версии binlog и
явные `null` для всех недоступных прогнозов. Содержимое binlog в docs не переносится.
