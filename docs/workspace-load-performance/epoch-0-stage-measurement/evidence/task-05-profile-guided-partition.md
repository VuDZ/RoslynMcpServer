# D/E0/task-05 — Исторический отчёт: профиль недоступен

**Текущий статус, 2026-10-10:** blocker снят в
[task-05.1](task-05.1-profile-retry.md). Свежие профили получены, обе направленные
калибровки не проходят пороги, диагностические планы не проходят gate: **no-go**,
шаг 2 не выполнялся. Scope — standalone spike и evidence, без production;
формальная приёмка code/tests pending единого зелёного main CI
(1362 passed / 1 прежний SDK-specific failed). Далее сохранены первоначальные
наблюдения и ревью; они не описывают итог повторного эксперимента.

Дата: **2026-10-10**. Исполнитель: **Codex (GPT-6)**. Статус: **шаг 1 завершён
предусмотренным ранним выходом при чтении профиля; формальная приёмка кода
pending из-за незелёного полного main CI**.

**Вердикт — no-go на этапе получения профиля.** PowerToys записывает второй
binlog версии 27, установленный `BinaryLogReplayEventSource` поддерживает
максимум 26. Files читается, но интервалы evaluation отсутствуют для всех
11 проектов. Годная модель не получена, gate для шага 2 закрыт. Это результат
снятия профиля, а не доказательство невозможности ускорения правильной разбивкой.

Выполненный scope: `profile`, `plan`, проверки планировщика, ordinary загрузки,
replay и evidence в standalone `spikes/ParallelLoad`. Production, корневой
csproj, solution, CI, версия и CHANGELOG этой задачей не изменены. Production
scope не принят и не активирован. Калибровка, симуляция и условный шаг 2 не
начаты; `parallel --strategy plan` не реализован, поскольку gate не пройден.

## Ревью, 2026-10-10 (Claude Opus 5.5)

Реализация аккуратна: отказ вместо нулевой симуляции, `null` вместо 0,
проверка ordinary-эквивалентности, отделение предварительных прогонов.
**Вердикт переквалифицирован: blocked (инструмент), а не no-go.** Гипотеза
не проверялась, и оба блокера снимаются без изменения порогов:

1. **Binlog v27 читается.** `BinaryLogReplayEventSource` (SDK 10.0.401) имеет
   публичные `AllowForwardCompatibility` и обязательное при нём событие
   `RecoverableReadError`. С ними `powertoys-restored/profile-binlogs/capture-2.binlog`
   читается без recoverable errors: 6 событий — `BuildStarted`, три
   `BuildMessage`, `LoggersRegistered`, `BuildFinished`, ни одного проекта.
   Это пустая сессия BuildHost-net472 (MSBuild VS 18.10, формат 27), которая
   обслуживает только vcxproj. Основной `capture-1.binlog` (v26) в том же режиме
   даёт 407 904 события, включая 248 `BuildSubmissionStarted` и 5729 пар
   `ProjectStarted`/`ProjectFinished`.
2. **Evaluation верхнего уровня в binlog не видна.** В Files-binlog нет ни
   одного `ProjectEvaluationStarted` при 11 загрузках верхнего уровня и 44
   вложенных; 1285 evaluation в PowerToys, по-видимому, относятся к вложенным
   вызовам с другими global properties. Требовать `EvaluationMs` от каждого
   проекта нельзя. Полная стоимость проекта в одном BuildHost берётся по
   разрывам: Roslyn грузит проекты последовательно, поэтому
   `w(p) = end(build p) − end(предыдущий top-level build того же узла)`
   содержит evaluation, build и обработку на стороне Roslyn. Первый проект
   узла несёт прогрев и старт — это вход для оценки `h`.

**Побочная находка в production.** Provenance capture сервера использует тот
же reader без forward compatibility. Во всех прокси-замерах PowerToys
(task-00) лог сервера пишет `Status="Incomplete", Files=2, ConfirmedBindings=0`,
а причина (`replay_failed:1:NotSupportedException`) в строку лога не попадает.
`AnalyzerProvenanceCaptureGate` отвечает на `Incomplete` отказом
`provenance-capture-incomplete`, то есть `load_workspace` с
`shadowCopyInSolutionAnalyzers=true` на таком решении не работает. Условие —
запуск BuildHost-net472 (vcxproj, не-SDK проект) при MSBuild VS новее MSBuild SDK.
Обычная загрузка не затронута.

Проверка выполнена скриптом `<repo>/artifacts/parallel-load/replay-v27.ps1`
(pwsh, сборки MSBuild из `<SDK>/10.0.401`), без изменения spike.

## Входы, окружение и протокол

- [Задача](../task-05-profile-guided-partition-hi.md), [инструкция spike](../../../../spikes/ParallelLoad/README.md).
- Все 44 снимка task-04 доступны в `<repo>/artifacts/parallel-load/task-04-evidence/`.
- HEAD PowerToys — `7b6789c576b304c7e1eda8ce571cc1b0afd280ab`; Files —
  `1994d9a4269f5144c70bc67d6a48de4a20616912`. Они проверены заново.
