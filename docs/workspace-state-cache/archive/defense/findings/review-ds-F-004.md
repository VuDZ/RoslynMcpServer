# review-ds/F-004 — Envelope не переносит verdict/completeness ordinary загрузки, а hydrated host выводит их из отсутствующих MSBuild diagnostics

Дата: **2026-10-07**. Source: [review-ds/F-004](../../review-ds/findings/F-004.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT WITH MODIFICATION

## Finding summary

Hydrate не имеет прежних MSBuild diagnostics; capture eligibility, loadGraphComplete и coverage outcomes могут измениться при построении новой input map.

## Relevant requirements / constraints

- Successful capture не должен превращать failed/incomplete load в healthy disk hit.
- Обычный load health, coverage unknown и session-bound analyzer admission — разные результаты; нельзя переносом одного bool подтвердить остальные.
- E1 сохраняет текущую watcher policy и не обещает доказанную полноту всего build graph.

## Evidence checked

- [E1: successful load/capture и watcher integration](../../epoch-1-disk-cache-mvp/spec.md)
- [task-07-existing-watcher-attachment-hi.md](../../epoch-1-disk-cache-mvp/task-07-existing-watcher-attachment-hi.md)
- [LastDiagnostics/HasBlockingLoadFailure/StartDiskWatcherUnderLock](../../../../../Services/Workspace/SolutionManager.cs)
- [CoverageReasons](../../../../../Services/Workspace/WorkspaceInputMapBuilder.cs)
- [PublishedAsComplete conjunction](../../../../../Services/Models/WorkspaceInputMap.cs)
- [LoadWorkspace diagnostics early failure report](../../../../../Tools/WorkspaceTools.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю contract gap: слово successful надо привязать к результату tool/health, а не только к тому, что manager open не бросил исключение. LastDiagnostics сегодня участвует в HasBlockingLoadFailure. Если новая ветка очистит diagnostics и вычислит loadGraphComplete заново, факт incomplete ordinary load потеряется.

Однако описанное Unknown→Complete не следует автоматически из одного пустого LastDiagnostics: current CoverageReasons всегда добавляет unknown imports/restore/custom task и другие reasons, а PublishedAsComplete требует отдельно complete multi-target/whole-graph coverage. Этот нюанс не отменяет потери LoadIncomplete и ошибочного health verdict.

Предпочтительный узкий outcome — запрещать reusable capture при blocking/непригодной ordinary загрузке и переносить проверенные portable load-health/coverage факты допустимого snapshot. Полная сериализация всех LastDiagnostics, transient session execution states и старых watcher ошибок не требуется: текущие watcher startup failures должны вычисляться заново, а old Banned/Unavailable не являются fresh admission. Non-blocking warnings можно сохранить в bounded diagnostic summary с происхождением.

## Position rationale

ACCEPT WITH MODIFICATION: eligibility/health/coverage необходимо определить; blanket перенос всех diagnostics и утверждение, что current builder станет Complete, слишком широки.

## Proposed response

Suggested change: формально определить capture eligibility без blocking load failure, graph-health/portable coverage evidence в envelope и правила rebinding текущей session. Сохранённое Unknown не повышать до Complete из-за пустого hydrate diagnostics. Тестировать ordinary vs disk по health/loadGraphComplete/coverage reasons; negative blocking-load case не записывает usable envelope. Historical/transient diagnostics отделить от independently recomputed live state.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-008 — связь для рассмотрения, не объединённый dispute.
