# review-grok/F-001 — Ordinary capture реального проекта не проходит публичные metadata/options

Дата: **2026-10-07**. Source: [review-grok/F-001](../../review-grok/findings/F-001.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

UNRESOLVED

## Finding summary

E0 real positive capture с file references конфликтует со strict exact/public fidelity нечитаемых DocumentationProvider и StrongNameProvider constructor state.

## Relevant requirements / constraints

- Нужен работающий real round-trip, а не ещё один negative-only эксперимент.
- Текущий E0 требует точное представление либо отказ, запрещает private reflection, silent defaults и удаление references ради success.
- Пользователь согласовал binary validation heuristic, но не давал разрешение терять XML documentation/signing semantics: это разные ограничения.

## Evidence checked

- [Поддерживаемый срез и Приёмка](../../epoch-0-snapshot-roundtrip/spec.md)
- [task-00-current-seams-and-controls-med.md](../../epoch-0-snapshot-roundtrip/task-00-current-seams-and-controls-med.md)
- [task-03-ordinary-capture-med.md](../../epoch-0-snapshot-roundtrip/task-03-ordinary-capture-med.md)
- [Исторический public API limitation и synthetic-only positive scope](../../../../backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md)
- [CaptureAsync: non-OwnedMetadataReference refusal; CaptureCompilationOptions](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperiment.cs)
- [Stock reference/strong-name refusal controls](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperimentTests.cs)
- [Комментарий: adjacent XML hash не подтверждает selected provider](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/WorkspaceObservationReader.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Подтверждаю concrete obstruction: existing capture отказывает любому stock PortableExecutableReference, а historical report прямо фиксирует отсутствие public provider getter и DesktopStrongNameProvider search-path getter. Обязательный real control с metadata references не может пройти этот exact observer без нового принятого mapping contract. Это ограничение известно заранее; synthetic OwnedMetadataReference не доказывает ordinary capture.

При этом отказ конкретного observer не доказывает невозможность всякого public reconstruction из independently captured loader inputs. Такой дополнительный source-backed механизм сейчас не описан и не проверен; защитить positive outcome ссылкой на его гипотетическое существование нельзя. Также нельзя вывести допустимость обнуления provider из того, что query всё ещё находит выбранный символ: observable XML documentation/emit/signing поведение имеет отдельный scope.

Выбор между более узким публично проверяемым semantic contract и дополнительным источником constructor/effective input evidence меняет критерий cache hit. Автор не вправе принять потерю единолично. Признаю blocker для заявленного strict positive E0, не объявляя весь staged cache approach фундаментально невозможным.

## Position rationale

UNRESOLVED: факт getter limitation принят; исправление требует human fidelity choice или конкретного доказанного alternate capture source. Нельзя автоматически снять real positive requirement или назначить negative E0 успехом.

## Proposed response

Для разрешения: владелец выбирает допустимую observable fidelity границу для XML-doc/strong-name и связанное write/emit поведение, либо требует отдельное constructor-input evidence для strict support. Предложенный узкий путь должен перечислить публично проверяемые options/reference properties/semantics и неподдержанные операции, сохранив реальный positive control. Pure null/default substitution не допускается до такого решения. Динамическая проверка конкретного control подтверждает границы, но не создаёт отсутствующие getters.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-003 — связь для рассмотрения, не объединённый dispute.
- review-ds/F-006 — связь для рассмотрения, не объединённый dispute.
- review-ds/F-002 — связь для рассмотрения, не объединённый dispute.
