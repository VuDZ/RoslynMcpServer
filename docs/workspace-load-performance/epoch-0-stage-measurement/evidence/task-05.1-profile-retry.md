# D/E0/task-05.1 — Профиль получен; модель непригодна, no-go

Дата: **2026-10-10**. Исполнитель и проверка: **Codex (GPT-6)**.
Статус: **implemented — эксперимент завершён отрицательным результатом;
формальная приёмка code/tests pending единого зелёного main CI**.

**Вердикт: no-go, шаг 2 не выполняется.** Оба инструментальных блокера
task-05 сняты. Свежие профили PowerToys и Files полны по стоимости через
межпроектные разрывы. Обе направленные проверки модели не проходят неизменённые
пороги 15 % / 10 %. Даже диагностический прогноз plan N=4 — 23,292 с,
выше gate 18,770 с (45 % baseline task-04). Это отрицательный результат
данной модели, а не доказательство невозможности любой параллельной загрузки.

Выполненный scope: standalone spike `spikes/ParallelLoad`, replay, profile/plan,
экспорт старых разбиений, офлайн-калибровка, evidence и статусы task-05/task-05.1.
Production, корневой csproj, solution, CI, версия и CHANGELOG этой задачей не
изменены. Оптимизация не активирована. Реальные `parallel --strategy plan`
и новая baseline-серия шага 2 не выполнялись.

## Ревью, 2026-10-10 (Claude Opus 5.5)

Вердикт no-go принят. Ограничения отчёта (несошедшаяся модель,
`closureReconstructionComplete=false`, неизмеримое native время) названы честно
и не влияют на вывод: самый правдоподобный для plan прогноз — RR-обученная
модель (группы plan по размеру похожи на round-robin) — даёт при N=4 1,79×, и
это согласуется с измеренными 1,51× round-robin в task-04.

Что ограничивает ускорение, по параметрам RR-модели (h ≈ 6,0 с, c(4) ≈ 0,99,
Σw ≈ 38,5 с):

- без общего ядра N=4 дал бы ≈ 6,0 + 0,99 · 38,5 / 4 ≈ 15,5 с (≈ 2,7×);
- общее ядро plan N=4 — 46 проектов, 22 % стоимости, — загружается в каждой
  группе: ≈ 6,0 + 0,99 · (8,5 + 7,5) ≈ 21,8 с; отчёт — 23,3 с с дисбалансом;
- `h` ≈ 6 с — холодный BuildHost (кэш импортов и evaluation), который
  последовательная загрузка платит один раз, а N групп — N раз.

Убрать повтор ядра публичным API нельзя: `LoadSolutionInfoAsync` не принимает
`ProjectMap`, а `LoadProjectInfoAsync(path, projectMap, …)` грузит один корень
за вызов, и каждый вызов — новый BuildHost с тем же `h`. Ветка параллельной
загрузки через публичный `MSBuildProjectLoader` для D закрыта.

**Поправка 2026-10-10 (Claude Opus 5.5).** Вывод «убрать повтор ядра публичным API
нельзя» неверен. `ProjectMap.Create(Solution)` публичный и кладёт в карту
`ProjectInfo` проектов, а `Worker.LoadProjectInfosFromPathAsync` при попадании в
карту BuildHost не вызывает. Ограничение «один корень на вызов» обходится
проектом-агрегатором со ссылками `ReferenceOutputAssembly=false`: такие ссылки
`ResolveReferencesAsync` загружает. Вердикт no-go task-05/task-05.1 для разбивки
по профилю с повтором ядра остаётся в силе; ветка «ядро один раз, затем группы»
проверяется в [task-07](../task-07-core-first-parallel-spike-hi.md).

Исправление ревью: строка темы в `docs/README.md` ещё показывала task-05 как blocked.

## Входы и воспроизводимость

- [Задача task-05.1](../task-05.1-profile-retry-hi.md),
  [неизменённые пороги task-05](../task-05-profile-guided-partition-hi.md),
  [инструкция spike](../../../../spikes/ParallelLoad/README.md).
- HEAD проверен заново: PowerToys `7b6789c576b304c7e1eda8ce571cc1b0afd280ab`,
  Files `1994d9a4269f5144c70bc67d6a48de4a20616912`. Debug|x64.
