# Ответы: Epoch 1

Исходные замечания: [review/epoch-1-workspace-lifecycle.md](../review/epoch-1-workspace-lifecycle.md). O/A: [основания](sources-and-scope.md).

## E1-01

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Независимость результата записи от загрузчика — допустимая архитектурная цель O8, но она не достигается заменой MSBuildWorkspace на Adhoc. A-WRITE требует отдельного preflight/persist/apply/reconcile workflow. Нужна конкретная реализационная граница. Тезис «требования взаимоисключающие» слишком силён: file persistence может принадлежать отдельному компоненту, либо операция может требовать явно определённого reload. Это варианты для feasibility, не выбранное решение. `rename_project` уже использует [файловый helper](../../../../Tools/ProjectTools.cs:53); code fix AddDocument остаётся существенным контрпримером.

**Предлагаемые изменения.** Epoch 1 «Работа/Приёмка», contract §1/§3: операция → capability → владелец disk write → отказ/partial status. V07 разнести на text-only, add/remove/rename document и project changes, не проверять только patch существующего текста. Не снимать требование корректной записи без согласования scope.

**Последствия.** Внутренний persistence API, сохранение A-WRITE, возможная latency reload-on-write и проверка всех public adapters. Disk cache DTO не должен становиться командой записи `.csproj`. Не обещать транзакционный rollback нескольких файлов (N6).

## E1-02

**Вывод: ACCEPT.**

**Основание.** [LoadCoreAsync](../../../../Services/SolutionManager.cs:1498) dispose-ит старую сессию до нового load. Формула «failure сохраняет прежнюю» означает изменение поведения, не готовую гарантию. A-ADMISSION дополнительно запрещает превращать opt-in failure в raw/no-overlay успех. Сохранение старого объекта не доказывает, что его можно выдать за состояние нового запрошенного решения.

**Предлагаемые изменения.** Epoch 1 cancellation/publication, contract §7 и V09: таблица переходов same-key refresh/другой key/новая opt-in сессия/cancel/failed prepare. Разделить удержание ресурсов старой сессии и разрешение публикации. Указать session identity, stale и admission после каждого исхода. Если сохраняется кандидат до publish — описать владение и disposal; это не требует автоматически отпускать A-LOAD lock.

**Последствия.** Lifetime и peak memory двух workspace, reference ownership, current loaded-path metadata, rejection старых write contexts и concurrency tests. Автоматический fallback на старое решение другого key недопустим без явного контракта. Неоткатываемое process loader state — отдельный N6; сохранение старого workspace его не решает.

## E1-03

**Вывод: PARTIALLY ACCEPT.**

**Основание.** `FindDocumentAsync` действительно выбирает первый контекст, а [WorkspaceDocumentDiskSync](../../../../Services/WorkspaceDocumentDiskSync.cs:141) обновляет первый membership. Последнее мешает O4 и V06. Но выбор контекста запроса и рассылка нового текста всем memberships — разные операции. Полная карта memberships не требует автоматически менять каждый semantic API или агрегировать несовместимые code fixes разных TFM.

**Предлагаемые изменения.** Epoch 1 membership audit и contract §3: явно разделить path→memberships для синхронизации и выбор project/TFM для query/edit. V06/V07: разные defines, linked file, обновление всех membership и детерминированный выбор operation context. Если вводится выбор project/TFM пользователем либо отказ при неоднозначности — вынести как отдельное изменение публичного поведения.

**Последствия.** Domain model memberships, deterministic mapping при hydrate и query results; tests на обычном и hydrated host. Смена порядка восстановленных проектов не должна случайно менять selected context. Возможный public context selector требует compatibility решения; его обязательность этим finding не доказана. N5 отдельно охватывает существующий sync.

## E1-04

**Вывод: PARTIALLY ACCEPT.**

