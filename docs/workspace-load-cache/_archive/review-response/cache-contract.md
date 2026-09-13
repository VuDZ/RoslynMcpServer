# Ответы: контракт кэша

Исходные замечания: [review/cache-contract.md](../review/cache-contract.md). Обозначения O/A: [основания](sources-and-scope.md).

## C-01

**Вывод: ACCEPT.**

**Основание.** O2/O8 требуют miss при непроверяемой зависимости. Сканер может обнаружить появление по известному отсутствовавшему пути или изменение известной области, но не вывести все потенциальные входы из списка уже открытых imports. Поэтому contract §4/§5 задаёт гарантию без достаточного описания её доказательства. Наличие binlog в текущем runtime не гарантирует полноту negative dependencies.

**Предлагаемые изменения.** В §3 добавить обязательное представление существования/отсутствия, областей появления и источника evidence; §4 определить распознавание закрытого профиля; §5 заменить «находит новые отрицательные зависимости» на проверку заранее обоснованного множества и отказ при недоказанной полноте. Epoch 0 п.4–5 и V12 должны проверять условный import вне известных документов и неизвестную конструкцию до hit. Допустим статически закрытый профиль, но его закрытость тоже надо доказать.

**Последствия.** Admission/DTO/probe API и версия профиля; при недостаточном evidence весь запрос unsupported. Tests включают не только hit/miss, но и причину/полноту множества. Изменений public tools для самой проверки не требуется. Не принимать binlog как самоочевидный полный источник; N2/N3 отдельно.

## C-02

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Событие из непокрытого источника нельзя ждать, однако пробел покрытия можно выявить сравнением обнаруженных dependencies с покрываемым множеством. Именно этот предикат отсутствует в §7. Второй hash после DTB не доказывает, какие bytes использовал сам DTB. Текущий [watcher](../../../../Services/SolutionManager.cs:1713) запускается после load; его нельзя использовать как готовое доказательство capture stability. Контракт уже признаёт ABA/TOCTOU, поэтому утверждать, что он обещает filesystem transaction, неверно.

**Предлагаемые изменения.** §7 разделить discovery, establishing coverage, capture и validation. Описать момент начала покрытия, проверяемую связь consumed inputs с DTO, позднее обнаружение входа, самоизменение `obj` в DTB и lazy reads при hydrate. При недоказанном покрытии — без reusable capture. Выбрать допустимую модель внешних writers до обещания strict hit; filesystem snapshot — кандидат, не обязательная новая инфраструктура.

**Последствия.** Generation model, ownership текстов/metadata, retry/cancel budget и V21. A-LOAD обеспечивает атомарность публикации, но не атомарность файлов. Недоказуемую стабильность нельзя скрыть флагом strict. N6 и вопрос арбитража A-03 отдельно охватывают поздние чтения и модель стабильности.

## C-03

**Вывод: REJECT.**

**Основание.** Конкретный failure scenario неверен. [Normalize](../../../../Services/DotNetConfigurationArguments.cs:15) возвращает null при отсутствии значения, не Debug. [IsSameLoadCache](../../../../Services/MsBuildWorkspaceProperties.cs:33) сравнивает nullable strings; null и `"Debug"` не равны. [LoadCoreAsync](../../../../Services/SolutionManager.cs:1466) сохраняет requested properties и применяет этот предикат. Поэтому описанный второй load не проходит RAM-hit. `buildArgs` по O6/A-PROVENANCE и contract §2 — CLI state, пока не участвует в DTB; отсутствие его в ключе обосновано разделением subsystems. Overlay имеет отдельную sticky-политику, рассмотренную C-05.

**Предлагаемые изменения.** Снимать различие absent/explicit или менять RAM semantics на основании этого finding не следует. При реализации disk key следует выполнить уже предусмотренный V15 с отсутствующими/явными значениями и одинаковой нормализацией; это не подтверждение заявленного дефекта.

**Последствия.** Нет необходимой миграции ключа, breaking change или нового public API. Общий вопрос расширения identity при новых properties остаётся, но не доказывается приведённой коллизией.

## C-04

**Вывод: ACCEPT.**

**Основание.** [HasBlockingLoadFailure](../../../../Services/SolutionManager.cs:1597) и [WorkspaceDiagnosticFormatter](../../../../Diagnostics/WorkspaceDiagnosticFormatter.cs) классифицируют диагностику загрузки; это не доказательство полного запрошенного графа. A-PROVENANCE `Complete` также означает результат специализированного capture, не completeness всего cache DTO. Отсутствие hard diagnostic недостаточно для admission. При этом предлагать непустой Compile/documents для каждого проекта слишком жёстко: нужно отличать корректный пустой проект от проваленной загрузки.

**Предлагаемые изменения.** §3/§4 и Epoch 2 перед capture: формальный предикат «все ожидаемые roots/instances/edges и обязательные compiler inputs получены», с источником ожидаемого множества, разрешёнными empty instances и отдельным diagnostic policy. V01/V03/V05 дополнить пропущенным root/reference, wrapped warning и законно пустым проектом.

**Последствия.** DTO completeness evidence, admission reason и тесты диагностик. Не менять нынешний load success classifier только ради cache capture; ordinary load может остаться доступным, а capture — отказать. Нельзя объединять load health, analyzer capture completeness и cache completeness в один bool (N2).

## C-05

**Вывод: PARTIALLY ACCEPT.**