- Установленные SDK — 9.0.314, 10.0.201, 10.0.300, 10.0.401: тот же inventory,
  что в task-04. Kaspersky `productState=266240` (две записи) и Windows Defender
  `productState=393472`: значения совпадают с сохранённым environment task-04.
  Антивирус и cache ОС не перенастраивались.
- VS 2026 Enterprise MSBuild.dll: FileVersion `18.10.1.42706`, ProductVersion
  `18.10.1-1.26427.6+3cd27c13eb422ceea9de6fb84d12684e3bcf3188`.
  Task-04 не сохранила fingerprint этой DLL, поэтому отсутствие её обновления
  отдельно подтвердить нельзя.
- **Уточнение отчёта task-04:** там указана регистрация VS, но поле `Registration`
  самих baseline-снимков обоих решений указывает `<SDK>/10.0.401`. В task-05
  наблюдается тот же путь. При сравнении использованы снимки, не ошибочное
  описание. Алгоритм регистрации и global properties spike не изменены.
- Финальные профили сняты после повторного restore обоих решений через
  `<VS>/MSBuild/Current/Bin/amd64/MSBuild.exe`, `-t:restore`, Debug|x64;
  оба exit code **0**, логи — в artifacts task-05. Решения не собирались.
- Каждый `profile` — новый процесс через MCP `run_dotnet_run`. Финальные outputs:
  `<repo>/artifacts/parallel-load/task-05-evidence/{powertoys,files}-restored/profile.json`.
  Предварительные проверки сохраняются отдельно в `{powertoys,files}/` и
  `{powertoys,files}-final/`. Это проверки получения профиля, не матрица n=3
  и не измерение speedup. Прогрев/n=3 относятся к не начатой калибровке/шагу 2.

## Профиль и причина остановки

Reader в процессе spike — Microsoft.Build **18.9.11**,
`18.9.11+e34a38d2ae1fc26406a317517196e55c68ff83ab`. AssemblyVersion сам по себе
не идентифицирует выпуск MSBuild, поэтому в профиле сохранён InformationalVersion.

PowerToys: ordinary load **43,026 с**, 248 уникальных проектов, 3570 событий
diagnostics, API load `ok`. Основной `capture-1.binlog` — версия **26**, 8 187 531
байт; второй `capture-2.binlog` — версия **27**, 660 байт. Версии дополнительно
прочитаны из заголовков распакованного gzip. Replay второго файла выдаёт:

```text
NotSupportedException: The log file format version is 27 with minimum required reader version 18, whereas this version of MSBuild only supports versions up to 26.
```

Маленький второй файл не проигнорирован: без успешного replay нельзя утверждать,
что он не содержит существенных событий. [profile.json](task-05-profile-guided-partition/profile.json)
имеет `Status=failed`, точную причину, `Projects=[]`, `Model=null`; exit code **2**.
Пустой массив не означает, что решение не содержит markup. Полный набор markup,
включение FancyZonesEditor/ColorPickerUI, распределение полной стоимости,
доля прогрева и стоимость общего ядра **недоступны**. Предварительные проверки
до повторного restore воспроизвели ту же ошибку.

Files: ordinary load **12,564 с**, 11 проектов, 15 событий diagnostics, API load
`ok`. Один binlog версии **26**, 338 926 байт, replay читается. Все 11 проектов
имеют собственный интервал top-level build и порядок внутри BuildHost, но у
всех `EvaluationMs=null`. [Files-профиль](task-05-profile-guided-partition/files-profile.json)
помечен `incomplete`, exit code **2**. Неполный профиль не подаётся в модель;
разницу wall и суммы build нельзя выдать за измеренный прогрев.

Для информации о частичном профиле Files: сумма top-level build **10,404 с**,
медиана проекта **59,692 мс**, минимум **32,329 мс**, максимум **5 225,109 мс**.
Это распределение только доступных build-интервалов, не полная стоимость `w(p)`.
Вложенные reference-query builds не начисляются повторно целевым проектам:
их время остаётся в интервале вызывающего top-level build.

Markup подтверждён для **Files.App.Controls** и **Files.App.UITests** событиями
`TaskCommandLineEventArgs` с `XamlCompiler.exe`; соответствующие сообщения
сохранены как отдельный вид evidence без самой командной строки. Среди
собранных положительных признаков Files нет `MarkupCompilePass1`,
`MarkupCompilePass2`, `GenerateTemporaryTargetAssembly`, `XamlPreCompile` или
`CompileXaml`. Эти имена остаются кандидатами detector, а не подтверждённым
полным списком для PowerToys. Полнота detector для PowerToys не принята.

Граф Files взят из public `ProjectReferences` snapshot загруженного solution.
Keys и target keys нормализованы одинаково, все targets разрешаются в профиле;
native missing targets в этот граф не включаются. JSON профилей не содержит
абсолютных путей машины/профиля пользователя. Тяжёлые binlog и ordinary snapshots
остались только в artifacts; [сводка](task-05-profile-guided-partition/comparison.json)
содержит размеры, версии и SHA-256, без содержимого документов.

