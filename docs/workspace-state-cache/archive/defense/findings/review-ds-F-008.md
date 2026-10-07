# review-ds/F-008 — Role evidence документов (IsGenerated) не переносится из DTO в hydrated input map

Дата: **2026-10-07**. Source: [review-ds/F-008](../../review-ds/findings/F-008.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT

## Finding summary

DTO roles не имеют явного restore/binding правила для existing input-map builder; потеря IsGenerated/provenance меняет классификацию disk events.

## Relevant requirements / constraints

- File role и all-memberships ownership должны иметь одинаковый смысл после ordinary и hydrate.
- Generated/Output не становятся UserInput по отсутствию признака; unknown provenance не угадывается.
- Один shared input map/session остаётся обязательным; восстановление не создаёт второй live index.

## Evidence checked

- [DTO document roles](../../epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md)
- [task-00-input-manifest-hi.md](../../epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md)
- [task-07-existing-watcher-attachment-hi.md](../../epoch-1-disk-cache-mvp/task-07-existing-watcher-attachment-hi.md)
- [Existing IsGenerated read и AddDocument flag comment](../../../../../Services/Workspace/RoslynDocumentBuildSideEffect.cs)
- [AddDocument/RoleForDocument](../../../../../Services/Workspace/WorkspaceInputMapBuilder.cs)
- [ClassifyKnown/NoticesFor](../../../../../Services/Workspace/WorkspaceDiskEventClassifier.cs)
- [DocumentInfo.Create GeneratedOnDisk argument](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperiment.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю. Поле roles в DTO означает intent, но не задаёт механизм, которым restored graph снова отдаёт ту же роль builder-у. Public DocumentInfo creation без isGenerated может установить false, а suffix heuristic не заменяет evidence реального evaluated item. Physical path может иметь несколько ролей/owners, поэтому одного bool по пути тоже недостаточно.

Existing reflection-reader — факт текущей реализации, а не разрешение новому capture использовать private state для любых options. Допустимые restore варианты: public isGenerated при construction там, где evidence определено, или применение переносимых occurrence-role facts при построении единственного input map. Неизвестная роль и producer identity должны оставаться неизвестными, а evaluation precedence сохраняться.

Generator text в DTO не добавляется: роль on-disk generated compile input и generated source text, produced by generator, различаются. Проверка role round-trip не разрешает перенос cached execution provenance.

## Position rationale

ACCEPT: это конкретный недостающий integration contract, влияющий на observable watcher/content-refresh поведение.

## Proposed response

Suggested change: schema фиксирует переносимые occurrence roles/provenance и producer bindings, hydrate/input-map adapter явно применяет их с новыми IDs. Добавить ordinary↔hydrate role/owner comparison для user, generated/unknown, additional/config, output и shared/evaluation cases. Не делать default UserInput и не восстанавливать producer по одному имени файла.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-004 — связь для рассмотрения, не объединённый dispute.
- review-grok/F-004 — связь для рассмотрения, не объединённый dispute.