- Restore обоих решений через VS MSBuild.exe, `-t:restore`, Debug|x64:
  **exit code 0**. Логи в `<repo>/artifacts/parallel-load/task-05.1-evidence/`.
- Каждый `profile` — свежий процесс MCP `run_dotnet_run`, после restore.
  Измерено по одному ordinary profile-load на решение; это входы профиля,
  не серия n=3 для speedup. Дополнительный `replay-profile` пересчитывал
  тот же профиль из этих сохранённых binlog и load snapshot без новой загрузки.
- SDK inventory совпадает с task-04: 9.0.314, 10.0.201, 10.0.300, 10.0.401.
  Kaspersky: две записи `productState=266240`; Defender: `393472` — совпадают.
  Антивирус и cache ОС не перенастраивались.
- VS Microsoft.Build.dll: FileVersion `18.10.1.42706`, ProductVersion
  `18.10.1-1.26427.6+3cd27c13eb422ceea9de6fb84d12684e3bcf3188` — совпадают с task-05.
  Task-04 не сохранила DLL fingerprint, поэтому её неизменность с task-04
  отдельно не доказана. Наблюдаемая регистрация обоих profile-load — SDK
  10.0.401, совпадает с полями Registration самих снимков task-04.
  Reader — Microsoft.Build `18.9.11+e34a38d2ae1fc26406a317517196e55c68ff83ab`.
- Все **44** снимка task-04 доступны и прочитаны:
  [inventory с хэшами](task-05.1-profile-retry/task04-inventory.json).
  Для калибровки используются только PowerToys measured runs 1/2/3;
  warmup 0 исключён из подгонки и ошибок, но входит в inventory и проверку
  реконструкции. Files-снимки используются для контроля профиля/эквивалентности;
  cluster/N=8 на Files в task-04 отсутствуют и не выдуманы.

Тяжёлые входы — binlog, полные load snapshots, restore logs — только в
`<repo>/artifacts/parallel-load/task-05.1-evidence/{powertoys,files}/`.
В docs находятся профили, планы, сводки и хэши; абсолютных путей машины
или профиля пользователя в опубликованных профилях нет.

## Replay и стоимость

`AllowForwardCompatibility=true`; подписка на `RecoverableReadError` обязательна.
В файле с хотя бы одним `ProjectStarted` recoverable error даёт failed с
`ErrorType` и `RecordKind`. Файл без проектов сохраняется как пустая сессия
с форматом, inventory событий и errors, без стоимости. Top-level builds
сортируются по Timestamp внутри файла; любое перекрытие или отсутствующий
finish/BuildStarted даёт failed. Nested reference builds уже входят во время
вызывающего top-level build и второй раз цели вызова не начисляются.

PowerToys: `capture-1.binlog`, формат **26**, **248** BuildSubmissionStarted,
5729 ProjectStarted и столько же ProjectFinished; **248** top-level builds,
1285 ProjectEvaluationStarted. `capture-2.binlog`, формат **27**, ровно
**6** событий: BuildStarted, три BuildMessage, LoggersRegistered, BuildFinished;
проектов и recoverable errors **0**. Он не входит в сумму стоимости.
Files: формат **26**, **11** top-level builds, ProjectEvaluationStarted **0**.
Recoverable errors и перекрытий нет ни в одном реальном файле.

`w(p)` — от конца предыдущего top-level build файла до конца текущего.
Первый интервал от BuildStarted выделен в `FirstMs`, его `WeightMs=0`:
его целиком покрывает host-компонент, в проектную сумму он не включён.
Нулевое значение здесь означает явное исключение первого интервала,
а не неизвестную стоимость. Evaluation сохраняется для наблюдений;
`null` у 215/248 PowerToys и 11/11 Files не делает профиль неполным.

Сверка с **GroupMs[0]**, то есть LoadSolutionInfoAsync того же profile-load:

- PowerToys: first **414,482 мс**, сумма w **38532,115 мс**, итого
  **38946,596 мс** против **39402,014 мс**, расхождение **1,156 %**.
  Wall с AddSolution — **39611,735 мс**.
- Files: first **311,659 мс**, сумма w **10670,771 мс**, итого
  **10982,430 мс** против **11540,383 мс**, расхождение **4,835 %**.
  Wall — **11629,424 мс**.

