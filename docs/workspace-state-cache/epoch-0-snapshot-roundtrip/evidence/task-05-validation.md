# E0/task-05 — final validation и acceptance

Дата: **2026-10-08**, Europe/Moscow. Координатор: Codex (GPT-6; точный runtime
model ID недоступен). Принят scope: **isolated schema-v1 Debug base-query hydrate
adapter/session owner**, новые IDs, current source bytes, fresh owned metadata,
rebinding всех portable occurrences и H-001/H-002 границы.
Исполнитель — один subagent; два независимых reviewer sessions.
[Round 1](task-05-review-round-1.md): MUST-FIX T05-R1-001/P2.
[Round 2](task-05-review-round-2.md): **ACCEPT**, T05-R1-001 **closed / fixed**,
новых must-fix нет. Подтверждено **1** исправление после ревью.

## Final code и проверки

Исходная точка task-05: `c78764d`, включая принятое prerequisite task-04.
Actual artifact и все intermediate outcomes описаны в [implementation](task-05-hydrate.md).
После исправления casing coordinator повторил всю обязательную validation
против замороженных исходников. [Полные ответы Roslyn MCP](task-05-validation.json):

- `run_dotnet_build`, `RoslynMcpServer.sln`, Release, `noIncremental=true`: **passed**.
- `run_specific_test`, `IsolatedWorkspaceHydratorTests`, Release,
  `noBuild=true`, `noRestore=true`: **27/27 passed**.
- `run_specific_test`, `WorkspaceSnapshotCodecTests`, те же параметры:
  **25/25 passed**. Exposure существующего comparer не меняет codec semantics.
- `run_test_by_filter`, `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`,
  те же параметры: **123/123 passed**.
- `run_test_by_filter`, `Category!=AnalyzerLifecycle`, main
  `RoslynMcpServer.Tests.csproj`, Release, `noBuild=true`, `noRestore=true`,
  timeout 300 seconds: **1350/1350 passed**, **0 failed**.

Passed совпадает с total в каждом run; zero-test, skips и timeouts не наблюдались.
Сборки/test processes не пересекались с исправлениями кода. Reviewer round 2
выполнял read-only проверку; её verdict не заменяет coordinator CI.
Посторонние dirty WorkspaceLoadCache/tooling files присутствовали в рабочем дереве,
не ревьюились и не принимались в scope; main suite выполнялась по этому дереву.

До round-1 finding build/21 focused/123 SourceStructure/main **1344/1344** также
проходили: [предыдущие runner outputs](task-05-validation-round-1.json).
Эти результаты не подменяют повторные проверки окончательного исправления.
Исполнитель сначала воспроизвёл casing bug на **6/6 failed** controls, затем
получил **27/27** после fix; все ранние failures сохранены в implementation report.
Первоначальный existing editorconfig failure при проверке task-04 отдельно
сохранён в [prerequisite validation](task-04-validation.md).

## Инварианты и пределы

Нового production `Workspace.TryApplyChanges` reference site нет. Hydrate-owned
path не открывает MSBuild project/solution, не вызывает loader или Process.Start.
Exact inventory structural witness: **3 TargetOutsideTheScope** limits для
byte-reader, metadata-created и workspace-disposed test hooks с полными identities.
Произвольные callbacks остаются вне доказанного no-DTB path; inventory не ослаблен.
Host cleanup проверяется semantic Dispose ordering и completion notification,
metadata cleanup — actual ObjectDisposedException после доступа к image.

AnalyzerLifecycle **not-run / not required**: изменения ограничены инертным
isolated adapter и reconstruction helpers. Production analyzer/shadow-copy/
load/prepare/publication paths, lifecycle host и shared test/build configuration
не менялись. Это не положительное evidence lifecycle integration.

H-001 external XML исключён; source comments сохранены. H-002 допускает только
подтверждённый signing-independent base-query slice; dependent/unknown current
source даёт refusal до возврата session. DTO не разрешает DLL execution.
Multimodule assemblies, scripts и cyclic graph явно исключены. Linux/case-sensitive
regression branch не запускался локально; Windows casing controls проходят.
Candidate Solution остаётся detached от пустого host CurrentSolution.

New-PID round-trip, disk .cs edit, persistent store, production write/publication,
benchmark/speedup и приёмка всей E0 — **not-run / not-accepted**; task-06/07 planned.
Version/AssemblyVersion/FileVersion остаются **1.5.5 / 1.5.5.0 / 1.5.5.0**.
Task, epoch index и series execution summaries синхронизированы координатором
с этой датой/scope/evidence; требования и activation permissions не изменены.
