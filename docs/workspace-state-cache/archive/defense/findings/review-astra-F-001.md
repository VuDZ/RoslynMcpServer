# review-astra/F-001 — Post-load probe не связывает manifest с поколением evaluated graph

Дата: **2026-10-07**. Source: [review-astra/F-001](../../review-astra/findings/F-001.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT

## Finding summary

Post-load hashes могут принадлежать новым props/import bytes, тогда как captured options и memberships получены из прежних bytes; стабильность во время capture не связывает graph с evaluation generation.

## Relevant requirements / constraints

- E1 разрешает перенос пригодного для semantic queries ранее evaluated graph; значимое изменение graph inputs должно вести к ordinary fallback.
- Пользователь выбрал file/directory hashes для offline diff. Это предполагает согласованный исходный graph+index, а не разрешает произвольную привязку hashes к уже загруженному состоянию.
- Manager semaphore не является filesystem lock; приёмка E1 включает cancellation/startup race.

## Evidence checked

- [E1: Store и поведение, Интеграция](../../epoch-1-disk-cache-mvp/spec.md)
- [task-00-input-manifest-hi.md](../../epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md)
- [task-06-load-save-and-restore-hi.md](../../epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md)
- [LoadCoreAsync: open перед watcher start](../../../../../Services/Workspace/SolutionManager.cs)
- [OpenAndCaptureAsync/ReplayOne: нет consumed-byte inventory](../../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs)
- [CaptureAsync: held source characters против admitted bytes](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperiment.cs)
- [Capture_refuses_stale_source_text_against_current_independent_bytes](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperimentTests.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Finding корректен. Текущий текст защищает интервал read/serialize/write после ordinary load, но не задаёт проверку принадлежности evaluated options тем же inputs. Watcher handshake будущего hydrate защищает другой интервал и не исправляет первоначальный несогласованный envelope.

В коде обычный open выполняется до StartDiskWatcherUnderLock. Existing provenance replay сохраняет properties и analyzer items, но не обязательные hashes всех фактически прочитанных evaluation inputs. Равные post-load probes не обнаружат A→B до первого probe. Тест stale source в isolated experiment подтверждает необходимость отдельной проверки consumed state для source; его нельзя распространить на props/targets без дополнительного evidence.

MVP не требует универсального filesystem snapshot. Для ограниченного профиля достаточно обязательного доказанного binding graph↔input generation; там, где его нет, ordinary load может быть полезным, но reusable capture запрещён. Даже pre/post hash сам по себе не исключает A→B→A; гарантию конкретного механизма нельзя объявить сильнее его evidence.

## Position rationale

ACCEPT: это отсутствующий contract первоначального capture, а не запрос расширить общий live freshness layer. Failure interleaving логически допустим; его динамическое воспроизведение здесь не заявляется.

## Proposed response

Suggested change после arbitration: в E1/task-00 и task-06 определить checked interval/evidence для original evaluation и capture. Newly discovered import/region должен проходить предусмотренное bounded повторное подтверждение либо запрещать capture целиком. Добавить deterministic props-after-consumption negative control до первого post-load probe; отвергнутый capture не ломает successful ordinary load. Механизм остаётся предметом следующей стадии, третьего post-load hash недостаточно.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-grok/F-002 — связь для рассмотрения, не объединённый dispute.
- review-grok/F-004 — связь для рассмотрения, не объединённый dispute.