Обе сверки проходят допуск 10 %. Начальная часть запуска BuildHost до
BuildStarted и хвост после последнего build не входят в binlog-интервал;
поэтому профиль не обязан совпадать с GroupMs точно. Эти остатки явно
сохранены как расхождения; новый host `h` калибруется отдельно.

Распределение w в PowerToys, мс: min **0** (исключённый первый), median
**97,386**, p90 **377,000**, max **1583,479**. First — **1,064 %** всей
binlog-суммы. Files: median **158,478**, p90 **3417,205**, max **4539,831**;
first — **2,838 %**. P90 — ближайшее нижнее порядковое значение, без интерполяции.

**Доля own build в w** (проекты с first исключены и из числителя):
PowerToys **51,388 %**, Files **88,408 %**. OwnBuildMs остаётся в профиле;
evaluation и межпроектная обработка существенны, особенно на PowerToys.

## Native попытки: наблюдение и граница измерения

В solution PowerToys **113 vcxproj**; Failure callbacks подтверждают все
113 уникальных путей. Записано **1014** сообщений `Cannot open project ...
file extension ... is not associated with a language` и **1252** MSBuild
Failure callbacks с vcxproj. Повторные callbacks включают ссылки на native
из C# проектов: их нельзя выдавать за 2266 top-level проектов или за точное
число отдельных BuildHost RPC. Files: native проектов/callbacks **0**.

Добавлен публичный `IProgress<ProjectLoadProgress>`, но native progress
событий **0**. Failure сообщает завершение отказа, без начала попытки.
Поэтому **изолированная длительность native попыток не идентифицируется**
этими публичными событиями и сохраняется как `nativeAttemptMs=null`.
Это ограничение измерения, не нулевое время и не скрытый успешный замер.

Для сверки с порядком solution записаны top-level интервалы и timestamps
Failure. **1005** unsupported callbacks попали между предыдущим end и
следующим start; **0** — внутрь own build; **9** — за пределами записанного
интервала. Сумма содержащих их межпроектных разрывов — **13648,430 мс**.
Это верхняя граница связанной native активности **внутри измеренного
binlog-интервала**, включающая также evaluation и работу Roslyn. Её нельзя
называть точным native временем или верхней границей всех native попыток
за полный load. Подробности по следующим C# проектам:
[native summary](task-05.1-profile-retry/powertoys-native.json).

Для модели выбрана разрешённая атрибуция: весь разрыв, включая native
попытки, остаётся у следующего C# проекта. First относится к host.
Отдельная native стоимость не добавляется, чтобы не посчитать эти интервалы
дважды. Такая цена зависит от порядка загрузки; это ограничение переносимости
профиля, а калибровка проверяет последствия на другой стратегии.

## Markup

Detector сохранён: targets MarkupCompilePass1, MarkupCompilePass2,
GenerateTemporaryTargetAssembly, XamlPreCompile; task CompileXaml;
task command/build message с XamlCompiler.exe.

Обнаружены **30** markup проектов PowerToys. В реальном binlog есть признаки
XamlCompiler.exe и MarkupCompilePass1; остальные перечисленные признаки
не понадобились для обнаруженного набора и не заявляются наблюдёнными.
**FancyZonesEditor** имеет `target:MarkupCompilePass1`, **ColorPickerUI** —
`command:XamlCompiler.exe`/`message:XamlCompiler.exe`. Оба обязательных
потерявших documents проекта покрыты. В Files обнаружены **Files.App.Controls**
и **Files.App.UITests** по XamlCompiler.exe. Полный набор и evidence:
[summary](task-05.1-profile-retry/summary.json) и [profile](task-05.1-profile-retry/profile.json).

## Калибровка: обе направленные проверки

Форма модели прежняя: `h + c(N) * sum(w)` по замыканию. Для каждого направления
подгоняются h и три c(N) одновременно, **42 измерения времени групп**
(3 повтора × (2+4+8) групп). Минимизируется сумма квадратов относительных
ошибок групп; параметры неотрицательны, решение NNLS вычислено перебором
активных ограничений четырёх параметров. Проверочные данные не участвуют
в подгонке. Перенастройки после просмотра held-out ошибок нет.

- Training **round-robin**: h **6014,898 мс**;
  c(2) **0,958730**, c(4) **0,985709**, c(8) **1,289455**.
