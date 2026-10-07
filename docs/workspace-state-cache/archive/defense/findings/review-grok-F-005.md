# review-grok/F-005 — Состав disk request key не перечислен

Дата: **2026-10-07**. Source: [review-grok/F-005](../../review-grok/findings/F-005.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT WITH MODIFICATION

## Finding summary

Термин semantic modes не сопоставлен закрытому списку load arguments; reviewer предлагает включить effective properties и overlay flag в disk key.

## Relevant requirements / constraints

- Key не смешивает разные base graphs; diagnostic flags и build suffix не делают новый semantic graph.
- Snapshot содержит base без shadow paths; overlay session/provenance проходят fresh prepare.
- Key/lookup и admission/preparation выбранного mode — разные уровни; требования обоих надо назвать.

## Evidence checked

- [Поведение первого MVP; base DTO без shadow paths](../../README.md)
- [task-03-ordinary-capture-med.md](../../epoch-0-snapshot-roundtrip/task-03-ordinary-capture-med.md)
- [task-05-fresh-session-preparation-hi.md](../../epoch-1-disk-cache-mvp/task-05-fresh-session-preparation-hi.md)
- [LoadWorkspace parameters](../../../../../Tools/WorkspaceTools.cs)
- [LoadAndPrepareAsync sticky/prepare branches](../../../../../Services/Workspace/SolutionManager.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю необходимость closed parameter matrix. BuildArgs уже исключён из graph identity, но briefOutput/logProjectOutputDiagnostics/useDiskCache/forceReload и режим analyzer preparation надо явно классифицировать. Это небольшая contract поправка.

Модифицирую recommended change в двух местах. Во-первых, evaluated effective properties нельзя вычислять из старого envelope; отдельный review-ds/F-001 требует разделить их с pre-evaluation merged globals. Во-вторых, обязательное включение overlay bool в base-envelope key не доказано. Shadow paths в DTO уже запрещены, capture описан как base. Один чистый base snapshot может использоваться для разных overlay requests, если independent fresh preparation/support predicate обязательны для каждого. Альтернативно separate mode keys допустимы ради простоты, но это design choice.

Failure с отдачей cached shadow paths нарушает существующий запрет DTO; реальный риск — пропустить fresh mode admission или недокументированно разрешить неподдержанный overlay hit. Поправка должна закрыть этот риск, а не автоматически раздвоить storage.

## Position rationale

ACCEPT WITH MODIFICATION: перечисление параметров необходимо; обязательный separate overlay key и прежний смысл effective нельзя принять без уточнения base/mode contracts.

## Proposed response

Suggested change: matrix для workspacePath/configuration/platform/TFM, overlay, buildArgs, diagnostic flags, useDiskCache/forceReload; отдельно locator/base identity и request admission. Current normalized host globals участвуют по решению identity contract; buildArgs/briefOutput/logProjectOutputDiagnostics/policy flags не меняют base graph. Overlay либо разделяет identity, либо sharing разрешён только с обязательным fresh prepare и explicit supported mode. Согласовать с решениями по DS/F-001 и DS/F-002.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-001 — связь для рассмотрения, не объединённый dispute.
- review-ds/F-002 — связь для рассмотрения, не объединённый dispute.