## Калибровка, планы и gate

`h` и `c(N)` **не подгонялись**: полный исходный профиль отсутствует. Обе
направленные проверки round-robin → cluster и cluster → round-robin для
N=2/4/8 имеют статус **not-started**; median error и wall error недоступны.
Нельзя заявить прохождение ≤15 % / ≤10 %.

Для всех девяти сочетаний plan/round-robin/cluster × N=2/4/8 в comparison.json
сохранена явная строка `unavailable: PowerToys profile failed`: wall, speedup,
повторы, размер общего ядра и повторы markup — `null`, не 0. Реальные `plan-N.json`
не созданы. Gate ≤45 % baseline не проверен и не пройден. Новая baseline-медиана
для шага 2 не вычислялась; baseline task-04 не переиспользуется для вымышленного go.

Алгоритм режима `plan` реализован и проверен на синтетических графах. Корни,
делящие markup через свои замыкания, объединяются транзитивно; bundles идут по
убыванию стоимости, в группу с минимальной **итоговой** стоимостью после
добавления, уже включённые dependencies бесплатны. Это разрешённое отличие от
буквального минимального прироста: у несвязанных корней прирост одинаков во
всех пустых группах и постоянный tie-break складывал бы их в одну группу.
Циклы без входящего корня получают явный запрос. Проверяется полное покрытие
и отсутствие markup в нескольких замыканиях. Без калиброванной модели план
помечается `uncalibrated-model`; неполный реальный Files-профиль отвергается
до записи плана. Приёмка алгоритма по performance на PowerToys не заявляется.

## Эквивалентность ordinary результатов

Использована функция `differences` из того же `compare.py`, что в task-04:
сравниваются все поля проектов и множество diagnostics, не только количество.
У обоих решений **0 различий** между baseline 1/2 task-04, между первым
profile-load и финальным после restore, а также между baseline task-04 и
финальным profile-load. Нормализация natural variance не применялась.
Это проверка ordinary load, не эквивалентность не выполненной parallel-стратегии.

## Validation и проверка исполнителем

- MCP Release build standalone spike — **passed**, новых warnings нет.
- MCP `check-profile` — **passed**: транзитивная связность roots по markup,
  повторяемое ядро без markup, циклы без корней, полное покрытие, rejection
  failed/incomplete costs и отсутствующего target key; N=2/4/8.
- MCP `check-merge` — **passed**, прежние проверки task-04 сохранились.
- MCP CLI `plan` на неполном Files-профиле — ожидаемый **exit code 1**,
  причина `Planning requires a nonempty successful profile and positive workers.`;
  output не создан.
- Скрипт сводки — **passed**: замкнутость graph после redaction, отсутствие
  абсолютных путей в profile, чтение binlog headers и полный snapshot comparator.
- MCP Release build `RoslynMcpServer.sln` — **passed**, четыре прежних warnings:
  CS8603 (дважды) NuGetFallbackAssemblyResolver, CS8601 HostSession,
  xUnit1031 WpfTemporaryProjectCallbackTests.
- Полный SourceStructure в Release — **123 passed / 0 failed**. Его код и
  точные identities/kinds известных структурных ограничений не менялись.
- Полный main `Category!=AnalyzerLifecycle`, Release, SDK 10.0.401 —
  **1362 passed / 1 failed**, всего 1363. Сбой:
  `RoslynMcpServer.Tests.WorkspaceLoadCache.DependencyPartialProofTests.Framework_selection_matches_installed_pack_files_without_loading_analyzers`.
  Expected: `framework-task-input-closure-unproven`; actual дополнительно
  содержит `framework-selection-profile-unproven`. Это тот же по имени и
  assert сбой существующей незакоммиченной работы, что в task-04.
  Он не устранён изменением чужих тестов и не объявлен зелёным прогоном.
- AnalyzerLifecycle не требуется и не запускался: production load/prepare,
  analyzer/shadow-copy lifecycle, lifecycle host и его конфигурация не менялись.
- Проверка исполнителем — один раунд, независимого ревью нет. Проверены public
  API, сохранение binlog после disposal загрузчиков, явный отказ при missing
  timings, обезличивание graph и отказ планировщика вместо нулевой симуляции.

[Хэши исходников](task-05-profile-guided-partition/source-sha256.json),
[validation summary](task-05-profile-guided-partition/validation.json).
Формальная приёмка кода **pending** единого зелёного main CI. Экспериментальный
ранний no-go завершён; E0 в целом и production-оптимизации не приняты.

Для продолжения исследования нужен совместимый reader всех binlog и источник
полных evaluation-интервалов. После этого необходимо выполнить обе проверки
калибровки и симуляцию, прежде чем разрешать шаг 2. Этот отчёт не меняет их пороги,
не разрешает public activation и не создаёт E1.
