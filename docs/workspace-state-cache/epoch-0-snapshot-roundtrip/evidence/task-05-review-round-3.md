# E0/task-05 — независимое ревью, round 3

Дата: **2026-10-08**, Europe/Moscow. Рецензент: Grok 4.7, отдельная сессия, не исполнитель
и не рецензент round 1 или round 2. Фактический reasoning — режим этой сессии;
отдельный runtime effort id не выставлен.
Итог: **ACCEPT**. Новых must-fix findings: **0**. T05-R1-001 остаётся закрытым
исправлением round 2. Приёмка task-05, выданная после round 2, этим раундом
не пересматривается и не расширяется.

## Проверенный scope

Прочитаны task-05, spec эпохи, README эпохи, H-001/H-002 в тексте задачи,
task-05-hydrate.md, review round 1 и round 2. Сверены actual artifacts:

- `Services/Workspace/StateCache/IsolatedWorkspaceHydrator.cs`;
- `Services/Workspace/StateCache/IsolatedHydratedSession.cs`;
- `Services/Workspace/StateCache/Models/HydratedInputOccurrence.cs`;
- `SnapshotOptionFacts.ReconstructCompilation` / `ReconstructParse`;
- `SnapshotSigningProfile.UnsupportedReason`;
- `WorkspaceSnapshotCodec.ProjectKeyIdentity`;
- `RoslynMcpServer.Tests/WorkspaceStateCache/IsolatedWorkspaceHydratorTests.cs`.

Установка provider проверена по Roslyn 5.9.0, commit `35d9211b`,
`ProjectState` constructor и `ProjectSyntaxTreeOptionsProvider`.
Посторонние dirty WorkspaceLoadCache / SolutionManager files не входят в verdict.
C# и требования не менялись. Собственный прогон build/tests не запускался:
зафиксированная validation координатора остаётся Release build, hydrate **27/27**,
codec **25/25**, SourceStructure **123/123**, main **1350/1350**.

## Контракт, который держится

Codec encode/decode стоит до первого `AdhocWorkspace` и до чтения байтов.
Невалидный snapshot и уже отменённый token не создают host. Project IDs создаются
до графа с `ProjectKeyIdentity`: path сравнивается OS-aware comparer codec,
Configuration/Platform/TargetFramework остаются точными. Directed edges, aliases
и EmbedInteropTypes берутся по этому ключу, поэтому обратный порядок projects
не меняет связи. Session.Projects копирует ту же identity в immutable map.

Текущие байты физического document path читаются один раз (`InputPathCanon`).
Текст в DTO не используется. Encoding и checksum policy восстанавливаются публично;
checksum считается заново. Null encoding не получает persistence policy.
Linked source и additional membership одного пути проверены focused control.

Metadata — новые owned images с kind/aliases/EmbedInteropTypes/path.
Образ попадает в ownership list до validation. `CreateFromImage` ленив;
публичный reader проверяет image до возврата session. Отказ на втором image,
отмена после первого и второго image и injected failure освобождают уже взятые
images и host. Session.Dispose и failure path вызывают `Workspace.Dispose` в try,
`Metadata.Dispose` для каждого image в finally, затем notification. Повторный
Dispose ничего не делает. Candidate solution не применяется: `CurrentSolution`
остаётся пустым, нового production `TryApplyChanges` нет.

Role, RoleSource, ProducerSource копируются строками. Unknown не становится
UserInput. Generated и подтверждённый metadata producer получают новые IDs.
Presence считается через текущий `File.Exists`.

Base compilation строится `CSharpCompilation.Create` в topological order.
`Project.GetCompilationAsync` не вызывается. Analyzer identity сохраняется
`AnalyzerFileReference` с loader, чей `LoadFromPath` бросает; focused control
вызывает `GetAssembly`. Script source, внешние metadata modules и цикл
project references отвергаются до возврата session.

H-001: external XML — `DocumentationProvider.Default`; references не удаляются.
Source XML comment реального `WorkspaceSnapshot` проверяется на base compilation.
Обычный route documentation-dependent запросов по-прежнему относится к E1;
hydrate его не объявляет.

H-002: reconstruction требует accepted empty-search-path policy и ставит
`DesktopStrongNameProvider` без search paths. До возврата session проверяются
effective options, assembly attributes, InternalsVisibleTo с public key и
identity public key. `AssemblyKeyFile` и неразрешимый attribute дают
`InvalidDataException` со stage, project, configuration, TFM, operation,
reason и `route=refusal`; host и images при этом освобождаются.
Emit/signing и production log/fallback не заявляются.

`ProjectState` в Roslyn 5.9 при создании project ставит свежий
`ProjectSyntaxTreeOptionsProvider` на compilation options из analyzer-config
documents проекта. Provider режет diagnostic options по `tree.FilePath`.
Base compilation передаёт эти options и деревья с тем же путём, поэтому
комментарий hydrator про fresh provider совпадает с конструктором Roslyn,
а не только с `Assert.NotSame`.

Structural witness по-прежнему запрещает достижимость `OpenProjectAsync`,
`OpenSolutionAsync`, `MSBuildProjectLoader.LoadProjectInfoAsync`,
`Process.Start` и `Workspace.TryApplyChanges` из `HydrateAsync`. Точный
inventory limits — три `TargetOutsideTheScope`: `_readBytes`,
`_metadataCreated`, `_workspaceDisposed`.

## Граница verdict

ACCEPT относится к isolated schema-v1 Debug base-query hydrate adapter и
session owner в принятом срезе H-001/H-002. Cross-process round-trip,
persistent envelope, disk `.cs` edit, benchmark, E1 admission/publication
и public activation этим раундом не принимаются.
