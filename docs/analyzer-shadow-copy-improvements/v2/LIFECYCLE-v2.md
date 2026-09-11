# Матрица жизненного цикла v2

Этот документ — общий контракт эпох 1–6. Текущие факты относятся к v1.3.5 по
арбитражу; целевые строки не являются отчётом о выполненных тестах. U-ARB-02/03/05
не выбираются матрицей. «Обновление» в таблицах всегда уточняется: граф, файлы
или исполнение.

## LC-S1. Действие, v1.3.5, цель и проверка

| Действие/ветка | v1.3.5 по входным материалам | Цель v2 | Проверка |
| --- | --- | --- | --- |
| Первый load/cache miss, false или omitted | Открывает workspace, overlay не включён | Сохранить opt-in, новая сессия без активного mapping | E1-S2: first load, broken-path negative control |
| Первый load/cache miss, true | После load отдельная подготовка main/PDB; rewrite до lazy load | Подготовка и ответ привязаны к сессии, immutable mapping, раздельный execution status | E1-S1/S2/S4, E2-S5 |
| Cached load, true | Граф переиспользуется, copier вызывается отдельно | Попытка подготовки на этом входе; при обещанном refresh обязательны hash; supported execution по U-ARB-02 | Три операции V1→V2, same-size/same-time mutation |
| Cached load, ранее true, затем false/omitted | Активный overlay сохраняется | Сохранить до U-ARB-05; это не disable и не новый refresh | true→false, true→omitted |
| Cached load, false→true | Попытка включения/подготовки без reopen графа | Mapping той же load session, результаты по ссылкам | false→true и prepare failure |
| Reset+load с каждым вариантом флага | Workspace заново; loader тот же; старый opt-in сброшен | Новый session mapping только при opt-in; свежая CLR версия не следует из reset | Варианты флага после reset и exact V1/V2 |
| Process restart+load | Новый процесс и loader | Проверка выбранного режима U-ARB-02; opt-in при новой загрузке | Exact V2, identity/path, стоимость restart |
| Graph-stale reopen | Граф открывается заново, поля overlay сбрасываются; loader остаётся | Mapping новой базы при opt-in, никакого переноса старых ProjectId | Graph stale, новая сессия, запись старого candidate |
| Другое решение | Workspace/overlay сбрасываются; CLR identity не сбрасывается | Нет переноса mapping, gate безопасного исполнения по U-ARB-02 | A enabled→B disabled, same identity |
| `GetCurrentSolution()` | Возвращает сохранённый snapshot/fallback; flush не выполняет | То же чтение без подготовки; одна база semantic operation | Инвентаризация читателей и read→transform |
| Text edit / `UpdateDocumentInMemoryAsync` | Raw text apply, затем повторная подготовка overlay | Общий workflow, готовый mapping, без refresh файлов анализатора | Маркер, новый текст, `.csproj` bytes, forced rebuild |
| Overlay-derived apply / `ApplySolutionChangesToDiskAsync` | Запись текстов до guard; снятие whole-list diff; reprepare после apply | Preflight до серверной записи, точный inverse, структурированный исход, reapply mapping | Exact inverse, unknown/stale rejection, все outcomes E4-S2 |
| Under-lock text apply/fallback | Отдельные внутренние ветки могут публиковать тексты и reprepare | Общий workflow без повторного semaphore; только применённое состояние | Fault injection и отсутствие повторного захвата |
| Доставка FSW | Накапливает dirty paths; `_solution` ещё не обновлён | Сохранить различие delivery/flush; не запускать artifact refresh | Наблюдаемая доставка с timeout |
| Production watcher flush | Применяет dirty documents к workspace, затем reprepare overlay | Common workflow и mapping reapply без analyzer I/O | Реальный FSW→FindDocumentAsync/синхронизированный getter→маркер/текст |
| Полный/частичный prepare failure | Rewrite results могут теряться на reapply; bool не описывает частичный mapping | Пер-ссылка результат, failed/stale refresh отдельно от edit и execution | Нет output, injected prepare failure, disk-full |
| Apply failure / частичная запись / reconciliation | Уже записанные тексты согласуются fallback; это не атомарное сохранение | Не публиковать неприменённые project changes; различать partial и reconciliation failure | Cancellation, per-file I/O, rejected apply, fallback |
| Clear/dispose | Workspace/overlay очищаются; process loader остаётся | Не удалять опубликованные поколения и не обещать unload | Старый operation context отвергается; cache сохраняется до безопасного cleanup |

## LC-S2. Эффекты целевых переходов

Обозначения: **G** — workspace graph/session; **S** — опубликованный snapshot;
**M** — prepared mapping; **A** — активные поколения ссылок; **L** — loader;
**D** — watcher dirty set; **R** — результат. «Прежний» означает неизменность этой
части состояния действием, а не отсутствие конкурирующих внешних событий.
Обычный load/cached load не приравнивается к гарантии flush: отдельно наблюдать
фактический sync production входа. Подготовка сама не потребляет dirty documents.

