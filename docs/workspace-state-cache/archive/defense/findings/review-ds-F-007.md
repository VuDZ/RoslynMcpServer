# review-ds/F-007 — Host seam не учитывает единственную разрешённую точку Workspace.TryApplyChanges

Дата: **2026-10-07**. Source: [review-ds/F-007](../../review-ds/findings/F-007.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT WITH MODIFICATION

## Finding summary

Новый host adapter может добавить второй production Workspace.TryApplyChanges site и нарушить exact structural write-boundary invariant.

## Relevant requirements / constraints

- В production scope есть ровно один Workspace.TryApplyChanges reference в SolutionManager.TryApplyWorkspaceChanges.
- Нельзя ослабить exact structural inventory ради нового host.
- Hydrate initialization через public ProjectInfo/DocumentInfo construction и изменение published workspace — разные операции.

## Evidence checked

- [task-04-manager-host-ownership-hi.md](../../epoch-1-disk-cache-mvp/task-04-manager-host-ownership-hi.md)
- [task-05-isolated-hydrate-hi.md](../../epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md)
- [SourceStructure и exact inventory](../../execution.md)
- [Verify: symbol call-site invariant](../../../../../RoslynMcpServer.Tests/SourceStructure/WriteBoundaryChecks.cs)
- [TryApplyWorkspaceChanges](../../../../../Services/Workspace/SolutionManager.cs)
- [CreateBaseSessionAsync: AddProject и isolated TryApplyChanges](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperiment.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Риск нарушения подтверждён. Existing isolated experiment действительно имеет собственные TryApplyChanges calls; копировать их в Services без адаптации нельзя. Manager ownership/task-04 должен знать этот structural constraint заранее, а не обнаруживать его только в общем CI.

Не принимаю correction «вся материализация Solution только через TryApplyWorkspaceChanges» как единственно допустимый путь. Structural test считает конкретный символ TryApplyChanges; он не запрещает public construction нового Adhoc candidate из SolutionInfo/ProjectInfo/DocumentInfo или AddSolution/AddProject. Existing prototype уже использует AddProject, хотя завершает assembly solution через TryApplyChanges. Refactor может избежать дополнительного Apply site при initialization.

Защищаю сам host seam: он совместим с инвариантом при правильной границе ответственности. Любые operations, которым нужен Workspace.TryApplyChanges, остаются в единственном wrapper. Альтернативный candidate construction должен отдельно пройти structural/transitive checks и не служить обходом проверки server writes.

## Position rationale

ACCEPT WITH MODIFICATION: добавить явный invariant и focused structural verification необходимо; обязательное применение manager wrapper для любого construction слишком сильно.

## Proposed response

Suggested change: task-04/task-05 явно запрещают новый production TryApplyChanges reference outside existing wrapper. Выбрать public candidate construction без второго site либо передавать apply intent существующей manager boundary. Проверить весь SourceStructure, включая call-site ownership и exact reachability limits; не просто увеличить допустимое число sites. No new approval или архитектурный ledger этим ответом не вводится.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-006 — связь для рассмотрения, не объединённый dispute.
