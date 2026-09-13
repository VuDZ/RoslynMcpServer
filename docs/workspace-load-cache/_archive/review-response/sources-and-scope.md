# Основания и границы проверки

Дата: 2026-09-12. Проверяемая база: commit `9867318ddb5294ce144bf024b9a61a1a2e3814c3`, версия исходников 1.3.21, Roslyn 5.9.0 по [RoslynMcpServer.csproj](../../../../RoslynMcpServer.csproj). Версия работающего MCP в этом проходе не проверялась. Указанные в review 1.3.20 — свидетельство автора review о его окружении, не версия этой проверки.

Прочитаны полностью все пять файлов первоначального плана [`proposal-v1/`](../proposal-v1/README.md), все девять файлов доарбитражной спецификации [`proposal-pre-arb/`](../proposal-pre-arb/README.md) и все девять файлов [`review/`](../review/README.md): **50 замечаний**. Также прочитаны `docs/ARCHITECTURE.md`, `AGENTS.md.sample`, относящиеся к выводам принятые решения и участки исходников, перечисленные ниже. Корневой `AGENTS.md` и отдельный документ исходных требований к кэшу не обнаружены. `AGENTS.md.sample` — политика для потребителей продукта, а не автоматически установленный AGENTS этой рабочей копии.

Первичного пользовательского задания на проектирование кэша в доступных материалах нет. Поэтому первоначальный план используется как документальный источник целей и ранее зафиксированных ограничений, **но не объявляется дословной записью требований пользователя**. Текущая инструкция пользователя на этот проход имеет приоритет: только оценка, без изменения планов, review и кода. Мой прежний ответ, удалённый по просьбе пользователя, источником требований не является.

## Прослеживаемые исходные цели и ограничения

Обозначения O1–O8 введены только для ссылок внутри этого ответа; это не существовавшие requirement IDs и не новые требования.

| Ссылка | Документ и точный раздел | Что установлено; предел вывода |
|---|---|---|
| O1 | [v1 README](../proposal-v1/README.md), «Зачем»; [v1 Epoch 3](../proposal-v1/epoch-3-evaluation-snapshot.md), «Mission» | Боль — новый PID на 100+ проектах, монорепа; повторное использование при неизменном графе и дереве. Частота рестартов после правок не задана. |
| O2 | v1 README, «Фиксированные решения» | Без git/ast для инвалидации, VS cache и внутренних Roslyn storage; без сериализации Solution/Compilation/исходных текстов; сомнение → miss. Merkle и stat shortcut там тоже зафиксированы, но являются решениями, а не автоматически неизменяемыми пользовательскими требованиями. |
| O3 | v1 README, «Фиксированные решения»; v1 Epoch 3, «Инвалидация — без VCS», шаги 1–3 | Обход конуса, prune, не полный монорепозиторий ради удобства; значимые входы нельзя терять ради бюджета. |
| O4 | v1 README, последний пункт «Фиксированных решений»; v1 Epoch 3, «Живая сессия: git pull при работающем MCP», пункты 1–4 | Sync при живом MCP должен отражаться в RAM/индексе на следующем semantic call; overflow требует полного пересчёта состава, не только известных документов. Это обязательная цель первоначального плана. |
| O5 | v1 Epoch 3, «Инвалидация — без VCS», шаг 6; «Матрица», строка «Sync: новые .cs» | Предложено повторное использование графа после изменения источников/членства без DTB. V2 переносит это за пределы Epoch 2. Право считать такое сужение принятым не подтверждено. |
| O6 | v1 README, «Фиксированные решения»; v1 Epoch 3, «Сборка workspace на hit» | Overlay остаётся в памяти; inner TFM, не outer; исходный план уже рассматривал AdhocWorkspace/ProjectInfo. |
| O7 | [v1 Epoch 1](../proposal-v1/epoch-1-msbuild-fast-open.md), «Mission/Decisions»; [v1 Epoch 2](../proposal-v1/epoch-2-lazy-project-load.md), «Mission/Decisions» | Metadata и частичная загрузка были самостоятельными направлениями ускорения, с opt-in и явной неполнотой; CLI scope не сужается автоматически. Обязательность их порядка относительно v2 нуждается в подтверждении. |
| O8 | v1 README, «Правило: лучше медленный DTB…»; [ARCHITECTURE](../../../ARCHITECTURE.md), «Editing and refactoring», «Architectural constraints» | Корректность семантики и сохранённых данных важнее быстрого ложного успеха; изменения проектного графа требуют корректного reload; сервер не является sandbox. |

V2 прямо обозначена самостоятельной proposed-альтернативой, не отменяющей v1. Её first-semantic критерий полезен, но факт его записи не доказывает согласование отказа от O4/O5/O7. Эта неоднозначность вынесена в арбитраж A-01 в summary.

## Принятые архитектурные ограничения