**Основание.** «Все пути получают overlay» двусмысленно без условия активации; A-STICKY и ARCHITECTURE запрещают автоматический opt-in. Однако при совместном чтении с запретом изменения defaults план не требует включить overlay всем. Write boundary уже существует, но применимость её host capabilities к hydrate должна быть проверена.

**Предлагаемые изменения.** Epoch 1 «Работа»: все semantic readers получают разрешённый published snapshot; overlay применяется только при соответствующем session admission. Сохранить exact inverse/freshness A-WRITE и U-ARB-05. V08 включает on/off, cached false/omitted, new session и failed opt-in, а не только successful true.

**Последствия.** Regression matrix, publication API и metadata эффективного режима. Новых defaults/автоактивации не требуется. Поддержка hydrate не означает права обойти ban или session-bound provenance; это R-04/C-05/N2.

## E1-05

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Запрет scanner-induced project writes полезен как сквозной O8-инвариант и должен повторно проверяться при новом scanner. Но утверждение review, что текущий sync работает только с известными документами и риска ещё нет, неверно в общем случае: [ApplyAsync](../../../../Services/WorkspaceDocumentDiskSync.cs:120) добавляет неизвестный dirty source в ближайший проект, а [flush](../../../../Services/SolutionManager.cs:1681) передаёт candidate в write boundary. `refreshAll` перечисляет known documents, но обычная dirty-ветка шире. Project-file side effect этим проходом не воспроизводился.

**Предлагаемые изменения.** Contract §7/verification release gates закрепляют инвариант серии; Epoch 1 аудит проверяет существующую dirty-ветку; Epoch 3 V19 повторяет проверку для scanner/overflow. Не запрещать пользовательский AddDocument blanket-правилом: он относится к намеренной записи, в отличие от отражения уже случившегося disk change.

**Последствия.** Классификация происхождения мутации, byte-level `.csproj` tests и собственные notifications. Новый N5 должен быть отдельной задачей оценки baseline, а не молчаливым расширением cache реализации.

## E1-06

**Вывод: ACCEPT.**

**Основание.** Изменение production SolutionManager влияет на пользователей даже при выключенном disk cache. O8/A-LOAD/A-WRITE не допускают подтверждать такой refactor только тестовым hydrator. «Нет disk-hit» не равно «нет production-изменений».

**Предлагаемые изменения.** Epoch 1 статус/«Приёмка/Handoff»: явно отделить test-only hydrator от shipped lifecycle refactor. V01–V09 и существующие U-ARB-04, stale-write, admission tests прогоняются через обычный load и через будущий production hydrate workflow с теми же write tools. Не маркировать runtime изменения экспериментом только потому, что cache flag ещё отсутствует.

**Последствия.** Двойная regression matrix, release/version/product documentation по реально выпущенному поведению. Public schema может остаться прежней, но cancellation/publication совместимость всё равно меняется и должна быть описана. В этом проходе только предлагается изменение acceptance, код не трогается.

## E1-07

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Проверки одного `Documents` недостаточно; это верно. Но v2 contract §3 уже включает options/references/provenance/additional/configs, а verification задаёт независимый oracle. Кроме того, `.props` — вход получения графа, а не обязательно документ, который требуется гидратировать в Roslyn. Нельзя смешивать полноту DTO compiler inputs и полноту invalidation dependencies.

**Предлагаемые изменения.** Epoch 1 handoff и verification: две явные таблицы «семантические данные для hydrate» и «входы/evidence для admission и последующей валидации», с источником каждой категории и состоянием теста. Ссылка на отсутствие temp paths не заменяет ни одной таблицы. Полноту negative dependencies решать совместно с C-01, не имитировать сравнением DTO с собой.

**Последствия.** Traceability отчёта, schema completeness и две группы тестов. Потеря props в dependency evidence блокирует reuse; отсутствие props среди обычных Documents само по себе не ошибка. Нет необходимости превращать все imports в Roslyn Documents или сериализовать их тексты.
