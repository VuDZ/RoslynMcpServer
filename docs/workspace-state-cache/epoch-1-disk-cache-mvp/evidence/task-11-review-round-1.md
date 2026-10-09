# E1/task-11 и task-00 — независимое ревью, round 1

Дата: **2026-10-09**, Europe/Moscow. Рецензент: **Codex, отдельный reviewer subagent,
унаследованная модель сессии; точный runtime model ID и reasoning недоступны**.
Рецензент не является автором проверенных C# или отчётов. Проверен HEAD
`da557044333e7c0a80ffa8d7ca25462a22be51cd` плюс три новых test-only файла и evidence.
Исходные dirty WorkspaceLoadCache/tooling файлы вне scope; commits не выполнялись.

Итог: **ACCEPT bounded partial S-002 experiment и task-00 blocked analysis**.
**Task-00 implementation BLOCKED, не accepted/completed.** Supported real manifest,
executable manifest DTO/capture adapter, reusable envelope и приёмка E1 отсутствуют.
Открытых must-fix findings: **0**. Исправленных замечаний в round 1: **1**.
Приёмка test-only результата требует финальной validation координатора;
[validation report](task-11-validation.md) является её отдельным источником.

## Scope и проверка

Прочитаны task-00/task-11, spec, README эпохи, общие execution/README/benchmark/spikes
и accepted E0/task-07 output. Полный [code style](../../../code-style.md) прочитан
до C# style review; независимых style замечаний к трём новым файлам нет.

Проверены actual новые `RawImportEvidenceCapture`, `RawImportEvidenceFixture`,
`RawImportEvidenceTests`, [final raw report](task-11-raw-evidence.md),
[S-002 blocker evidence](task-11-raw-evidence.md), controlled expected inventory,
controlled/real replay и workspace JSON, result, copy hashes и локальная
Roslyn decompiler evidence. Production/default/activation изменения в owned scope
не обнаружены. No additional log policy выбран автоматически.

Независимый recheck сохранённых JSON подтвердил:

- Controlled: 224 import events; real: 228; по одному EvaluationFinished и четыре
  ProjectStarted в каждом log. Imports bound к evaluation 4 / submission 0;
  root evaluation 3 не покрыта. Imported ProjectFile является importer, включая
  shared.props→nested.props; consumer берётся через evaluation context.
- Nested, conditional, shared, external и false-condition absent controlled paths
  присутствуют в Library evaluation. Null ImportedProjectFile сам по себе не
  трактуется как универсальное known-absence proof. Required lock/config closure
  отсутствует; MSBuildAllProjects не считается exhaustive import inventory.
- Assets TaskInput присутствует для обоих consumers и относится к actual build
  contexts 4/0 и 13/1. Это отдельное положительное path evidence, не all-input proof.
- Dependent build evaluation 9 / submission 1 не имеет соответствующих import/
  evaluation-finished events. Совпадение physical project path и instance number
  не связывает её с evaluation 4. Final reports сохраняют этот дополнительный gap.
- Real workspace содержит два projects, Diagnostics=[], SamplePluginSymbolFound=true.
  Все **234** entries real-copy-hashes.json независимо сверены с текущими repo
  non-obj bytes: **0 mismatches**. Actual test также проверяет owned bytes после open.
  Этот ordinary positive не является supported manifest positive или P-004 proof.
- Negative transformations удаляют import source/evaluation binding и проверяют
  невозможность root наследовать dependent imports. Path-level MissingImportConsumers
  не заявлен all-instance admission. Result остаётся partial/blocked/RealSupportedManifest=false.

Same request channel проверен по production AnalyzerProvenanceCaptureService:
MSBuildWorkspace open с BinaryLogger filename + ProjectImports=None и caller Normal.
Installed Roslyn loader передаёт filename/parameters, не caller verbosity;
effective remote verbosity явно unmeasured. Missing root evaluation объясняется
BuildHost ProjectCollection без evaluation loggers; sequential overwrite не доказан.
P-004 consumed-byte generation binding остаётся отдельным unproven основанием.

## T11-R1-001 — CLOSED в round 1

Во время начального чтения raw evidence, пока автор завершал отчёт, reviewer выявил
необходимость явно отделить dependent evaluation **9 / submission 1** от import
evaluation **4 / submission 0**, а также обозначить physical-path collapse в
MissingImportConsumers. Иначе описание «видно у dependent consumer» могло бы
ошибочно читаться как proof всех consumed instances.

Уточнение передано до verdict; автор добавил оба ограничения в final S-002 report
и task-00 blocker report. Reviewer повторно прочитал изменённые sections и сверил
их с сохранёнными actual contexts. Замечание исправлено без C# изменений и без
изменения negative outcome. Это один подтверждённый documentation finding,
включённый в статистику round 1; отдельный round 2 не объявляется.

## Validation и предел решения

Reviewer не запускал повторную build/main suite: final validation принадлежит
координатору. Координатор сообщил Release solution build passed и main
`Category!=AnalyzerLifecycle` **1360 total / 1360 passed / 0 failed** после последней
C# правки; actual runner evidence и skip accounting фиксируются в validation report.
Focused scenario имеет 1/1 pass по implementation report. SourceStructure входит
в main suite; production C# и structural limit inventory не менялись.
AnalyzerLifecycle дополнительно не требуется для этих test-only additions:
production analyzer/load/prepare/publication, lifecycle host/config не менялись.

Task-11 может быть принят только как завершённый bounded partial experiment с
manifest readiness blocked. Task-00 report достоверно фиксирует dependency blocker;
inventory/P-004 proposals остаются предложениями, producer не внедрён и не доказан.
Недостаточный S-002 не разрешает положительный manifest, envelope или downstream
activation. Координатор сохраняет task-00 blocked, обновляет status/index/reports
и добавляет финальные validation links без изменения требований.

## Identity проверенного C#

- RawImportEvidenceCapture.cs: `29C52C6652218621C395DC0A0DFD7BEBA4D9B30E0545FF858C1522373E68D8C1`.
- RawImportEvidenceFixture.cs: `2C37EA43630B38FB57DEFFFDBAE0318D1F09C9BD9C8E19642BDCFFEC71E91682`.
- RawImportEvidenceTests.cs: `6ED9C1F3FD8737FF526AE4879F4B2889205CE576E5EB7767EBFCAC5199101EBD`.
