# S2 — отклонять устаревшую базу до записи

Статус: **выполнено; независимая приёмка
[принята](s2-acceptance.md)** (v1.3.16, V3-R1 закрыт).
Зависимость: [S1](s1-regression-baseline.md).
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

Выполнено 2026-09-12. V3-R1 закрыт: stale candidate отклоняется до persist/apply/reconciliation.

### Источники и среда

- **База:** `fbd6766` (`test: add permanent V3-R1/R2/R3 regression baseline`)
- **Commit шага:** этот коммит; write-boundary/context, `SolutionManager`,
  disk sync, тесты S2, host `applyDetachedCandidate`, csproj `1.3.16`,
  README, этот файл и [s2-acceptance.md](s2-acceptance.md)
- **Версия csproj:** `1.3.16`
- **OS / host:** Windows, x64 process
- **SDK:** `10.0.204` (`run_dotnet_build` / `run_specific_test`)
- **MCP binary:** `RoslynMcpServer` (workspace tools; production publish/reload
  этого шага не делались)

### Реализация

- Контекст держит published base, mapping/session, raw snapshot identity,
  `_rawWorkspaceRevision` и флаг допуска overlay. Freshness сравнивает raw
  revision/identity, не overlay↔raw через `ReferenceEquals`.
- `Preflight` до любой записи: `unknown-operation-context`, `stale-session`,
  `stale-base` (raw или held base ≠ current published/raw), `stale-publication`.
- `ResolveOperationContext` больше не подставляет текущие session/mapping
  неизвестной базе (A4-12). Under-lock update/flush штампуют свою текущую базу
  без повторного semaphore.
- Успешный `TryApplyChanges` (включая reconciliation) поднимает revision.
  Watcher dirty path всегда обновляет identity snapshot, даже если текст
  совпал с `GetTextAsync`.

### Команды

1. `load_workspace` → `RoslynMcpServer.sln`
2. `run_dotnet_build` → `RoslynMcpServer.sln`
3. `run_specific_test` class=`V3RegressionBaselineTests` method=`V3_R1_…`, `noBuild=true`
4. `run_specific_test` class=`V3WriteBaseFreshnessTests`, `noBuild=true`, `timeoutSeconds=600`
5. `run_specific_test` class=`WorkspaceWriteBoundaryTests` / `WorkspaceDocumentDiskSyncTests`
6. Точечный прогон `Epoch4WorkspaceWriteTests` (полный класс превышает таймаут MCP-клиента)

### Фактические результаты

| Проверка | Результат |
| --- | --- |
| R1 `V3_R1_same_session_stale_candidate_is_rejected_before_any_write` | **passed** — `PreflightRejected`, `saved=0`, диск и published = B |
| Reset/reload, other-document write, watcher flush, unknown context | **7/7** `V3WriteBaseFreshnessTests` passed |
| Fresh overlay / no-overlay; under-lock update/flush | **passed** (нет deadlock и нет ложного stale) |
| Exact inverse / unknown analyzer diff | `WorkspaceWriteBoundaryTests` **15/15**; Epoch4 unknown-diff **passed** |
| `.csproj` и документы при stale apply | byte-identical состоянию до попытки |
| Epoch4 write suite (по методам) | все проверенные методы **passed** (text/overlay/watcher/reload/recon/IO/cancel/no-overlay) |
| R2 / R3 | остаются красными — это S3/S4, не регресс S2 |

### Самопроверка

- Отказ stale — `PreflightRejected` до persist; `SavedPaths` пуст; reconciliation не запускается.
- Symbol lookup/transform по-прежнему на одной удержанной базе; merge нет.
- Production published accessor (`publishedDocument` / `GetPublishedSolutionAsync`).
- Публичная MCP-схема не менялась; catalog 63 / 44,503.

### Ограничения

- Production/MCP publish+reload не выполнялись; номер выпуска в исходниках `1.3.16`.
- Полный `Epoch4WorkspaceWriteTests` одним вызовом упирается в таймаут MCP-клиента;
  методы прогнаны по отдельности.
- R2/R3 специально не закрывались.
- Inaccessible U-ARB-01 по-прежнему вне v3 S2.
