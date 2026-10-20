# Task-07.1 — Core-first: эквивалентность и разбиение

Дата: **2026-10-10**, Europe/Moscow. Изолированный spike; production, version,
solution и CI не меняются. Evidence и вердикт task-07 остаются без изменений.
Вердикт эксперимента: **no-go**. В PowerToys эквивалентность восстановлена и
скорость проходит порог; на Files при фиксированном ε=0,15 кластерные планы N=2/4
невыполнимы, поэтому полная эквивалентность требуемой матрицы не доказана.
Шаг 0 и 52 попытки матрицы завершены. Формальная приёмка code/tests pending:
обязательный main CI имеет один прежний SDK-specific failure. Commit не создан.

## Шаг 0: допустимые входы загрузки

У групп `MSBuildProjectLoader` глобальное публичное свойство `SolutionDir` теперь
равно абсолютному каталогу **исходного** solution с завершающим `\`. Это ровно
значение, которое Roslyn 5.9.0 записывает в `SetSolutionProperties`, когда каталог
существует. Оно передаётся до design-time build, через constructor properties;
`ProjectInfo`, output paths и diagnostics после загрузки не исправляются.

В `.slnf` первой фазы входят ядро и каждый исходный solution entry, отсутствующий
в графе загружаемых проектов. Таким образом, native и другие незагружаемые проекты
запрашиваются один раз через `LoadSolutionInfoAsync`. Они не становятся корнями
агрегаторов. Обе фазы используют стандартный режим публичного loader.

Read-only декомпиляция пакета `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0
сохранена в `artifacts/parallel-load/task-07.1-evidence/loader-decompilation.md`:

- `SolutionFileReader.ReadSolutionFileAsync` сначала читает `.slnf`, затем
  `SolutionFilterReader.TryRead` заменяет `absoluteSolutionPath` исходным solution.
- `LoadSolutionInfoAsync` вызывает `SetSolutionProperties` с этим исходным путём;
  режимы requested/discovered совпадают, `preferMetadataForReferences...=false`.
- `LoadProjectInfoAsync` не вызывает `SetSolutionProperties`. Requested root
  получает `ThrowForAll`, discovered references — режим `SkipUnrecognizedProjects`.
  У групп requested root — временный SDK aggregator; исходные roots являются
  discovered. Настройки режима не подменялись.
- `ResolveReferencesAsync` при `ReferenceOutputAssembly=false` всё равно вызывает
  загрузку проекта и затем не добавляет compilation reference. Поэтому такое
  ребро необходимо графу ownership даже при отсутствии его в snapshot.

Проверены две **новые** baseline загрузки каждого решения и одна core-first
загрузка: Files XML N=2 и PowerToys XML N=4. Все шесть — отдельные процессы после
нового restore Debug|x64; commits снова проверены координатором.

| Решение | Baseline 1, с | Baseline 2, с | Core-first, с | Эквивалентность |
|---|---:|---:|---:|---|
| Files | 11,502 | 11,265 | 9,023 | identical |
| PowerToys | 42,127 | 41,925 | 25,983 | identical кроме 13 различий регистра Windows paths |

