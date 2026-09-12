# S4 — блокировать opt-in semantics при непригодном capture

Статус: **не выполнено**. Зависимость: [S3](s3-persistent-publication-state.md).
Результат шага: V3-R3 устранён; failed/incomplete capture не даёт raw semantic snapshot.

## Основание

`CreateFailClosedSnapshot` вызывает `StripInSolutionAnalyzerReferences`,
который перечисляет только provenance-confirmed references. При corrupt
capture подтверждений нет; «удалить подтверждённые» оставляет исходный
snapshot без изменений и возвращает real output semantic readers.

Targets: `AnalyzerProvenanceCaptureService` snapshot status,
`SolutionManager.LoadAndPrepareAsync`, recovery/publication/accessors,
`WorkspaceTools.LoadWorkspace`, `AnalyzerExecutionGate`.

## Работа

Для opt-in сессии при отсутствующем, Failed, Incomplete или чужом по session
capture не публиковать semantic snapshot целиком. Это консервативный отказ
на границе операции, а не попытка угадать происхождение отдельных references.
Он не требует filename fallback или удаления потенциально внешних analyzers.

Сообщение load должно явно отличать открытый MSBuild graph от неготового
semantic workspace: статус capture, причина отказа и отсутствие разрешённого
overlay. Не ограничиваться «0 rewritten» или безоговорочным «success».
Новых MCP параметров и структурированной схемы ответа не требуется.
Недоступные semantic operations возвращают понятную причину через существующий
формат ошибок; не получают null-dereference или raw fallback.

Отказ сохраняется при edit/flush, cached false/omitted и отмене. Cache hit
не делает failed snapshot Complete. Восстановление требует пригодного capture
на разрешённой границе обновления графа; если текущий API переиспользует capture,
сообщение должно указывать reset/reopen, а не обещать исправление cached true.
Reset/reopen по-прежнему не отменяет restart-required CLR identity policy.

Complete capture с отдельной unconfirmed/foreign reference не приравнивать
к failed capture: существующий confirmed-only matcher и диагностика
сохраняются. Политика inaccessible original здесь не выбирается.

## Приёмка

- R3 из S1 зелёный: corrupt capture + opt-in не даёт semantic snapshot,
  exact marker отсутствует, real DLL отсутствует в process assemblies.
- Проверены missing, corrupt и mixed valid/corrupt binlogs, Incomplete/null
  snapshot и несовпадение session; ни один не даёт raw fallback.
- После отказа edit/flush и cached false/omitted не открывают semantics.
- После reset/reopen с успешным capture fresh host исполняет exact V1 из
  shadow path; graph/session действительно новые, а не ошибочно reused.
- Complete capture сохраняет работу missing-path repro и foreign fixtures;
  no-overlay load не получает неоговорённого нового требования к capture.
- В ошибочном opt-in load ответ явно сообщает semantic unavailability;
  временные binlogs очищаются по существующему контракту capture.

## Результат

Не выполнено.
