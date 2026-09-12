# S2 — отклонять устаревшую базу до записи

Статус: **не выполнено**. Зависимость: [S1](s1-regression-baseline.md).
Результат шага: V3-R1 устранён, свежий текст не перезаписывается stale candidate.

## Основание

`WorkspaceWriteBoundary.Preflight` проверяет session/path и analyzer diff,
но не свежесть `WorkspaceWriteOperationContext.BaseSnapshot`.
`ApplyWorkspaceWriteUnderLockAsync` сохраняет документы до `TryApplyChanges`.
Последующий отказ Roslyn и успешная reconciliation уже не защищают данные.
Это известный A4-09 с подтверждённым последствием потери свежих изменений.

Targets: `WorkspaceWriteBoundary`, `WorkspaceWriteOperationContext`,
`SolutionManager.SetPublishedSnapshot`, `ResolveOperationContext`,
`ApplyWorkspaceWriteUnderLockAsync` и under-lock callers.

## Работа

Контекст операции должен удерживать базовый опубликованный snapshot,
использованный mapping/session и штамп исходного raw workspace состояния
на момент выдачи базы. Под одним `_workspaceLock` сравнивать этот штамп с
текущим состоянием **до** ручного сохранения, project-file write и apply.
Подойдёт raw snapshot identity или внутренний revision с эквивалентным
покрытием всех мутаций. Сравнение overlay с raw по `ReferenceEquals` неверно.

Несовместимая база даёт `PreflightRejected` с различимой причиной stale base.
Отказ ничего не пишет и не запускает reconciliation: side effects этой
операции ещё не было. Изменение другого документа тоже означает изменение
базы; автоматическое объединение candidate в этом шаге не требуется.

Если меняется mapping или допуск публикации без изменения raw workspace,
старый контекст также нельзя молча переинтерпретировать новым состоянием.
Применить явную проверку совместимости либо консервативно отклонить candidate.
Symbol lookup и transform продолжают использовать одну удержанную базу.

`ResolveOperationContext` при неизвестной базе не присваивает ей текущие
session/mapping. Для внешнего candidate отсутствие проверяемого контекста
означает отказ до записи. Доверенные under-lock callers создают контекст
непосредственно из своей текущей базы без повторного захвата semaphore.
Это закрывает связанный риск A4-12 без истории snapshots или merge engine.

## Приёмка

- R1 из S1 зелёный: после held A → write B → apply A диск и published text
  содержат B; `SavedPaths` отказавшей операции пуст.
- Отдельно проверены stale после reset/reload, same-session intervening
  document write, watcher flush и неизвестный operation context.
- Candidate с актуальной базой успешно применяется с overlay и без него;
  under-lock update/flush не попадают в deadlock и не получают ложный stale.
- Известные original↔shadow replacements инвертируются точно; unrelated
  references, порядок и кратность сохранены, unknown analyzer diff отклоняется.
- При отказе `.csproj` и затрагиваемые документы byte-identical состоянию
  непосредственно перед попыткой stale apply.

## Результат

Не выполнено.
