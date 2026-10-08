# E0/task-05 — независимое ревью, round 1

Дата: **2026-10-08**, Europe/Moscow. Рецензент: Codex (GPT-6; точный runtime model ID
недоступен), отдельный subagent, не исполнитель проверяемого scope.
Итог: **MUST-FIX**. Открытых findings: **1**; исправлений пока не подтверждено.
Task acceptance, bookkeeping и commit принадлежат координатору.

## Проверенный scope

База: `c78764d`. Прочитаны task-05, spec и README эпохи, README/execution/benchmark
серии, accepted implementation/review outputs task-03/04/08 и H-001/H-002.
`docs/code-style.md` прочитан целиком до комментариев о стиле; `.editorconfig`
в репозитории не найден. Проверены новые hydrate/session/occurrence files,
изменения SnapshotOptionFacts, focused tests и task-05-hydrate.md.
Посторонние dirty/untracked WorkspaceLoadCache/tooling files не входят в verdict.
Рецензент не менял C#, requirements, task/index statuses и не делал commit.

## T05-R1-001 — P2 / MUST-FIX: rebinding использует другую identity проектов, чем codec

Место: `Services/Workspace/StateCache/IsolatedWorkspaceHydrator.cs:41`
(последствия на строках 168, 262, 278); дополнительное место:
`Services/Workspace/StateCache/IsolatedHydratedSession.cs:37`.

Codec на строке 353 создаёт project-key dictionary с ProjectKeyComparer.
Его Equals на строках 1218–1224 использует InputPathCanon.Comparer для ProjectPath
и точное равенство Configuration/Platform/TargetFramework. На Windows
InputPathCanon.Comparer — OrdinalIgnoreCase (`InputPathCanon.cs:10–12`).
Project reference принимается через этот dictionary (`WorkspaceSnapshotCodec.cs:656–657`),
occurrence owner/producer — на строках 743 и 790; graph/membership/producer checks
на строках 924, 945, 962, 969 применяют ту же identity.

Hydrate создаёт `ids` через ToDictionary без comparer. SnapshotProjectKey — record
со string properties, поэтому default equality различает регистр ProjectPath.
Например, у library остаётся Key.ProjectPath `C:\\...\\Library.csproj`, а у app
edge.Target — тот же key с `LIBRARY.CSPROJ`. Schema encode/decode принимает эту
направленную связь на Windows: это тот же canonical physical project path и тот
же instance context. Codec не переписывает исходный Target в canonical stored key.
Hydrate затем обращается к `ids[reference.Target]` на строке 168 и получает
KeyNotFoundException вместо восстановления валидного graph. Аналогичный valid
owner либо confirmed producer с другим регистром ломает RebindInputs.
Cleanup этой ошибки работает, но mandatory graph/identity contract не выполняется.

Это вывод по actual validation и lookup code, **не результат запущенного
исполняемого case-control**. Воспроизводимый regression control: на Windows
использовать существующий двухпроектный fixture, заменить только casing
ProjectPath в target, затем отдельно в occurrence owner/producer; сначала
проверить успешный actual codec round-trip, затем успешный hydrate, правильные
fresh IDs, edge и occurrence bindings. При изменении порядка проектов результат
должен остаться тем же. На case-sensitive ОС сохранить различие путей.

Требуемое исправление: использовать одинаковую OS-aware project-key identity
на hydrate rebinding и в сохраняемом session project map. Одной замены ToDictionary
недостаточно, если следующий `projects.ToImmutableDictionary()` возвращает map
с default record comparer. Не менять accepted codec requirements ради отказа
ранее допустимых graph keys; добавить meaningful regression control.

## Остальные проверенные контракты

В проверенном diff других must-fix findings не обнаружено. Fresh IDs выделяются
до construction; source bytes берутся с диска и объединяются по physical path;
membership metadata, roles/provenance/producer facts переносимы без guessing.
Options используют принятый public construction witness. Metadata получают
fresh owned images и reference properties; lazy image validation принудительно
выполняется до результата. External assembly modules, scripts и cyclic graph
явно исключены; real multi-module positive control не заявлен.

H-001 external XML exclusion объявлено; source XML comments проверяются на real
control. H-002 current-source signing-dependent и unresolved-attribute controls
отказывают до возврата session. Base compilations строятся вручную, analyzer
loader запрещает assembly execution. Construction не публикует candidate.
Оба ownership paths вызывают Workspace.Dispose с Metadata.Dispose в finally;
focused controls проверяют actual image disposal и idempotent session disposal.

Structural witness утверждает виды и identity ровно трёх внешних resource hooks:
`_readBytes` ← `readBytes`, `_metadataCreated` ← `metadataCreated`,
`_workspaceDisposed` ← `workspaceDisposed`, все TargetOutsideTheScope.
Произвольные supplied callbacks не доказаны no-DTB; report прямо сохраняет эту
границу, как и ограничения interface/virtual dispatch analysis. Default owned
path не содержит MSBuild open/DTB loader, process start или TryApplyChanges.
SourceStructure inventory не ослаблен. New PID, E1 admission/publication,
production writes и benchmark этим task/review не принимаются.

## Validation и граница verdict

Рецензент не запускал build/test процессы: во время review complete main suite
выполнял координатор. Полученные от координатора результаты против review code:
Release solution build — PASS; focused — **21/21**; SourceStructure — **123/123**;
main `Category!=AnalyzerLifecycle` — **1344/1344**. Это coordinator validation,
не независимый run рецензента и не проверка отсутствующего casing control.
После исправления finding координатор повторяет обязательную final validation.
AnalyzerLifecycle отдельно здесь не запускался; inert isolated components не
меняют production analyzer/shadow-copy/load/prepare/publication paths или harness.

Verdict **MUST-FIX** относится к одному дефекту rebinding. Зелёные существующие
controls не покрывают этот valid schema graph case и не закрывают finding.