- Training **cluster**: h **18,380 мс**;
  c(2) **1,086902**, c(4) **1,284946**, c(8) **1,410108**.

Для каждой проверочной комбинации далее указаны медиана абсолютной
относительной ошибки групп / медиана абсолютной ошибки wall (max групп):

- round-robin → cluster N=2: **90,39 % / 5,18 %**, failed.
- round-robin → cluster N=4: **358,97 % / 7,56 %**, failed.
- round-robin → cluster N=8: **612,12 % / 36,03 %**, failed.
- cluster → round-robin N=2: **8,08 % / 11,60 %**, failed.
- cluster → round-robin N=4: **2,86 % / 1,06 %**, passed.
- cluster → round-robin N=8: **20,88 % / 12,19 %**, failed.

В gate дополнительно требуется ошибка wall ≤10 % **в каждом** проверочном
повторе; по каждому повтору и каждой группе значения сохранены в summary.
Один passed N=4 не делает модель годной. Причина большого host расхождения:
constant h, подобранный на крупных round-robin группах, не переносится на
малые/пустые cluster группы. c(N) по запрошенному N также не описывает одинаково
N занятых групп и cluster, где почти вся работа остаётся в одной группе.
Небольшие coefficients ниже 1 допустимы как эмпирическая подгонка, не
интерпретируются как причинный эффект конкуренции.

### Проверка реконструкции замыканий

Режим comparison-partitions использует **тот же код** разбиения, что task-04,
а замыкания — граф baseline-снимка. Graph profile и baseline совпадают.
Cluster counts совпали во всех 12 снимках. У round-robin обнаружено
расхождение в два дополнительных загруженных проекта на прогон:

- N=2: graph **164/164**, реально **165/165**.
- N=4: graph **116/120/92/93**, реально **117/121/92/93**.
- N=8: graph **68/71/46/51/74/81/66/67**, реально **69/71/46/51/74/82/66/67**.

Это не округление стоимости и не отсутствующий снимок. Сохранённые объединённые
snapshots не содержат identities проектов по каждой группе; определить эти
две дополнительные загрузки однозначно по ним нельзя. Они не добавлены
догадкой в веса. Подгонка по известным замыканиям всё равно проверена против
фактически измеренных времён; вдобавок `closureReconstructionComplete=false`
блокирует признание модели годной. У Files известных N=2/4 реконструкция
совпадает с реальными counts. Причина дополнительных загрузок PowerToys
остаётся отдельным открытым вопросом, не разрешением на unsafe plan.

## Диагностическая симуляция и gate

**Все следующие числа недопустимы как валидированный performance-прогноз.**
Они сохранены для сравнения алгоритмов и воспроизводимости отрицательного
результата; `admissible=false`, планы — `uncalibrated-model`. Годной модели
нет, поэтому требуемая симуляция для разрешения шага 2 не состоялась.

Ниже — RR-trained модель: wall; speedup к медиане baseline task-04
**41,711 с**; повторы проектов; размер общего ядра; число повторяемых markup:

- plan N=2: **27,813 с; 1,500×; 31; 31; 0**.
- plan N=4: **23,292 с; 1,791×; 71; 46; 0**.
- plan N=8: **28,617 с; 1,458×; 121; 48; 0**.
- round-robin N=2: **32,030 с; 1,302×; 80; 80; 11**.
- round-robin N=4: **26,962 с; 1,547×; 173; 96; 15**.
- round-robin N=8: **25,526 с; 1,634×; 276; 100; 16**.
- cluster N=2: **42,150 с; 0,990×; 2; 2; 0**.
- cluster N=4: **43,167 с; 0,966×; 2; 2; 0**.
- cluster N=8: **54,615 с; 0,764×; 2; 2; 0**.

RR repeats выше — graph-предсказание; реальные task-04 repeats
82/175/278 отличаются на те же две загрузки. Полные списки повторяющихся markup
для каждой стратегии и обе версии модели — в summary. В планах проверены
полное покрытие 248 ключей и отсутствие markup в нескольких замыканиях.
Алгоритм task-05, включая выбор минимальной итоговой стоимости группы,
не менялся. Files-планы для шага 2 не создавались: общий gate закрыт.

