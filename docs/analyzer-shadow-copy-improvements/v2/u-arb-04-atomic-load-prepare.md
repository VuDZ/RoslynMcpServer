# U-ARB-04 — спецификация atomic load/prepare

Статус: **реализовано в v1.3.14; приёмка выполнена**. Решение
[принято](u-arb-04-decision-acceptance.md)
([u-arb-04-decision.md](u-arb-04-decision.md)). Evidence
[принят](u-arb-04-acceptance.md). Реализация
[принята](u-arb-04-implementation-acceptance.md). Inaccessible не открывать.
Не менять U-ARB-05 session-sticky, restart-required, confirmed-only matcher
и write boundary эпохи 4. Публичная MCP schema и новые параметры
`load_workspace` не вводятся. После ship — patch bump (ожидаемо 1.3.14).

Цель: для opt-in `shadowCopyInSolutionAnalyzers=true` не существует состояния,
в котором production semantic reader видит raw analyzer references новой
load-сессии между physical load и prepare. Следствие такой вставки уже
измерено: `GetCompilationAsync` existing-correct published snapshot грузит
real `Generator.dll` и на Windows даёт `MSB3021`.

## S1. Единый manager workflow

`WorkspaceTools.LoadWorkspace` не вызывает `LoadAsync` и
`ShadowCopyInSolutionAnalyzerReferencesAsync` двумя отдельными await с
разными захватами `_workspaceLock`. Один public/internal manager API
выполняет под **одним** захватом `_workspaceLock`:

1. physical load или same-key cache lookup (`LoadCoreAsync`);
2. при `true` — prepare + execution gate;
3. публикация итогового snapshot;
4. затем unlock.

Между шагами 1–3 другой production reader не получает snapshot этой операции.
`logProjectOutputDiagnostics` и сборка ответа `load_workspace` идут **после**
завершения boundary или строго под тем же захватом; не между load и prepare.

`LoadAsync` без prepare остаётся для `false`/omitted и для test-only путей,
которые не обещают overlay. Публичный `ShadowCopyInSolutionAnalyzerReferencesAsync`
не является способом «догонять» новую opt-in сессию после уже опубликованного
raw; enable на cache hit (`false→true`) входит в тот же единый workflow
(lookup + prepare + publish, один lock).

Публикация:

| Исход | Что можно опубликовать |
| --- | --- |
| Load `false`/omitted | raw `workspace.CurrentSolution` — честный no-overlay |
| Opt-in успех (новый session или cache-hit enable) | overlay после gate; real output не в published analyzer refs |
| Opt-in, prepare/gate не дал applied overlay (новый session) | **fail-closed:** не публиковать raw новой сессии как успешный opt-in result |
| Opt-in, есть прежний applied mapping (file-prepare fail, эпоха 2) | прежний stale mapping; не подменять его raw |
| Restart-required после честного no-overlay semantic | overlay не активируется; это не маскируется как успешный enable |
| Cancellation / blocking load failure | prepare не запускать; не оставлять «сырой успех» opt-in |

Fail-closed на новой opt-in сессии: `_solution ?? _workspace.CurrentSolution`
нельзя оставлять как production semantic snapshot. После unlock accessor не
должен скомпилировать `workspace.CurrentSolution` этой сессии с
existing-correct real analyzer path. Допустимо: не публиковать (`_solution`
не raw) и semantic accessor не делает fallback на `CurrentSolution`; либо
опубликовать snapshot без этих in-solution analyzer refs. Нельзя: присвоить
`_workspace` и отдать lock-free getter с fallback на raw.

Graph reopen / другой load key / `ClearWorkspaceAsync` по-прежнему сбрасывают
overlay. Opt-in на новой сессии снова идёт через этот workflow. Cache-hit
`true` при уже активном overlay — refresh prepare под тем же единым захватом,
без промежуточной raw публикации.

## S2. Сериализованный snapshot accessor

Production compilation / `GetSemanticModelAsync` не читают lock-free
`GetCurrentSolution()` в окне load/enable. Нужен accessor, который:

- ждёт завершения in-flight load/prepare boundary (тот же `_workspaceLock`
  или эквивалентный publish-gate);
- already-holding-lock callers не захватывают semaphore повторно;
- возвращает только опубликованный после boundary snapshot;
- не компилирует `GetWorkspaceCurrentSolution()` / raw `CurrentSolution`.