**Основание.** A-LOAD/A-ADMISSION не разрешают считать граф готовой semantic-сессией до prepare/gate. Но новый PID без флага по A-STICKY законно начинает no-overlay и при свежем MSBuild. Межпроцессное восстановление прежнего opt-in не было требованием. Обязательность overlay-флага в ключе базового DTB snapshot не следует из требования корректного session mode: один DTO может обслуживать несколько режимов, если preparation/evidence действительно отделены.

**Предлагаемые изменения.** §2/§3/§9: отдельно requested/effective overlay, base identity и semantic-ready status; §7/Epoch 2: успешный disk load требует завершения разрешённой подготовки. Уточнить fail-closed/fallback для непригодного cross-process provenance без снятия A-ADMISSION. Проверить on/off/sticky/restart и повторный enable после disk hydrate.

**Последствия.** Load response, внутренний publication result и переносимый provenance contract; вероятно новый producer/DTO version. Нельзя просто переписать sessionId в старом snapshot или автоматически включить overlay. Выбор ключа остаётся зависимым от доказанной модели; N2 — отдельный блокер интеграции.

## C-06

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Потенциальная частая инвалидация `obj` — реальный эксплуатационный риск O1. Но запись файла не означает изменения bytes, а strict SHA-256 не реагирует на одно изменение mtime. Утверждение «любой build → miss» и нулевой hit-rate без замеров не доказано. Хеши файлов — проверка пригодности, не состав RequestIdentity; исключение обязательного compile input противоречит O8.

**Предлагаемые изменения.** §3/§6 сохранить проверку всех значимых inputs, но перечислить конкретные категории generated compile/config/provenance файлов и причины их изменения. Epoch 0/verification «Измерения» и Epoch 2 handoff: отдельные no-op build, обычный MCP build и edit+build+restart; показать bytes/категории, вызывающие miss. Не назначать blanket miss по самому факту build.

**Последствия.** Телеметрия по категориям, реалистичный workload и решение о пользе. Если бюджет не проходит — revise профиля/цели, а не дырка в hashes. Live наблюдение `obj` рассматривается E3-07; content reuse требует отдельного доказательства O5.

## C-07

**Вывод: PARTIALLY ACCEPT.**

**Основание.** O3 не гарантирует маленького конуса. Root `.csproj` с широким glob может покрыть почти всю монорепу; ограничение «project directories» само по себе это не исправляет. Но root `.slnx` не означает, что все проекты имеют glob от корня: review смешивает watcher root и membership roots. Полноту нельзя обеспечивать усечением обхода до произвольного лимита с последующим hit.

**Предлагаемые изменения.** §5 и Epoch 0 baseline: явные roots, walk-up, explicit paths, retarget policy и измеренный worst case с root project. Задать ресурсный бюджет; превышение завершает cache probe отказом/fallback, а не подтверждает неполный scan. V12/V16/V18 проверяют полноту и bounded отказ.

**Последствия.** Bounded I/O/cancellation, метрики visited entries/bytes, cache admission и причины miss. O3/O8 сохраняются; возможен неподдержанный слишком широкий профиль. Никакого переноса этого лимита на обычную MSBuild загрузку автоматически не следует.

## C-08

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Протокол активных readers до выпуска нужен. Однако PID+start time+heartbeat — не единственное решение, и один timeout heartbeat не доказывает смерть процесса: возможна длительная пауза. §8 допускает эквивалентные механизмы; требование не удалить используемое поколение важнее выбора lease implementation. Также нужно определить, читает ли lazy hydration payload после формального завершения hydrate.

**Предлагаемые изменения.** §8/Epoch 2: выбрать платформенный протокол владения, acquisition относительно cleanup, release/abandonment, crash и отказ проверки владельца, lifetime lazy reads. Если используется lease — описать heartbeat и безопасное поведение при его просрочке, а не считать expiration безусловным разрешением удаления. V17/V18: paused reader, PID reuse, crash и долгая гидрация.

**Последствия.** Store metadata/locks, ограничение retention, отказ от новой записи при невозможности безопасного GC. Старый несовместимый формат — miss. Дисковый GC не должен распространяться на shadow generations с отдельной A-LOADER политикой; N6.

## C-09

**Вывод: PARTIALLY ACCEPT.**

**Основание.** «Encoding при необходимости» не определяет воспроизводимое декодирование и будущую запись. Этот пробел принимается. Но предложенный oracle только по `SourceText.GetChecksum()` недостаточен: при создании из stream checksum вычисляется по исходным bytes и может совпасть при разном декодировании. Это видно в [SourceText.From(Stream), строки 198–207](https://source.dot.net/Microsoft.CodeAnalysis/Text/SourceText.cs.html). Поэтому исправление review требует усиления, а утверждение о выборе encoding из editorconfig во всех случаях здесь не подтверждено.

**Предлагаемые изменения.** §3/§6: правила effective encoding/BOM/fallback и representation unspecified/null; непредставимый случай unsupported. Verification V02/V07: сравнивать последовательность символов, encoding policy и семантику; checksum/algorithm — дополнительно. Fixtures: non-UTF8 без BOM, BOM, невалидные bytes, round-trip edit. SourceText не сериализуется в дисковый cache.

**Последствия.** Document DTO/loader, write encoding policy, compatibility version и bytes-after-edit tests. [Текущий disk sync](../../../../Services/WorkspaceDocumentDiskSync.cs:95) отдельно читает ReadAllTextAsync и создаёт SourceText без encoding — это более широкий N5, а не доказательство, что одной правки DTO достаточно.