Доля стоимости общего ядра в сумме уникальных w: plan N=2/4/8 —
**17,90 % / 22,10 % / 22,55 %**; round-robin —
**40,47 % / 47,15 % / 49,20 %**; cluster — около **5,13 %**.
Общее ядро меньше у plan, но это само по себе не даёт нужного ускорения.

Порог шага 2 — **18770,124 мс**. Для N≤4 даже RR-trained диагностические
plan-прогнозы выше него; cluster-trained — также: N=2 **24,730 с**,
N=4 **22,541 с**. Два обязательных условия gate ложны:
**modelPassed=false**, **predictionPassed=false**.
`step2Authorized=false`, новых parallel loads **0**.

## Эквивалентность и validation

Ordinary snapshots обоих свежих profile-load сравнены тем же `compare.py`
с baseline task-04 по всем полям проектов и множеству diagnostics.
**Строгих различий 0**, baseline 1/2 task-04 также совпали. Нормализация
ProjectReferences для извлечения обезличенного graph не скрыла различия:
rawDifferenceCounts тоже пуст. Эта проверка не утверждает эквивалентность
не выполненной parallel-стратегии.

- Standalone spike: MCP **Release build passed**, финальный build warnings **0**.
  Один промежуточный no-incremental build дал MSB3061 для DLL, открытых MCP
  decompiler; сборка прошла. Финальный обычный Release build — без warnings.
- `check-profile`, в том числе на Release binary: **passed**. Реальные
  synthetic binlog созданы публичным BinaryLogger; формат повышен на 1,
  неизвестная length-prefixed запись добавлена перед EOF. Проверены новый
  пустой формат, empty recoverable inventory, failed с ErrorType/RecordKind
  при наличии проектов, failed на перекрытии, first+w=80 мс на последовательности,
  missing/negative/NaN weights, прежние markup/core/cycles/coverage проверки.
- `check-merge`: **passed**, прежние проверки ownership/TFM/IDs/aliases/
  EmbedInteropTypes/opaque native targets сохранены.
- Скрипт калибровки: **passed** как выполнение анализа; это не passed модели.
  Прочитаны 44 входа, сопоставлены graph/keys, сохранены все count mismatches,
  обе held-out проверки, девять диагностических сравнений и оба направления
  plan-прогноза. Пропусков failed/timeout/нулевых замеров нет.
- `RoslynMcpServer.sln`, MCP **Release build passed**. Четыре прежних warnings:
  CS8603 ×2 NuGetFallbackAssemblyResolver, CS8601 HostSession,
  xUnit1031 WpfTemporaryProjectCallbackTests.
- Полный SourceStructure, Release: **123 passed / 0 failed**. Точные
  identities/kinds структурных ограничений и их expected inventories не менялись.
- Полный main `Category!=AnalyzerLifecycle`, Release: **1362 passed / 1 failed**,
  всего **1363**, skipped **0**. Единственный сбой:
  `RoslynMcpServer.Tests.WorkspaceLoadCache.DependencyPartialProofTests.Framework_selection_matches_installed_pack_files_without_loading_analyzers`.
  Expected только `framework-task-input-closure-unproven`; actual дополнительно
  `framework-selection-profile-unproven`. Имя и assert совпадают с task-04/task-05;
  это существующий SDK-specific тест незакоммиченной соседней работы. Он не
  исправлялся и не исключался из полного прогона.
- AnalyzerLifecycle не запускался: standalone spike не изменяет production
  load/prepare/publication, analyzer lifecycle, lifecycle host или его конфигурацию.
- Проверка исполнителем: один раунд. Независимого reviewer/subagent не было.
  Проверены fail-closed replay, отсутствие двойного начисления вложенных builds,
  отдельный first, неизменность partition/markup ограничений и отсутствие
  допуска к шагу 2 после failed калибровки.

[Validation summary](task-05.1-profile-retry/validation.json),
[хэши исходников](task-05.1-profile-retry/source-sha256.json),
[хэши тяжёлых входов](task-05.1-profile-retry/artifact-sha256.json).
Возвращённый отчёт MCP main сохранён в artifacts `main-validation.log`.
Формальная приёмка code/tests **pending** единого зелёного main CI.
Статусы task-05, task-05.1 и карты эпохи обновлены с этим ограничением;
production scope, эпоха целиком и public activation не приняты.