| Переход | G | S | M | A | L | D | R |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Первый load false/omitted | Новая сессия | Raw snapshot | Нет | Нет overlay generation | Текущий процесс | Состояние новой сессии | Load, без rewrite/execution promise |
| Первый load true, prepare успешна | Новая сессия | Полный overlay | Новый, этой сессии | Подготовленные | Текущий процесс, lazy | Состояние новой сессии; prepare не flush | Prepared/rewritten, execution ещё не наблюдалось |
| Cached true, разрешённая успешная подготовка | Прежний | Полный overlay результата | Проверенный результат той же сессии | Выбранные по content policy | Прежний; U-ARB-02 gate | Не меняется от prepare; flush учитывается отдельно | Refresh/rewrite отдельно от execution |
| Cached true, неподдержанное in-process обновление | Прежний | Неподдержанный результат не публикуется как успех | Подготовка не разрешает исполнение | Нет разрешения активировать заведомо неверную версию | Прежний | Prepare не flush | Явный отказ по выбранному U-ARB-02 режиму |
| Cached true→false/omitted | Прежний | Прежний overlay | Прежний | Прежние | Прежний | Без изменения от флага | Текущее sticky поведение, не disable; U-ARB-05 |
| Cached false→true | Прежний | Overlay по результату подготовки | Новый/частичный результат этой сессии | Успешно подготовленные | Прежний, lazy | Prepare не flush | Prepared/partial/failure отдельно |
| Reset+load | Новая сессия | Новый raw/overlay по флагу | Старый не переносится | Новые только при opt-in | Прежний, assemblies могут жить | Состояние нового workspace | Reopen; execution по U-ARB-02 |
| Process restart+load | Новая сессия | Новый по флагу | Новый при opt-in | Выбранные в новом процессе | Новый | Новая сессия watcher | Новый load; exact execution проверяется отдельно |
| Graph-stale reopen | Новый граф/база | Новый по opt-in | Пересоздаётся для новой базы | Из новой подготовки | Прежний | Относится к новому workspace | Reopen и refresh outcomes раздельны |
| A→B load | Сессия B | B по флагу | Mapping A не применяется к B | Только B при opt-in | Прежний; identity gate | Watcher B | Маркер B либо явный unsupported; broken/off — отсутствие генерации |
| Getter | Прежний | Читает целый S/fallback | Прежний | Прежние | Getter не загружает | Не flush | Нет обещания свежести недоставленного/неflushed текста |
| Text edit, полный успех | Прежний | Принятый новый текст+overlay | Прежний совместимый | Прежние | Прежний | Штатный document sync; не artifact refresh | Успех записи; last refresh сохраняется отдельно |
| Overlay apply, полный успех | Принятые поддержанные изменения | Полный принятый snapshot+overlay | Только совместимые записи mapping для оставшихся проектов | По применимому mapping | Прежний | Штатный document sync | Полный успех записи; не refresh |
| Under-lock apply, полный успех | Как соответствующий apply | Как соответствующий apply | Без prepare | По mapping | Прежний | Штатный document sync | Тот же результат workflow |
| FSW delivery | Прежний; событие графа отдельно помечает stale | Прежний | Прежний | Прежние | Прежний | Добавлены доставленные dirty paths | Delivery не равна публикации |
| Flush, полный успех | Прежний граф | Доставленные и обработанные тексты+overlay | Прежний | Прежние | Прежний | Обработанные dirty paths учтены штатным sync | Freshness в пределах завершённого flush, не всей файловой системы |
| Prepare failure без старого mapping | База текущей сессии | Только результат с исходными ссылками на failed entries | Успешные entries + причины failed entries | Только пригодные, если есть | Прежний | Prepare не flush | Failure/partial; не полный refresh/execution success |
| Failed refresh со старым совместимым mapping | Прежний | Допустим прежний overlay | Допустим прежний mapping | Прежние только с явным stale | Прежний; execution gate сохраняется | Prepare не flush | Failed refresh, stale-generation; последующий edit не стирает отказ |
| Preflight rejection | Прежний | Candidate не публикуется | Прежний | Прежние | Прежний | Нет новых серверных write effects; внешние события остаются внешними | Unsupported/stale, никаких записей операции |
| Partial persistence→reconciliation success | Только фактически принятый project state | Согласованные сохранённые тексты+overlay | Применимый готовый | По mapping | Прежний | Сверить с фактическим document sync, не выдавать необработанное за flushed | Partial с reconciliation success |
| Reconciliation failure/отмена | Не приписывать неприменённый state | Не публиковать весь rejected candidate | Не выполнять prepare | Не активировать новый refresh | Прежний | Успешный flush не утверждается; фактический dirty state включается в проверку fallback | Частичные пути, reconciliation failure; свежесть не подтверждена |
| Clear/dispose | Workspace закрыт | Публикация закрытой сессии снята | Активный mapping сессии снят; operation может удерживать свою ссылку | Нет активного overlay workspace; файлы остаются | Прежний, возможны старые compilation | Watcher закрытой сессии не источник новой | Clear не unload и не cleanup |

Правила низкоуровневого подтверждения dirty paths при отмене не меняются этой
серией; эпоха 1 фиксирует фактическую production политику, эпоха 4 не объявляет
необработанные события синхронизированными. Состояние D проверяется во всех строках
при итоговом аудите; таблица не вводит новый механизм доставки, очередь или retries.

## LC-S3. Инструкция обновления и совместимость

Первое использование: завершить build, затем `load_workspace` с
`shadowCopyInSolutionAnalyzers=true`. Успешный rewrite означает замену ссылок;
исполнение подтверждается при семантическом использовании.

После изменения генератора завершить build и выполнить операцию artifact refresh,
поддержанную выбранным режимом U-ARB-02. Если режим требует restart, нужен новый
процесс сервера; reset+load его не заменяет. До выбора режима нельзя обещать V2
ни по cached load, ни по reset+load.

В отличие от v1.3.5, edit Consumer после эпох 2/4 не перечитывает output генератора
и сохраняет активный mapping. Прежний случайный pickup новых bytes при edit больше
не является workflow обновления; это наблюдаемое изменение совместимости workaround.
При failed refresh старое поколение явно помечено stale, а успех следующего edit
не отменяет эту информацию. Отключение до U-ARB-05 — reset, затем load false/omitted.