`GetCurrentSolution()` может остаться для не-semantic чтения (info, resolve
path, listing), но **не** как база `GetCompilationAsync` /
`GetSemanticModelAsync` в `Tools/` и production helpers. Новый caller без
строки в [epoch-1-semantic-entry-points.md](epoch-1-semantic-entry-points.md)
ломает inventory. Inventory guard дополняется: production файлы не вызывают
lock-free getter непосредственно перед compilation/semantic model.

Обязательно перевести на accessor (и flush-варианты, если они уже ждут диск):

- `CodeAnalysisTools` diagnostics/skeleton;
- `NavigationTools` usages/impl/definition/references;
- `UtilityTools.RenameSymbol` (вторая база после symbol — та же сериализованная
  сессия, без окна raw);
- `CodeFixTools` / `RefactoringTools` / `AstTools` / `TestTools` перед
  semantic model;
- helpers в `Services/` (`CallGraphHelper`, `CodeFixHelper`, и т.п.), если
  они компилируют документ.

`GetWorkspaceCurrentSolution` остаётся test-only seam. Lifecycle host
`oracleSource: workspace` не входит в production boundary.

Не вводить публичный session token, историю snapshots, MVCC.

## S3. Упорядочивание и совместимость

Линеаризуемый порядок двух конкурентных production запросов:

1. Semantic вошёл в accessor **до** opt-in load/enable — no-overlay operation.
   Existing-correct compilation может загрузить real DLL; поздний enable
   детерминированно `restart-required`. Это не баг boundary.
2. Opt-in load/enable вошёл первым — semantic ждёт unlock и видит только
   опубликованный overlay (или fail-closed результат, не промежуточный raw).
3. Состояния «новая сессия raw опубликована, prepare ещё нет» для production
   semantic reader нет.

Не менять:

- U-ARB-05: cached `false`/omitted не disable и не refresh; `true` включает
  или обновляет; reset / другой key — новая сессия;
- U-ARB-02 restart-required и U-ARB-03 main-only;
- confirmed-only matcher (E5-S2);
- write boundary (E4): inverse, preflight, reconciliation, mapping reapply
  без analyzer I/O.

Отдельно покрыть в тестах (не отдельными продуктовыми режимами): cache-hit
`false→true`, graph-stale reopen + `true`, cancellation mid-load, prepare
failure на новой сессии, blocking load (0 projects / missing Compile) без
prepare.

Test seam для детерминированного concurrency: пауза **после** physical
load/cache lookup и **до** prepare, пока lock ещё удерживается с точки зрения
публикации (снаружи не видно raw). Semantic через production accessor не
должен увидеть промежуточный raw и не должен загрузить real `Generator.dll`.
Вероятностный «два MCP tool сразу» не заменяет этот seam.

## S4. Приёмка реализации

- Конкурентная semantic после вошедшего первым opt-in: exact shadow path,
  real path отсутствует в process assemblies, forced rebuild меняет hash
  existing-correct DLL, `.csproj` byte-identical.
- Semantic первой, затем enable: `ShadowEnabled=false`,
  `execution-not-permitted:RestartRequired`, не успешная активация.
- Prepare failure / cancel на новой opt-in сессии: production accessor не
  компилирует raw real output этой сессии; `load_workspace` не сообщает
  успешный overlay.
- `WorkspaceTools` не имеет окна двух lock между load и prepare.
- Inventory: нет production `GetCompilationAsync`/`GetSemanticModelAsync` с
  lock-free `GetCurrentSolution()` / `GetWorkspaceCurrentSolution()`.
- Прежние `UArb04LoadBoundaryEvidenceTests`, `Epoch1WritePathTests`,
  epoch-1 flag matrix (sticky / `false→true`) остаются зелёными.
- Публичная schema без новых полей. Version в csproj/README после ship.

Shipped в v1.3.14: opt-in load/enable использует единый
`LoadAndPrepareAsync`; production semantic readers ждут
`GetPublishedSolutionAsync` / `GetPublishedSolutionAfterDiskSyncAsync` или
получают `Document` через сериализованный `FindDocumentAsync`. Контракт эпохи
4/evidence для уже активного overlay сохранён.
