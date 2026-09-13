# Сводка ответов на review

Оценены все **50** замечаний. Предлагаемый результат прохода — **revise до production disk-hit**, с сохранением возможности ограниченного эксперимента. Ни работоспособность гидрации, ни её невозможность, ни полезность на целевой монорепе здесь экспериментально не установлены.

Это оценка критики, а не утверждённый новый план. Исходные v1/v2 и review не редактировались. Код не реализовывался. [Источники и границы evidence](sources-and-scope.md) отделяют первоначальный план от отсутствующего первичного задания пользователя и от принятых ADR-подобных решений.

## Классификация всех замечаний

| Вердикт | Количество |
|---|---:|
| ACCEPT | 10 |
| PARTIALLY ACCEPT | 30 |
| REJECT | 4 |
| NEEDS CLARIFICATION | 6 |

**ACCEPT:**

- [R-03](README.md#r-03).
- [C-01](cache-contract.md#c-01), [C-04](cache-contract.md#c-04).
- [E0-01](epoch-0-feasibility.md#e0-01).
- [E1-02](epoch-1-workspace-lifecycle.md#e1-02), [E1-06](epoch-1-workspace-lifecycle.md#e1-06).
- [E2-03](epoch-2-conservative-disk-cache.md#e2-03).
- [E3-03](epoch-3-live-consistency.md#e3-03), [E3-07](epoch-3-live-consistency.md#e3-07).
- [E4-04](epoch-4-measured-optimizations.md#e4-04).

**PARTIALLY ACCEPT:**

- [R-01](README.md#r-01), [R-02](README.md#r-02), [R-04](README.md#r-04).
- [C-02](cache-contract.md#c-02), [C-05](cache-contract.md#c-05), [C-06](cache-contract.md#c-06), [C-07](cache-contract.md#c-07), [C-08](cache-contract.md#c-08), [C-09](cache-contract.md#c-09).
- [E0-02](epoch-0-feasibility.md#e0-02), [E0-03](epoch-0-feasibility.md#e0-03), [E0-04](epoch-0-feasibility.md#e0-04).
- [E1-01](epoch-1-workspace-lifecycle.md#e1-01), [E1-03](epoch-1-workspace-lifecycle.md#e1-03), [E1-04](epoch-1-workspace-lifecycle.md#e1-04), [E1-05](epoch-1-workspace-lifecycle.md#e1-05), [E1-07](epoch-1-workspace-lifecycle.md#e1-07).
- [E2-01](epoch-2-conservative-disk-cache.md#e2-01), [E2-04](epoch-2-conservative-disk-cache.md#e2-04), [E2-06](epoch-2-conservative-disk-cache.md#e2-06).
- [E3-01](epoch-3-live-consistency.md#e3-01), [E3-05](epoch-3-live-consistency.md#e3-05), [E3-06](epoch-3-live-consistency.md#e3-06), [E3-08](epoch-3-live-consistency.md#e3-08).
- [E4-02](epoch-4-measured-optimizations.md#e4-02), [E4-03](epoch-4-measured-optimizations.md#e4-03).
- [V-02](verification.md#v-02), [V-03](verification.md#v-03), [V-04](verification.md#v-04).
- [H-01](handoff-template.md#h-01).

**REJECT:**

- [C-03](cache-contract.md#c-03).
- [E2-02](epoch-2-conservative-disk-cache.md#e2-02).
- [E4-01](epoch-4-measured-optimizations.md#e4-01).
- [V-01](verification.md#v-01).

**NEEDS CLARIFICATION:**

- [R-05](README.md#r-05), [R-06](README.md#r-06).
- [E0-05](epoch-0-feasibility.md#e0-05).
- [E2-05](epoch-2-conservative-disk-cache.md#e2-05).
- [E3-02](epoch-3-live-consistency.md#e3-02), [E3-04](epoch-3-live-consistency.md#e3-04).

ACCEPT принимает проблему и необходимость исправления; конкретизация исправления дана в ответе. PARTIALLY ACCEPT отделяет подтверждённый пробел от неверной предпосылки или чрезмерной рекомендации. REJECT не означает доказанную готовность всей v2. NEEDS CLARIFICATION означает отсутствие достаточного требования/данных для выбора, а не разрешение исполнителю решить молча.

## Новые и более широкие архитектурные риски

Эти риски выделены отдельно от исходных finding IDs. Они не объявляются новыми требованиями или воспроизведёнными runtime-дефектами, если ниже не указано обратное.

**N1 — неустановленное право менять исходную цель.** V1 требует live freshness после sync (O4), предлагает reuse после изменения sources/membership (O5) и самостоятельные metadata/partial направления (O7). V2 сама говорит, что не отменяет v1. Ни её статус proposed, ни наш ответ не разрешают считать эти цели снятыми. Моя прежняя рекомендация просто вынести Epoch 3 из обязательного маршрута была недостаточно обоснована. Нужен источник первичных требований или решение владельца о приоритетах/поэтапном выпуске. Последствия: acceptance и release scope; A-01/A-02. Связано с R-05/R-06/E0-05/E2-04/H-01.

**N2 — межпроцессное доказательство analyzer provenance не эквивалентно текущему Complete snapshot.** A-PROVENANCE прямо запрещает перенос snapshot между load sessions; [модель](../../../../Services/AnalyzerProvenanceCaptureService.cs:128) хранит LoadSessionId/ProjectId, а [gate](../../../../Services/AnalyzerProvenanceCaptureGate.cs:21) отвергает чужую сессию. Простая подстановка нового sessionId после чтения с диска обошла бы смысл этой проверки. Не определено, какое переносимое evidence и какая повторная валидация дают право создать **новое** same-session подтверждение без DTB. Нужны отдельные правила exact mapping project instances, проверка источника/версий и отрицательные тесты. Если этого нет, opt-in hydrated path не готов, даже если compiler DTO полон. Это шире добавления overlay в key; A-04. Дополнительно Complete analyzer capture не доказывает полноту всего графа и его зависимостей.

**N3 — checksum не устанавливает доверие к исполняемым references.** Contract §8 считает payload недоверенным и запрещает загрузку произвольной DLL только по cache-файлу. Но если путь, hash DLL и «provenance» проверяются исключительно по одному изменяемому manifest, его внутренняя согласованность не подтверждает принадлежность analyzer текущему запрошенному проекту. Это риск проектирования, не проведённый exploit. Нужна явная threat model: cache corruption, запись другим OS principal, same-user modification, trusted storage/permissions и происхождение evidence. Сервер по ARCHITECTURE доверяет клиенту/OS identity и не является sandbox; не следует изобретать защиту от всесильного same-user процесса. Но нельзя обещать stronger untrusted-payload guarantee одним SHA-256. Расширение capture может также сохранить секретные global/environment properties в долговременном DTO, хотя нынешний binlog временный. Нужны правила отбора/хранения/логирования; A-04. Связано с C-01/R-02/E2-01, но безопасность не сводится к свежести.

**N4 — диагностическая метка не должна выдавать недоказанную гарантию.** `ram|disk|msbuild`, completeness, validation mode, semantic admission и факт записи cache — разные измерения. Например, disk DTO найден, но prepare недопустим; strict hashes совпали, но coverage неизвестен; useDiskCache включён, capture ещё не состоялся. Один cache-hit/success bool будет скрывать эти различия. Нужна минимальная, ограниченная по размеру форма результата; не обязательно новый структурированный MCP schema. Последствия: load/semantic metadata, help и tests, не перенос всех подробностей provenance в ответ пользователю. Связано с C-05/E2-03/E4-02/E3-04.

**N5 — baseline sync уже имеет спорную границу членства и декодирования.** Статически подтверждено: [WorkspaceDocumentDiskSync](../../../../Services/WorkspaceDocumentDiskSync.cs:79) выбирает одно membership, читает через ReadAllTextAsync, создаёт SourceText без encoding и добавляет новый source по ближайшему каталогу проекта. [Flush](../../../../Services/SolutionManager.cs:1681) передаёт этот candidate в write boundary даже при `persistDocuments=false`; этот параметр не доказывает отсутствие project-file side effects у TryApplyChanges. Порча `.csproj` в новом сценарии здесь не воспроизводилась. Следует отдельно проверить исключённый glob/linked file/не-UTF8/новый source и project bytes. Это корректирует мой прежний поверхностный вывод о baseline и предпосылку E1-05; нельзя закрыть проблему одним новым hydrator или молча расширить текущую задачу до ремонта runtime.

**N6 — три разных вида атомарности и lifetime.** Atomic pointer в store защищает целостность payload; публикация Solution под lock защищает manager state; ни то ни другое не даёт атомарного снимка файлов и rollback CLR loader. Поздний TextLoader/metadata/analyzer read может потребить другие bytes после validation либо после освобождения lease. Failed candidate способен оставить process-lifetime загруженную assembly, хотя workspace удалён; A-LOADER не обещает выгрузку при reset. Сохранение старого workspace не равно восстановлению прежнего execution state. Capture DTB может сам менять `obj`, что также надо отличать от внешнего writer. Нужны явно заданные lifetime и момент потребления каждого input, snapshot consistency assumptions и тесты side effects до publish. A-WRITE по-прежнему не обещает транзакцию нескольких файлов. Связано с C-02/C-08/E1-02/E3-01/E3-08; решения A-03/A-05/A-06.

**N7 — сравнение старого toolset с собой.** Contract §2 требует фактически выбранный SDK/MSBuild/environment fingerprint. Если probe проверит лишь сохранённые старые пути/версии, он может пропустить изменение выбора SDK при том же project tree, например появление более нового подходящего SDK при разрешённом roll-forward. Это сценарий для проверки, не воспроизведённый дефект. Нужно определить, как получить **текущее** разрешение toolset и значимое environment без ошибочного self-validation и без утечки секретов; изменение → incompatible/unsupported. Это шире file content hashes и C-03. Затрагивает §2/§4/§6, V13/V15, producer compatibility и стоимость probe.

## Предлагаемые изменения при включении принятых замечаний

Все строки — требования к будущей доработке документов/проверок, **не выполненные изменения исходного плана**. Для каждой строки указаны affected sections и downstream effects. Здесь собрана и принимаемая часть PARTIALLY ACCEPT. Имена будущих DTO/public fields в ответах иллюстративны до утверждения API.

| Изменение | Finding IDs | Разделы, которые потребуется изменить | Требуемый результат и последствия |
|---|---|---|---|
| CH-01. Актуализировать baseline | R-03, E1-06 | README «Текущее основание»; Epoch 0 п.1; Epoch 1 «Приёмка/Handoff»; будущие ссылки ARCHITECTURE | Commit/source version отдельно от running binary; U-ARB-04/05 и v3 guarantees; обычный load regression suite обязателен. V1 не объявлять отменённым. |
| CH-02. Выбрать hydrate host и владельца записи | R-01, E0-01, E1-01 | Epoch 0 п.2–4/приёмка; Epoch 1 «Задача/Работа/Приёмка»; contract §1/§3; V07 | Матрица read/text/document/project mutation, capabilities, persist/error/partial outcomes. Public behavior не зависит случайно от host; новый writer или reload-on-write требуют доказательства и compatibility решения A-06. |
| CH-03. Доказать admission и dependency closure | R-02, C-01, E0-02, E2-01 | Contract §3–§5; Epoch 0 п.4–5; Epoch 2 п.3–4; V12/V13 | Positive/negative/target dependencies с источником evidence; known absent paths/regions; unknown rejected до hit. Versioned profile/DTO; binlog не объявляется полным автоматически; evaluation-only не замена target evidence. |
| CH-04. Определить completeness | C-04, E1-07 | Contract §3/§4; Epoch 1 handoff; Epoch 2 capture gate; V01/V03/V05 | Expected roots/instances/edges/inputs, legitimate empty instances и diagnostics policy. Раздельны полнота Roslyn graph, analyzer capture и cache evidence. DTO/probe/tests, обычный load classifier не ломается ради cache. |
| CH-05. Интегрировать overlay admission | R-04, C-05, E1-04 | Contract §2/§3/§7/§9; Epoch 1/2 lifecycle; V08/V09/V23 | Base без shadow paths; после hydrate только разрешённые prepare/gate/publication; requested/effective режим, semantic availability. Sticky/default/main-only/restart сохраняются. Перенос evidence — отдельное решение N2/A-04. |
| CH-06. Специфицировать generation, стабильность и lifetime | C-02, C-08, E1-02, E3-01 | Contract §7/§8; Epoch 1 cancellation; Epoch 2 storage; Epoch 3 refresh; V09/V17/V18/V21 | Coverage до consumed inputs, late dependencies и own-DTB writes; old vs candidate ownership; lazy reads и leases; safe cleanup при paused/crashed reader. Один lock без recursive acquire; никаких обещаний filesystem/CLR rollback. Конкретная consistency policy зависит от A-03. |
| CH-07. Разделить memberships и query context | E1-03 | Contract §3; Epoch 1 membership audit; V06/V07/V20 | Все membership для sync, детерминированный контекст запроса/правки. Проверить разные TFM/defines и порядок при hydrate. Public selector/отказ при ambiguity — отдельный compatibility выбор. |
| CH-08. Закрепить decoding и текстовый oracle | C-09 | Contract §3/§6; V02/V07; Epoch 1 write tests | Effective encoding/BOM/fallback/unspecified, сравнение символов и semantics; checksum alone недостаточен. Document DTO/loader/write policy; baseline sync риск N5 отдельно. |
| CH-09. Уточнить сквозную границу disk reconciliation | E1-05, E1-06 | Contract §7; Epoch 1 audit; Epoch 3 V19; verification release gates | Scanner не пишет project files; намеренная пользовательская mutation отделена от внешнего disk event. Проверить существующий AddDocument path, оба host и fault paths. Не запрещать все project edits blanket-правилом. |
| CH-10. Привязать oracle к реальному production пути | E0-03, E1-07, V-02, V-03 | Epoch 0 «Приёмка»; verification oracle/V04/V11; Epoch 1 handoff | Fresh MSBuild остаётся эталоном. Полный generated set/text плюс marker, недоступность output не passed. Positive admission, negative rejection и fallback reason тестируются отдельно. Compile Remove не обязан давать membership_changed. |
| CH-11. Уточнить workload и performance acceptance | C-06, C-07, E0-04, E2-04, V-04 | Epoch 0 baseline; Epoch 2 «Задача/Приёмка/Handoff»; verification «Измерения/Release gates» | Названное реальное решение, утверждённый budget; unchanged/edit/build/restart, hot calls, все miss/capture attempts, bytes/categories/scan costs. Нельзя выводить нулевой hit-rate без данных или исключать obj ради числа. Допустимость сужения O5 — A-01/A-02. |
| CH-12. Ограничить scope обхода безопасным отказом | C-07, E3-03, E3-07 | Contract §5; Epoch 0 scan baseline; Epoch 3 coverage; V12/V16/V18/V22 | Roots/walk-up/explicit и места появления missing inputs; resource budget/retarget. Превышение → отказ cache, не частичный успешный scan; root sln не равен root glob. |
| CH-13. Разделить source/policy/write status | R-04, C-05, E2-03, E4-02 | Contract §9; Epoch 2 п.1–2/п.6; Epoch 4B; V23 | Bounded effective metadata: cache policy, source, capture/write outcome, validation policy и readiness. При RAM-hit не выдумывать наличие дисковой записи. Нет обязательной новой MCP schema. |
| CH-14. Формализовать live classification и content-only | E3-03, E3-05, E3-07 | Epoch 3 «Изменения»; contract §4/§5; V19–V23 | Раздельные event/probe/prune rules, explicit inputs не теряются; graph role имеет приоритет. Content-only требует отсутствия влияния и на evaluation, и на targets. Unknown внутри потенциального membership не игнорируется. |
| CH-15. Определить notifications, очереди и cadence | E3-06, E3-08 | Epoch 1 own-write path; Epoch 3 notifications/persistence; contract §7/§8; V20/V21 | Подтверждённые собственные bytes/revision, partial writes и pending external change; временное suppress не доказательство echo. Актуальность RAM/индекса отдельно от durable cache. Foreground/background/idle решается A-05, а не выбирается молча. |
| CH-16. Описать non-strict совместимость | E4-02 | Epoch 4B; contract §6/§7/§9; V10/V16/V19–V23 | Честный validation mode, producer evidence→reader policy, strict после слабого режима требует полной проверки. Schema/namespace меняются при несовместимых данных, не автоматически на любой policy flag. |
| CH-17. Зафиксировать scope semantic и test tools | E4-03, E4-04 | Epoch 4A; V24/V25; будущие help/Description | Partial read с coverage либо отказ, если полнота обязательна; refactoring не выполняется вслепую. Syntax test discovery явно неполон, CLI уважает явную цель; binary/project ambiguity не угадывается. |
| CH-18. Развести experiment и production разрешения | E0-04, E2-06, V-04, H-01 | README этапность после A-01; Epoch 2 «Реализация/Приёмка»; verification release gates; handoff все decision sections | Отдельные store correctness/activation checkpoints, обязательные features/evidence/budget и точные разрешённые следующие действия. Изменение MUST не легализуется полем «отклонения». Public rollout после gates, не просто default false. |

Для каждого изменения persistence-формата до выпуска нужно определить schema/producer compatibility и неизвестную версию трактовать как miss. Ни v1, ни v2 cache в текущем runtime не выпущены; необходимость мигрировать их данные не доказана. Уже существующий shadow store `v2-main-only` имеет отдельный lifetime/GC contract и не должен попасть под cleanup нового evaluation cache. Runtime/API version и product docs меняются только при реально выпущенном поведении; в этом проходе version bump отсутствует.

Дополнительные предложения **за пределами исходных findings**: N2 требует portable-evidence decision; N3 — threat model и retention/secret policy; N5 — отдельного baseline repro; N6 — ownership late reads/loader effects; N7 — current toolset resolution test. Они не реализуются попутно с CH-01–CH-18 и не считаются принятыми новым требованием без оценки.

## Неразрешённые вопросы независимому арбитру

Арбитраж должен опираться на первичное задание и evidence. Там, где их нет, решение не заменяется предположением. Численные результаты требуют эксперимента, а не голосования о правдоподобии.

| ID | Вопрос и связанные findings | Что должен разрешить арбитр / какие данные нужны |
|---|---|---|
| A-01 | Статус v1/v2 и допустимое сужение: R-05, E0-05, E2-04, H-01 | Получить первичное задание/подтверждение владельца: O4/O5/O7 обязательны для полного результата или для первого релиза? Можно ли выпускать unchanged-only этап и откладывать live freshness? До этого Epoch 3 не объявлять optional и не считать O5 снятым. |
| A-02 | Репрезентативность профиля: R-06, R-05, E0-04, V-03/V-04 | Названное большое решение, обязательные TFM/SG/import features, смесь запросов и утверждённый budget. 70% — предложение, не восстановленное пользовательское требование. Частота hit и достаточность профиля определяются замерами. |
| A-03 | Гарантия свежести и доступность: C-02, E3-01/E3-02/E3-04, E4-02 | Stable-tree assumptions, наблюдение silent lost events, допустимость stale/unknown read, поведение write и budget hot path. Нельзя выбирать watcher trust как strict; сохранение Banned/Unavailable независимо от freshness обязательно до отдельного пересмотра A-ADMISSION. |
| A-04 | Новый источник доверия при межпроцессном hydrate: R-04, C-05, N2/N3/N7 | Какая повторная проверка позволяет создать новый session-bound provenance без DTB, как подтверждаются DLL/current toolset и как защищается evidence. Нужен capture/hydration spike и threat model; менять sessionId в старом DTO без доказательства недопустимо. |
| A-05 | Capture/persistence scheduling: E2-05, E3-06, C-08 | Foreground vs background, момент ответа, latency/write-rate/стабильность, отмена/reset/shutdown и lifetime reader. Idle не выбран автоматически; нужен замер successful captures по всей нагрузке. |
| A-06 | Mutating contract другого host: R-01, E1-01/E1-02 | Требуется ли весь нынешний edit API без дополнительного DTB; допустим ли явный reload-on-write; кто сохраняет project mutations и как сообщает partial writes. Отсутствие решения блокирует production hydrate, но не доказывает невозможность публичных API. |
| A-07 | Оставшееся методологическое расхождение с V-01 | Мы отвергаем запрет сравнения разных host: fresh MSBuild должен остаться независимым эталоном, production hydrate — объектом проверки. Если автор review сохраняет возражение, арбитр должен назвать конкретное наблюдаемое свойство, которое такое сравнение не проверяет, а не требовать одинаковый CLR type. |
| A-08 | Граница «общего интерпретатора»: R-02, E2-01, E4-01 | Мы отвергаем тождество любой закрытой модели и общего MSBuild interpreter. Если спор сохраняется, нужен перечень допустимых конструкций/API и проверяемый способ reject неизвестного, с учётом первоначального O5. Экспериментальная выполнимость closure отдельно не доказана. |

C-03 опровергнут прямым чтением null/Debug comparison; E2-02 — наличием conjunctive acceptance в самой спецификации. Пока не представлено отличающееся evidence или другая версия, эти два вопроса не требуют выбора архитектуры арбитром. Принятые здесь вердикты не означают, что автор review уже согласился с ними.

## Проверка этого пакета

Пакет содержит девять файлов ответов с той же структурой имён, что review, плюс `sources-and-scope.md` и эту сводку. Для всех 50 IDs заданы ровно один вердикт, обоснование, изменения либо явно сформулированная неопределённость и downstream consequences. Общие зависимости вынесены в N/A/CH, но ни один finding не заменён только ссылкой на другой.

Выполнена статическая сверка плана, review, исходников и указанных публичных API. Build/test/hydration/performance проверки не запускались: этот проход не реализует код. Проверка полноты IDs, ссылок и неизменности исходных документов — отдельная проверка качества ответов, не доказательство работоспособности будущего кэша.

Результат проверки пакета: 50 исходных IDs ↔ 50 ответов, пропусков/лишних IDs/дубликатов нет; каждый ответ имеет вердикт, основание, изменения или запрос уточнения и последствия. Локальные ссылки и ссылки сводки на finding anchors существуют. SHA-256 всех 23 исходных файлов v1/v2/review совпали с зафиксированными до записи ответов. `git diff --name-only` не показал изменений отслеживаемых файлов; добавлен только новый каталог ответов, существующий untracked каталог review сохранён.
