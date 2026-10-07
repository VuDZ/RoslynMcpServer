# review-ds/F-006 — E0 валидирует DTO/codec в Tests assembly, а E1 поставляет другую реализацию

Дата: **2026-10-07**. Source: [review-ds/F-006](../../review-ds/findings/F-006.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT WITH MODIFICATION

## Finding summary

Не закреплено, где живут reusable E0 DTO/codec/hydrate и как их evidence применимо к production E1; reviewer предполагает неизбежную другую Tests-only реализацию.

## Relevant requirements / constraints

- E1/task-03 должен использовать E0 codec; production не зависит от Tests assembly.
- E0 не меняет production entry path, но это не запрет создать production-reusable inert classes.
- Принятая схема и actual executable codec — разные виды evidence; tests должны проверять поставляемый codec.

## Evidence checked

- [Предложенные production/test paths и отсутствие зависимости от Tests](../../execution.md)
- [Services/StateCache либо isolated prototype](../../epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md)
- [task-04-snapshot-codec-low.md](../../epoch-0-snapshot-roundtrip/task-04-snapshot-codec-low.md)
- [task-03-atomic-envelope-store-med.md](../../epoch-1-disk-cache-mvp/task-03-atomic-envelope-store-med.md)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю неопределённость расположения reusable кода: task-02 действительно допускает два варианта, а маршрут последующей передачи не задан. Evidence prototype нельзя автоматически присвоить переписанному production codec.

Не принимаю утверждение, что E0 обязательно живёт в Tests assembly и E1 обязательно имеет другую реализацию. Текст task-02 допускает Services/Workspace/StateCache; execution задаёт отдельно production и test каталоги. «Production path не меняется» означает отсутствие load integration, а не запрет добавить inactive shared DTO/codec. Ни task-04, ни другие документы не предписывают писать codec исключительно в Tests.

Поэтому узкое исправление — выбрать единый executable artifact: inert reusable components в production-compatible месте с начала E0, focused harness/tests отдельно, а E1 подключает тот же код. Если выбран isolated prototype, описать перенос и rerun всех необходимых bounds/round-trip checks против реального перенесённого кода; rewrite нельзя скрыть словом reuse.

## Position rationale

ACCEPT WITH MODIFICATION: artifact ownership надо уточнить, но обязательная двойная реализация основана на предположении reviewer.

## Proposed response

Suggested change: закрепить location/ownership E0 DTO/codec/hydrate и путь повторного использования в E1. Предпочтение автора — production-compatible components с E0 без включения runtime. После интеграции tests выполняются против фактически поставленного artifact по AGENTS; historical evidence не заменяет final validation. Не добавлять отдельную большую миграционную эпоху.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-003 — связь для рассмотрения, не объединённый dispute.
- review-ds/F-007 — связь для рассмотрения, не объединённый dispute.
