# Эпоха 4 — Единая граница записи workspace

Статус: **планируется**. Зависимости: эпоха 1 и контракт original↔shadow mapping
[эпохи 2](epoch-2-immutable-shadow-copies.md). Binding gate эпохи 3 независим.

## E4-S1. Контекст операции и preflight

Все production-вызовы `MSBuildWorkspace.TryApplyChanges` проходят через одну
внутреннюю границу в `SolutionManager` либо выделенном компоненте. Общий workflow
охватывает также вызывающую запись документов, fallback и публикацию overlay.
Недостаточно обернуть только вызов workspace после уже выполненного сохранения.

Вход — candidate и operation context: идентичность неизменяемого базового снимка,
load/session identity и конкретный mapping/generation, с которым candidate создан.
Операция читает symbol и строит преобразование на одной базе. «Последний mapping»
не заменяет mapping исходной операции. Не добавлять постоянную историю снимков,
реестр всех in-flight operations или merge engine.

До любого документного или project-file write со стороны сервера:

1. Проверить совместимость текущей load session и базы с operation context.
   Stale/несовместимый candidate отклонить до побочных эффектов; защитой overlay
   нельзя перезаписывать более свежие изменения.
2. Классифицировать analyzer-reference diff относительно базы и известного mapping.
3. Обратить только внесённые этим mapping замены original↔shadow. Сохранить
   несвязанные references, порядок и кратность, без дублирования.
4. Отклонить неподдержанный diff до записей. Не стирать его молча и не передавать
   в `MSBuildWorkspace` как best effort. Намеренное редактирование analyzer
   references остаётся вне серии до отдельного контракта.

Проверка и последующие действия входят в согласованную оркестрацию workspace;
перезагрузка не должна сделать проверенную сессию другой между preflight и apply.
Under-lock callers входят во внутренний under-lock этап без повторного захвата
того же semaphore. Внешняя запись watcher уже состоялась вне сервера: preflight
предотвращает новые серверные side effects, а не отменяет изменение пользователем.

Добавленные/удалённые проекты учитываются явно. Для удалённого проекта mapping
не создаёт проект обратно; для добавленного отсутствие записи в базе не разрешает
неизвестные analyzer changes или temp references. Классификация и отказ применяются
до записи; обычные допустимые изменения состава проектов не дают основание
угадывать происхождение по temp-path prefix.

## E4-S2. Общий workflow

Workflow состоит из preflight, точной инверсии overlay, применения очищенного
candidate к workspace и требуемого сохранения файлов, обработки частичного отказа,
reconciliation и финальной публикации overlay. Он учитывает, что сам
`TryApplyChanges` может записывать файлы: перестановка apply перед ручным сохранением
не превращает workflow в транзакцию. Результаты фактически выполненных записей
отслеживаются независимо от bool workspace apply.

| Исход | Сохранение и workspace | Публикация и результат |
| --- | --- | --- |
| Preflight rejection | Никаких новых записей и apply от этой операции | Candidate не публикуется; явный отказ с причиной |
| Полный успех | Все запрошенные поддержанные изменения сохранены и применены | Один полный overlay snapshot из принятого состояния и prepared mapping; полный успех |
| Отказ после побочных эффектов | Часть файлов могла измениться при I/O error, cancellation или rejected apply | Отдельно отражать сохранённые пути и неприменённые изменения; переход к reconciliation, без успеха всего candidate |
| Reconciliation успешна | Согласовать тексты, действительно достигшие диска, с принятым workspace state | Один полный overlay snapshot по готовому mapping; исход остаётся частичным, если весь запрос не выполнен |
| Reconciliation неуспешна | Согласование не подтверждено для всех затронутых текстов | Не публиковать неприменённый candidate; явно сообщить reconciliation failure и известные частичные записи, не обещать свежесть диска |

Неприменённые project-state changes никогда не публикуются. Text reconciliation
после failed apply сохраняется как часть workflow и не объявляется запрещённой
«публикацией всего candidate». Успешная reconciliation не превращает частично
выполненный запрос в полный успех. Если reconciliation не удалась, последний
полный опубликованный снимок не является подтверждением согласованности с диском.

Внутренний result и адаптеры должны различать полный успех, preflight rejection,
partial persistence, reconciliation success/failure и причину отказа/отмены.
Отразить известные сохранённые пути и неприменённый project state; один список
paths или bool недостаточен для сообщения полного результата. Новый публичный
структурированный MCP schema этой ревизией не вводится.

После успешного apply либо успешной reconciliation повторно применить подготовленный
mapping и опубликовать целый immutable snapshot. Ни этот шаг, ни обычный edit,
ни watcher flush не выполняют analyzer refresh, copy или hash. Последний результат
artifact refresh сохраняется отдельно от результата текущей записи.

## E4-S3. Граница записи и граница загрузки

Инверсия защищает persistence `.csproj`; она не доказывает изоляцию analyzer loading.
Проверить семантические чтения raw `workspace.CurrentSolution`, раннюю загрузку до
overlay и existing-correct-path matrix эпохи 1. Фиксировать loaded path/identity
и forced rebuild после всех write paths. Конкретный trigger lock заранее не задан.
Отдельный дизайн semantic/load boundary допускается только после воспроизведения
и локализации утечки по U-ARB-04. Anti-lock цель не сужается до missing-path fixture.

## E4-S4. Приёмка

- Инвентаризация production `TryApplyChanges` и вызывающих путей сохранения
  подтверждает общий workflow, включая внутренние under-lock варианты.
- Exact inverse восстанавливает только известные mapping entries, сохраняет
  несвязанные ссылки, порядок и кратность. Тесты whole-list wipe не подтверждают
  этот контракт и должны быть заменены по новым ожиданиям.
- Отдельные тесты: no-overlay, unknown analyzer diff, stale base/session после
  reload, добавленный и удалённый проект. Unsupported/stale отказ происходит до
  серверной записи любых документов/проектов, даже если candidate содержит rename.
- Для text edit (`UpdateDocumentInMemoryAsync`), overlay-derived
  `ApplySolutionChangesToDiskAsync`, watcher delivery+flush и fallback проверить
  exact marker, запрошенный текст при полном успехе и `.csproj` bytes. При
  частичном исходе проверить именно фактически сохранённые тексты и явный статус,
  а не ожидать полного запрошенного изменения.
- Fault injection: rejected apply, ошибка отдельного файла, cancellation после
  части записей, успешная и неуспешная reconciliation. Неприменённые project changes
  не публикуются; нет ложного полного успеха или обещания rollback.
- Post-apply/reconciliation используют готовый mapping без analyzer file I/O;
  семантические операции не смешивают базовые снимки.
- Нет временных `<Analyzer Include>` после сохранения и последующей сборки;
  independent existing-output lock tests сохраняются.

Атомарное присваивание `_solution` обеспечивает целостность ссылки на immutable
снимок. Оно не означает атомарность нескольких `.cs`, `.csproj` и workspace вместе.
Общий rollback/transaction engine и redesign конкурентного редактирования вне серии.