| Обозначение | Источник | Ограничение |
|---|---|---|
| A-LOAD | [U-ARB-04 decision](../../../analyzer-shadow-copy-improvements/v2/u-arb-04-decision.md), «Решение/Граница реализации»; [atomic-load-prepare](../../../analyzer-shadow-copy-improvements/v2/u-arb-04-atomic-load-prepare.md), S1–S3 | Load/cache → prepare → gate → publication под одним захватом; production reader получает только published snapshot; under-lock путь без повторного semaphore. |
| A-STICKY | [U-ARB-05](../../../analyzer-shadow-copy-improvements/v2/u-arb-05-decision.md), «Решение» | Cached false/omitted сохраняют режим; новый key/reset — новая сессия. Overlay не включается автоматически. |
| A-WRITE | [Write boundary](../../../analyzer-shadow-copy-improvements/v2/epoch-4-workspace-write-boundary.md), E4-S1–S4; [v3 S2](../../../analyzer-shadow-copy-improvements/v3/s2-write-base-freshness.md), «Работа» | Preflight до записи, проверка базы, exact inverse, отчёт о частичном сохранении; атомарного rollback всех файлов нет; merge/MVCC не вводятся. |
| A-ADMISSION | [v3 S3](../../../analyzer-shadow-copy-improvements/v3/s3-persistent-publication-state.md), «Работа»; [v3 S4](../../../analyzer-shadow-copy-improvements/v3/s4-provenance-failure-gate.md), «Работа» | Ban/unavailable переживает edit/flush; непригодный capture не даёт raw opt-in semantics; обычная публикация не делает analyzer I/O. |
| A-PROVENANCE | [F-09 capture design](../../../analyzer-shadow-copy-improvements/v2/epoch-5-f09-capture-design.md), «Выбранный канал», «Ключ item…», «Lifetime», «Отказы» | Binlog того же DTB уже используется для analyzer provenance; доказательство привязано к loadSessionId и ProjectId и не переносится между сессиями. Временные binlog могут содержать секреты, удаляются. Это не полный evaluation dependency tracker. |
| A-LOADER | [ARCHITECTURE](../../../ARCHITECTURE.md), «Workspace lifecycle»; [v3 README](../../../analyzer-shadow-copy-improvements/v3/README.md), «Общие ограничения» | Process-lifetime loader, main-only, restart-required для same-identity updates; reset не выгружает CLR. |

Это ограничения текущего продукта, а не запрет любых будущих изменений. Их изменение потребует отдельного обоснования совместимости; v2 не может отменять их неявно.

## Проверенные участки кода и границы evidence

- [SolutionManager](../../../../Services/SolutionManager.cs): `LoadAndPrepareAsync`, `FindDocumentAsync`, `LoadCoreAsync`, `HasBlockingLoadFailure`, `FlushDirtyDocumentsUnderLockAsync`, `ApplyWorkspaceWriteUnderLockAsync`, `StartDiskWatcherUnderLock`, `IsSelfWriteSuppressed`.
- [MsBuildWorkspaceProperties](../../../../Services/MsBuildWorkspaceProperties.cs:33) и [DotNetConfigurationArguments](../../../../Services/DotNetConfigurationArguments.cs:15): null не заменяется на Debug.
- [WorkspaceDocumentDiskSync](../../../../Services/WorkspaceDocumentDiskSync.cs:79): первое membership, чтение через ReadAllTextAsync, AddDocument по ближайшему каталогу. Это статические факты; вызванная ими порча файлов здесь не воспроизводилась.
- [ProjectTools](../../../../Tools/ProjectTools.cs:23): `rename_project` использует `ProjectRenameHelper.Apply`, затем clear, не Workspace.TryApplyChanges.
- [AnalyzerProvenanceCaptureService](../../../../Services/AnalyzerProvenanceCaptureService.cs:128) и [gate](../../../../Services/AnalyzerProvenanceCaptureGate.cs): session-bound snapshot; logger `ProjectImports=None`.
- [SourceGeneratorOracle](../../../../RoslynMcpServer.LifecycleTestHost/SourceGeneratorOracle.cs:14): exact marker и GetSourceGeneratedDocumentsAsync уже есть; отсутствие generated text пока не делает `Success` false.

Проверены также публичные первичные источники: [AdhocWorkspace](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.adhocworkspace?view=roslyn-dotnet-4.13.0), [SourceText implementation](https://source.dot.net/Microsoft.CodeAnalysis/Text/SourceText.cs.html), [MSBuild logging](https://learn.microsoft.com/en-us/visualstudio/msbuild/obtaining-build-logs-with-msbuild?view=vs-2022). Они подтверждают общие свойства API, но не заменяют эксперимент на закреплённом Roslyn 5.9.0.

Новые runtime-тесты, DTB/hydration эксперименты, нагрузочные замеры и security repro не выполнялись. Предложения ниже не являются реализацией или принятой финальной архитектурой. Изменения DTO предполагают будущую schema/producer version с miss для несовместимого payload; миграция ещё не выпущенного v2 cache не требуется. Проверки исходных SHA-256 отделены от проверки работоспособности будущей реализации.