Обе пары baseline совпали полностью. В каждом core-first — ноль unexplained и
ноль `plan-violation`; 11/248 проектов соответственно. Все три значения
`OriginalSolutionDirectory`, `Phase1SolutionDirectory`, `GroupSolutionDirectory`
совпали: `<solution>\`. В Files первая фаза запросила 7 paths, в PowerToys — 148,
включая ядро. Raw callback counts не объявляются уникальными diagnostics;
компаратор проверяет существующий distinct inventory без изменений своего кода.

Компаратор `compare.py` и `SolutionObservation.cs` byte-unchanged относительно
начала продолжения. Полные before/after, состояние sampler, request inventories
и SHA-256 шести JSON — в `artifacts/parallel-load/task-07.1-evidence/step0-summary.json`.
Шаг 0 разрешает последующую матрицу; его одиночные времена не заменяют медианы n=3.

## Граф и кластеризация

`--graph xml` сохраняет буквальные XML `ProjectReference`, включая ROA=false.
`--graph snapshot-load` берёт compilation references свежего baseline и добавляет
буквальные XML-рёбра с `ReferenceOutputAssembly=false` в attribute или metadata
element. Это проверка конкретного дополнения snapshot; она не объявляется полным
evaluated graph imports/conditions/property-generated references. Исходный
`snapshot` сохранён для воспроизводимости task-07.

`--partition greedy` сохраняет прежнюю раскладку. `--partition cluster` начинает
с неё и выполняет детерминированный локальный поиск максимум 20 раундов:

1. Все перемещения одного root в другую группу.
2. До 128 swaps roots между группами, ранжированных по изменению суммарной
   стоимости общих замыканий с roots группы.
3. До 32 bundles roots, имеющих общий дорогостоящий проект, с попыткой собрать
   каждое bundle в одной группе. Это помогает пройти плато, когда один move
   оставляет общую зависимость в нескольких группах.

Для каждого кандидата **заново** вычисляются core, его замыкание вниз и promotion
общих каталогов. Приоритет: уменьшить превышение баланса; затем уменьшить
стоимость ядра; затем максимальную стоимость группы. Строгое ограничение —
`max(GroupCosts) <= (1 + ε) * sum(GroupCosts) / N`, ε по умолчанию **0,15**.
Стоимость групп исключает ядро; среднее делится на все N, включая пустые группы.
Нулевые стоимости имеют условный ratio=1 и удовлетворяют ограничению 0<=0.

Это ограниченный эвристический поиск, а не доказательство глобального минимума.
`infeasible-search` означает, что поиск не нашёл допустимую раскладку. Лучший
найденный план и причина сохраняются, загрузка не выполняется, epsilon и число
учитываемых workers не меняются. Нулевая observation такого отказа не является
доказательством эквивалентности. Offline export `core-plan` ничего не загружает;
планирование настоящего прогона выполняется заново и включено в wall.

Проверки coverage, downward closure и общего obj остаются обязательными.
Каждой группе создаётся отдельный `ProjectMap.Create(coreSolution)`.

Отдельное read-only evidence уточняет отказ на Files. Для его буквального XML
графа есть четыре roots: App.Server, App, App.UITests и InteractionTests.
`exhaustive_core_first_witness.py` перечислил **все** 16 assignments для N=2 и
256 assignments для N=4 с теми же closure/directory/core правилами. Допустимых
при ε=0,15 — **ноль**. Минимальный max/mean: **1,222852109** и **2,445704218**.
Это finite proof для данного XML-графа и данного профиля, а не общий вывод о
Files, evaluated imports или других SDK. В `files-partition-witness.json` сохранены
roots, лучший баланс, assignments, стоимость и хэши XML/profile; обычный loader
в этой проверке не запускался. Сам runtime продолжает честно писать
`infeasible-search`, не выдавая свою эвристику за exhaustive solver.

## Фазы и telemetry

`--core-depth 2` повторяет схему внутри исходного ядра: core-of-core загружается
одним solution loader, группы ядра — своими aggregator loaders поверх его карты,
после merge основная фаза 2 получает карту целого ядра. Native/unloadable requests
проходят только через самый нижний solution loader. Выделены время нижнего ядра,
время каждой его группы, merge, outcome и нарушения ownership. Измерение режима
условное: только лучшая комбинация N<=4 при доле phase1 более 25% wall.

`--phase2 isolated` сохраняет тот же план и собственный loader/process manager
каждой группы, но выполняет основные группы последовательно. Один публичный
loader call не гарантирует один физический процесс BuildHost: в текущей серии
наблюдаются два процесса на loader. Их runtime kind не записан этим sampler и
не выводится из прежних binlogs. В speed gate этот режим не входит;
эквивалентность ему необходима. Изолированное время минус profile weight — остаток
с влиянием cold process, aggregator, порядка и различия profile context; это не
чистое измерение стоимости создания процесса. First-progress latency остаётся
отдельным proxy, включающим работу первого проекта.

Sampler записывает CPU каждого BuildHost, первую/последнюю успешную polling
observation и долю интервалов с **нулевым приращением CPU**. Разрешение около
100 мс; start/exit censored, CPU — нижняя оценка по опросам. Нельзя вычислять
waiting share как wall-CPU: многопоточный CPU может превышать wall.
Zero-CPU share не включает ожидание при ненулевом CPU других threads и не
доказывает I/O или влияние антивируса. Нельзя усреднить share всех физических
hosts и объявить её ожиданием работы групп: один процесс может почти всё время
быть неактивным, пока второй активно вычисляет. Phase/group offsets помогают ограничить
соответствие процессу; при параллельных группах public progress не даёт надёжной
PID-привязки. Kaspersky и Defender не перенастраиваются.

## Матрица и сравнение разбиений

Координатор выполнил **52 попытки**: 44 исходной матрицы, 4 isolated и 4 recursive.
Каждая комбинация — прогрев 0 и три отдельных measured процесса 1/2/3.
Из 52 попыток 44 выполнили загрузку, 8 Files завершились отказом планировщика
до создания BuildHost. Отказы включают 2 warmup и 6 measured rows; их пустой
snapshot не является identical. Отдельные шесть загрузок шага 0 в этот счёт не входят.
Загружено 248/11 проектов PowerToys/Files; baseline pairs матрицы identical.
PowerToys имеет только прежние допустимые различия регистра paths: 6/7 у cluster,
13 у greedy на прогон. Unexplained и `plan-violation` во всех состоявшихся
measured загрузках — ноль. Failed/timeout загрузок нет; присутствуют explicit
`infeasible-search` строки. Полный inventory, исходные статусы и SHA сохранены.

Wall включает XML/snapshot/profile reading, поиск плана и подготовку агрегатора;
observation/serialization после wall, как в task-07. Код C#/csproj не менялся
между freeze и последней загрузкой. Snapshot graph использует baseline 1 **этой**
матрицы. Cache ОС сохранён, Kaspersky/Defender не перенастраивались.

Медианы n=3; memory — максимум **отдельного** measured run в десятичных GB.
BuildHosts — число физических процессов за весь прогон, а не concurrency peak.
`Case` означает identical кроме объяснённого регистра Windows paths.

| Решение / graph / partition / N | Wall, с | × baseline | Core count / cost | Phase1, с | Critical group, с | Plan, с | Peak max, GB | Busy cores | Hosts | Equivalence / status |
|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---|
| PowerToys baseline | 41,825 | 1,000 | — | — | 41,589 | — | 1,138 | 1,418 | 2 | identical; ok ×3 |
| xml / greedy / 2 | 32,099 | 1,303 | 29 / 17,01% | 7,101 | 24,351 | 0,140 | 1,385 | 2,500 | 6 | Case; ok ×3 |
| xml / greedy / 4 | 25,824 | 1,620 | 35 / 21,11% | 8,503 | 16,790 | 0,147 | 1,810 | 3,727 | 10 | Case; ok ×3 |
| xml / greedy / 8 | 26,964 | 1,551 | 48 / 26,79% | 11,068 | 15,248 | 0,146 | 2,743 | 4,687 | 18 | Case; ok ×3 |
| xml / cluster / 2 | 33,938 | 1,232 | 12 / 10,25% | 4,341 | 26,773 | 2,368 | 1,388 | 2,489 | 6 | Case; ok ×3 |
| xml / cluster / 4 | 29,621 | 1,412 | 12 / 10,38% | 4,406 | 18,041 | 6,608 | 1,796 | 3,457 | 10 | Case; ok ×3 |
| xml / cluster / 8 | 38,603 | 1,083 | 29 / 19,63% | 7,210 | 17,465 | 13,622 | 2,972 | 3,991 | 18 | Case; ok ×3 |
| snapshot-load / cluster / 4 | 28,892 | 1,448 | 13 / 10,54% | 4,516 | 17,762 | 6,068 | 1,851 | 3,559 | 10 | Case; ok ×3 |
| xml / greedy / 4, depth2 | 25,228 | 1,658 | 35 / 21,11% | 8,674 | 15,949 | 0,218 | 1,817 | 4,189 | 18 | Case; ok ×3 |
| xml / greedy / 4, isolated | 61,114 | diagnostic | 35 / 21,11% | 8,606 | 14,486 | 0,143 | 0,517 | 1,371 | 10 | Case; ok ×3 |
| Files baseline | 11,119 | 1,000 | — | — | 11,016 | — | 0,215 | 1,518 | 1 | identical; ok ×3 |
| Files xml / cluster / 2 | 0,072 (plan only) | — | 2 / 14,55% | не выполнена | не выполнена | 0,072 | — | — | 0 | не измерена; infeasible-search ×3 |
| Files xml / cluster / 4 | 0,073 (plan only) | — | 2 / 14,55% | не выполнена | не выполнена | 0,073 | — | — | 0 | не измерена; infeasible-search ×3 |

Сравнение cost профиля, в секундах; стоимость ядра исключена из групп:

| N | Greedy core / cost share | Greedy group costs | Cluster core / cost share | Cluster group costs | Cluster max/mean |
|---:|---|---|---|---|---:|
| 2 | 29 / 17,01% | 15,958; 16,018 | 12 / 10,25% | 17,291; 17,291 | 1,000003 |
| 4 | 35 / 21,11% | 6,747; 6,573; 8,633; 8,446 | 12 / 10,38% | 9,577; 9,176; 8,958; 6,822 | 1,109272 |
| 8 | 48 / 26,79% | 2,443; 3,681; 3,434; 4,417; 3,980; 3,788; 2,541; 3,927 | 29 / 19,63% | 4,298; 4,387; 4,142; 3,843; 3,653; 3,420; 4,167; 3,059 | 1,133336 |

Cluster достигает меньшего ядра и строгого баланса во всех трёх PowerToys N,
но не выигрывает wall. N=4 сокращает phase1 на 4,097 с; само планирование дорожает
на 6,461 с, а critical group увеличивается на 1,251 с. Это стоимость реального
алгоритма на 142 roots, не незамеченная предварительная работа. Для production
такой поиск потребовал бы отдельного ограничения стоимости планирования;
precomputed скорость этой задачей не измерена.

`snapshot-load` N=4 имеет ноль нарушений ownership, в отличие от прежнего snapshot
task-07. Это подтверждает достаточность добавления ROA=false **для этих запусков**,
а не полноту snapshot для любых evaluated references. 13 core projects и группы
65/43/70/57 отличаются от XML cluster 12 и 65/64/54/53; cost shares 10,54/10,38%.

## Изолированные группы и причина замедления

Выбрана лучшая non-recursive N=4 комбинация: XML greedy. Ровно её же план
выполнен последовательно в isolated. Native/core input и group maps одинаковы.

| Группа | Profile weight, с | Isolated, с | Isolated − profile, с | First progress proxy, с | Parallel, с | Parallel / isolated |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 6,747 | 12,737 | 5,991 | 0,441 | 14,535 | 1,141 |
| 1 | 6,573 | 11,336 | 4,762 | 0,439 | 14,184 | 1,251 |
| 2 | 8,633 | 14,486 | 5,853 | 0,445 | 16,790 | 1,159 |
| 3 | 8,446 | 13,686 | 5,240 | 0,458 | 16,316 | 1,192 |

Наблюдаемый штраф конкуренции — **14–25%**, медиана ratios **1,176**. Поэтому
прежние 1,5–1,66× относительно profile нельзя целиком объяснить конкуренцией:
изолированная загрузка уже имеет остаток 4,762–5,991 с относительно weight.
First-progress proxy всего 0,439–0,458 с. Остаток включает cold loader/processes,
агрегатор, порядок и несовпадающий profile context; чистая причинная стоимость
spawn не измерена.

Во всех трёх parallel и isolated запусках — 2 физических host первой фазы и
8 второй. Для каждой фазы сохраняются окна, каждый PID+creation identity, CPU,
observed lifetime и sampled zero-CPU share в исходных JSON. Внутри phase2 при
разделении **только по величине CPU**, без утверждения runtime kind:

- 4 наиболее CPU-активных host каждого parallel run: CPU 13,594–18,594 с;
  zero-CPU intervals 0–5,60%. В isolated: CPU 9,906–15,656 с, zero-CPU 0–6,47%.
- Остальные 4: parallel CPU 1,578–1,906 с, zero-CPU 72,58–83,02%; isolated
  CPU 1,359–1,656 с, zero-CPU 72,54–80,86%.

Это не свидетельство того, что все группы большую часть времени ждут: малый CPU
одного физического host соседствует с активной работой другого. В parallel public
progress не позволяет надёжно привязать PID к конкретной группе. Данные показывают
умеренную общую цену конкуренции и отдельный большой baseline/profile residual.
Причина этого residual и конкретный ресурс конкуренции не установлены.
Антивирус, import caches, IPC или GC остаются гипотезами; никакая из них не
объявляется доказанной. Сжатый анализ без PID-array — `selected-host-analysis.json`
в ignored artifacts; исходные per-host counters остаются там же.

## Рекурсивное ядро

У выбранного XML greedy N=4 phase1/wall = **32,93%**, поэтому условная ветка
обязательна и выполнена. Core-of-core: **7 проектов**, cost 2,743 с из 8,132 с
стоимости исходного ядра. Четыре подгруппы ядра: 5/9/7/7 проектов и weights
1,502/0,939/0,747/2,201 с. Все завершились ok, повторов/нарушений нет.

Медиана нижней фазы — **3,157 с**, first-progress proxy **0,591 с**. Подгруппы
ядра — **4,098 / 4,630 / 4,279 / 5,442 с**, merge ядра **1,818 мс**.
Полная phase1 **8,674 с**, против **8,503 с** без рекурсии: саму фазу 1 схема
не ускорила. Общий wall 25,228 против 25,824 с — разница 2,31%; critical group
основной фазы уменьшился на 0,841 с. Этот небольшой общий выигрыш нельзя
приписать ускорению ядра или считать устойчивым причинным эффектом по n=3.
Физических hosts за прогон 18 против 10, peak памяти 1,817 GB. Native requests
остались в нижней фазе один раз; final observation equivalent с 13 case-only.

## Модель и условный прогноз

Модель строится по paired isolated/parallel **depth1** XML greedy N=4. Изолированные
groups дают cold/context residual, а не предположение, что весь их перерасход
равен конкуренции. Profile service weights нормированы к текущему baseline:
`β = (B − h1) / totalProfileCost = 1,070079`. Для группы residual = isolated −
β×weight; значения 5,518/4,302/5,248/4,648 с, медиана **4,948 с**.
Это additive overhead proxy с несколькими причинами, **не чистая стоимость spawn**.
Измеренный first-progress h2 **0,443 с** хранится отдельно.

Параметры: h1 proxy 0,593 с; c = median(parallel/isolated) **1,175612**;
balance factor b **1,135995**; planning P 0,147 с; preparation A 0,006 с;
merge M 0,120 с. Для заданного ordinary времени T и core cost share f:

`W = max(0, T − h1)`;
`T_parallel ≈ P + A + h1 + f×W + c×(residual + b×(1−f)×W/4) + M`.

На той же calibration комбинации модель даёт **26,246 с**, измерено **25,824 с**,
ошибка **+1,63%**. Это in-sample calibration, не independent/held-out validation.
Если использовать только first-progress h2 вместо измеренного большого residual,
модель заметно занизит время. При zero non-core profile cost или отрицательном
calibrated residual script сохраняет evidence и выдаёт model unavailable.

| Ordinary T, с | Core share | Условный прогноз, с | Условный × |
|---:|---:|---:|---:|
| 120 | 5% | 50,526 | 2,375 |
| 120 | 22% | 64,048 | 1,874 |
| 120 | 50% | 86,319 | 1,390 |
| 300 | 5% | 116,618 | 2,572 |
| 300 | 22% | 150,524 | 1,993 |
| 300 | 50% | 206,368 | 1,454 |

Это **не измерения** 120/300 с и не обещание production ускорения. Допущения:
фиксированы residual, c, balance, setup/merge; новая стоимость распределяется
похожим образом, исходное ядро последовательно. Рекурсивная схема здесь не
экстраполируется: граф ядра у гипотетического solution неизвестен.

## Gate и последующий scope

Итоговый **no-go** определяется полнотой и корректностью required matrix:

- Шаг 0 — PASS; PowerToys measured equivalence — PASS, unexplained/violations 0.
- Speed threshold — PASS: N=4 greedy depth1 61,74% baseline; depth2 60,32%.
- Memory threshold всех состоявшихся measured загрузок — PASS: maximum
  **2,972 GB**, меньше 4 GB. Sampler errors 0; максимальный gap **325,626 мс**,
  меньше 1 с. Пропущенных planned attempts нет.
- Required Files cluster N=2/4 отказали до загрузки; equivalence этих measured
  rows **не доказана**. Exact witness подтверждает невозможность баланса при
  фиксированном ε=0,15 для данного графа/профиля. Полный strict gate не пройден.

На Files отдельный go/no-go не выносится. Уточнённое условие ordinary fallback:
не найден допустимый баланс до запуска loader, ожидаемый выигрыш после стоимости
планирования <=0 либо memory budget недостаточен. Отказ нельзя маскировать
исключением пустых workers, ослаблением ε или утверждением identical для пустого
snapshot. Производственная fallback branch этой задачей не реализована.

E1 не активируется. Снята конкретная преграда эквивалентности: будущий loader
обязан воспроизвести original `SolutionDir`, native/unloadable requested inventory
и diagnostic mode; snapshot графу нужны load edges ROA=false. Для общего решения
production должен получить evaluated load graph от host C/снимка B либо безопасно
отказаться при неполном ownership. Условия обычного fallback и bound планирования
нуждаются в самостоятельном контракте и новой validation; это не разрешение
обойти no-go или изменить требования продолжения задним числом.

Компактные числа и SHA raw matrix — [summary.json](task-07.1-core-first-equivalence-and-partition/summary.json).
Measured source text сохранён в ignored `measured-source/*.measured.txt`; его
[SHA inventory](task-07.1-core-first-equivalence-and-partition/measured-source-sha256.json)
отделён от Python analysis/report. Полные observations, диагностики, per-run
outcomes и сравнения лежат только в `artifacts/parallel-load/task-07.1-evidence`.

## Проверки реализации

MCP Release build spike и `check-core-first` прошли. Self-checks проверяют снижение
стоимости ядра при строгом балансе, swaps, детерминизм, отказ indivisible root с
пустыми workers, zero-cost graph, ROA=false edge из metadata, closed recursive
subset и случай CPU>wall. Предыдущие проверки aliases/TFM/IDs/merger, partial
failure evidence, one-stop sampler и cancellation сохранены.

`check_core_first_continuation.py` прошёл: .75 gate, отказ из-за unexplained на
Files, исключение isolated из speed gate, individual memory outlier,
all-failed/infeasible rows с сохранённым планом, sampler gaps, timeout и missing
baseline; после первого независимого review добавлена проверка nonempty groups
с нулевой profile cost: model unavailable с причиной, group evidence сохранена,
деления на ноль и вымышленных прогнозов нет. Первый запуск Python выявил
недоступный sandbox TEMP; scratch перенесён
в разрешённый ignored `artifacts/parallel-load`, повторная проверка прошла.
После model correction Python regression повторно прошла; проверяется также
неположительный additive residual и сохранение context overhead отдельно от
first-progress proxy. Финальная validation координатора: spike Release build и
check-core-first/merge/profile — PASS; solution Release build — PASS; main
`Category!=AnalyzerLifecycle`: **1362 passed / 1 failed / 0 skipped**. Внутри него
SourceStructure — **123 passed**. Прежний SDK10.0.401 failure
`Framework_selection_matches_installed_pack_files_without_loading_analyzers`:
expected `framework-task-input-closure-unproven`, actual дополнительно
`framework-selection-profile-unproven`. AnalyzerLifecycle не требуется: production
lifecycle/configuration не менялись. Формальная приёмка code/tests pending и
commit не создан. [История review и final validation](task-07.1-core-first-equivalence-and-partition/validation.md)
завершена координатором: независимое ревью round 2 **ACCEPT**, 2026-10-10, для standalone реализации и evidence отрицательного эксперимента; task/index statuses обновлены. Формальная приёмка code/tests остаётся pending main CI.
SHA-256 [52 matrix + 6 step0 attempts и supporting evidence](task-07.1-core-first-equivalence-and-partition/raw-attempt-sha256.json),
[финального Python analysis](task-07.1-core-first-equivalence-and-partition/analysis-source-sha256.json),
[точный Files witness](task-07.1-core-first-equivalence-and-partition/files-partition-witness.json)
позволяют связать компактные числа с ignored raw artifacts.
