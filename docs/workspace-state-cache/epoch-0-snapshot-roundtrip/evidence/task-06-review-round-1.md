# E0/task-06 — независимое ревью, round 1

Дата: **2026-10-08**, Europe/Moscow. Рецензент: Grok 4.7, отдельная сессия, не исполнитель
проверяемого scope. Фактический reasoning — режим этой сессии; отдельный runtime
effort id не выставлен.
Итог: **ACCEPT**. Открытых must-fix findings: **0**. Исправлений после ревью: **0**.
Accepted scope этой задачей не объявлен: приёмка, bookkeeping статуса `accepted`
и commit остаются за координатором.

## Проверенный scope

Прочитаны task-06, spec эпохи, README эпохи, execution, P-001/P-002/P-010,
H-001/H-002 в тексте задачи, task-06-roundtrip.md, validation/processes JSON.
Сверены новые test-only файлы и вызываемый production path без его изменения:

- `RoslynMcpServer.Tests/WorkspaceStateCache/CrossProcessRoundtripTests.cs`
- `RoslynMcpServer.Tests/WorkspaceStateCache/RoundtripProcess.cs`
- `RoslynMcpServer.Tests/WorkspaceStateCache/RoundtripObservation.cs`
- `RoslynMcpServer.Tests/WorkspaceStateCache/RoundtripFixture.cs`
- `OrdinaryWorkspaceCapture.CaptureAsync` / `BindInstancesAsync` / `RequireUniqueProjects`
- `IsolatedWorkspaceHydrator.HydrateAsync` / `DecodeText` / refusal message
- `SnapshotSigningProfile.UnsupportedReason`
- `CapabilityOperationGate` (принятый isolated gate task-08)
- `WorkspaceSnapshotCodec.ValidateEvaluated`

HEAD остаётся `5678c2b1ce36f9abbc7ff93a541e299b00a1e5c5`. Production C#,
`SolutionManager` и version не менялись. Посторонние dirty WorkspaceLoadCache/tooling
файлы не входят в verdict. Требования не переписывались.

Повторный `run_specific_test` класса `CrossProcessRoundtripTests`, Release,
restore пропущен, timeout 600 с: **6/6 passed, 0 failed**. Четыре новых каталога
`TestResults/task-06/` от этого прогона (parent PID 80964): `real`, `edit-utf8`,
`edit-utf8-bom`, `edit-utf16-bom`. В каждом child TRX **total=1 / passed=1 /
failed=0 / notExecuted=0**, child PID отличается от parent и от остальных.
Полный main suite **not re-run**: авторская validation на том же HEAD остаётся
Release build passed и `Category!=AnalyzerLifecycle` **1358/1358**. AnalyzerLifecycle
**not-run / not required**.

## Контракт, который держится

Ordinary capture идёт через public `MSBuildProjectLoader` и
`OrdinaryWorkspaceCapture.CaptureAsync`. Expected semantic facts заданы в child
независимо от DTO: `RoslynMcpPlugin.SamplePlugin` в `SamplePlugin.cs`,
`IRoslynMcpPlugin` из assembly `RoslynMcpServer`, metadata `JToken`, отсутствие
compiler errors, `NullableContextOptions.Enable`, `OptimizationLevel.Debug`.
Parent dispose ordinary workspace до `dotnet vstest`. Child читает envelope с диска
и не вызывает capture. `MsBuildBootstrapper.Register` не открывает solution.

Наблюдение ordinary solution и hydrated solution сравнивается целиком: проекты,
документы, folders, source kind, text hash, encoding/preamble/checksum, parse и
compilation option facts, directed edges, metadata kind/aliases/EmbedInteropTypes,
analyzer paths. Свидетель options — принятый public Equals с тремя host-owned
identity normalizations. Hidden strong-name state и external XML за сохранённые
не выдаются. Свежие ProjectId/DocumentId, включая additional и analyzer-config,
проверяются на real control. `CurrentSolution` host пуст. Role, RoleSource,
ProducerSource и producer binding сверяются с snapshot; presence берётся с диска.

Три уровня properties: raw requested и pre-open globals — null на capture и ещё
раз на decoded snapshot в новом PID. Evaluated Configuration `Debug`, TFM
`net10.0` и пустые unknown — на snapshot до encode; decode прогоняет принятый
codec witness (source, равенство key и evaluated values, Configuration `Debug`).
Edit-child дополнительно видит `configuration=Debug tfm=net10.0` в refusal
decoded key.

H-001 отдельно: source comment `Portable graph facts only` на base compilation
`RoslynMcpServer`; `JToken.GetDocumentationCommentXml` пуст. Operation gate
отказывает `metadata-documentation` и `documentation-analyzer` до callback.
H-002 отдельно: ordinary capture отказывает `signing-dependent` и
`signing-state-unknown` без snapshot; в child текущий source с `AssemblyKeyFile`
и неразрешимым attribute отказывает hydrate до возврата session
(`stage=hydrate`, reason, `route=refusal`, Debug/net10.0). Images после отказа
недоступны (`ObjectDisposedException`), workspace notification одна. Emit/sign
и `unknown-write` идут через принятый isolated gate и не исполняют callback.
Это не production fallback и не E1 log integration; отчёт так и говорит.

Generator: отказ `generator-dependent-scope-unsupported` возникает только когда
`GetSourceGeneratedDocumentsAsync` непуст, после успешного SDK build
`JsonSerializerContext`. Multi-TFM `net10.0;net10.0-windows` даёт
`ambiguous-project-instance` только из `RequireUniqueProjects` при двух
канонических путях. Оба отказа без snapshot и без подмены reason.

Scoped edit: один существующий `Shared.cs`, два linked source и один additional.
Три child PID для UTF-8 без BOM, UTF-8 BOM и UTF-16 LE BOM. До записи
`SnapshotSigningProfile` пуст и constant равен 19 на compilation каждого owner.
Один physical write сохраняет preamble загруженной кодировки; точные байты,
`Grüße 世界`, memberships и `CheckOverflow` держатся. Повторный hydrate того же
envelope читает новые байты. Принятые байты возвращаются в `finally`.
Production write boundary не вызывается.

Ноль тестов не проходит: child TRX требует total=1, passed=1, notExecuted=0 и
ненулевой exit, receipt PID другой. Каталог evidence — новый GUID.
Production cache не включён. Benchmark и E1 по-прежнему not-run.
