# Изменения v1 → v2

Ревизия спецификации от 2026-09-11. Эпоха 3 реализована в **v1.3.7**
(restart-required / main-only); эпоха 4 **принята** в **v1.3.8** (write boundary);
эпоха 6 — **аудит документации завершён** ([epoch-6-acceptance.md](epoch-6-acceptance.md)).
Эпоха 5: E5-S1 измерено; U-ARB-01 выбран как capture на load (запас Alt-2/Alt-3);
эскиз F-09 [принят](epoch-5-f09-acceptance.md); rollout ждёт P0-spike.
Серия не завершена. Исходные
v1/review/response/arbitration сохранены. Файлы эпох переписаны как самостоятельный
русский нормативный текст, добавлены общая lifecycle matrix и реестры
трассировки/открытых вопросов.

## Материальные изменения по принятым решениям

По каждому finding указан окончательный verdict Sol. Частичное принятие
не означает принятия всех предложений review. Следствия распределены между
указанными разделами, а не ограничены исходным абзацем.

| Finding / verdict | Материальное изменение | Разделы v2 |
| --- | --- | --- |
| [R-01](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Разделены факты v1.3.5, цели и release gates; неизменяемость сохранена как цель эпохи 2. | README: текущее поведение, целевые инварианты, эпохи |
| [R-02](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Единое обещание согласованности разложено на целостность, freshness после flush и одну базу операции; добавлена инвентаризация всех semantic readers. | README: инвариант 2; [E1-S4](epoch-1-lifecycle-verification.md); [E4-S1/S2](epoch-4-workspace-write-boundary.md); [LC-S1/S2](LIFECYCLE-v2.md); [E6-S2](epoch-6-contract-and-documentation.md) |
| [R-03](../arbitration/finding-verdicts.md) — ACCEPT | Эпоха 4 зависит от mapping эпохи 2; независимый CLR binding gate эпохи 3 сохранён. | README: эпохи; [E2-S1](epoch-2-immutable-shadow-copies.md); [E4-S1](epoch-4-workspace-write-boundary.md) |
| [E1-01](../arbitration/finding-verdicts.md) — ACCEPT | Oracle выполняется в изолированном production test host с MSBuild bootstrap и читает точный IFieldSymbol.ConstantValue; новый публичный MCP API не нужен. | [E1-S1/S5](epoch-1-lifecycle-verification.md); [E3-S1](epoch-3-loader-contract-and-dependencies.md) |
| [E1-02](../arbitration/finding-verdicts.md) — ACCEPT | Неопределённый полный reload заменён тремя операциями: cached load, reset+load и process restart, с независимым учётом графа, artifact refresh и исполнения. | README: термины; [E1-S2](epoch-1-lifecycle-verification.md); [E2-S1](epoch-2-immutable-shadow-copies.md); [E3-S1](epoch-3-loader-contract-and-dependencies.md); [LC-S1–S3](LIFECYCLE-v2.md); [E6-S2/S3](epoch-6-contract-and-documentation.md) |
| [E1-03](../arbitration/finding-verdicts.md) — ACCEPT | A/B same-identity тест проверяет реально исполненный маркер и loaded path/identity; broken-path B с flag off проверяет отсутствие генерации. | [E1-S2/S4](epoch-1-lifecycle-verification.md); [E3-S1/S5](epoch-3-loader-contract-and-dependencies.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| [E1-04](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Добавлены true→false, true→omitted, false→true и reset-варианты; sticky/reset поведение сохраняется, будущая семантика не выбрана. | README: v1.3.5; [E1-S2](epoch-1-lifecycle-verification.md); [E2-S1](epoch-2-immutable-shadow-copies.md); [LC-S1–S3](LIFECYCLE-v2.md); [U-ARB-05](UNRESOLVED-v2.md) |
| [E1-05](../arbitration/finding-verdicts.md) — ACCEPT | Consumer использует generated member и не меняется при V1→V2; отрицательный контроль исключает ложный успех diagnostics. | [E1-S1](epoch-1-lifecycle-verification.md); [E3-S1](epoch-3-loader-contract-and-dependencies.md); [E5-S4](epoch-5-reference-provenance.md) |
| [E1-06](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Три write paths названы и проверяются независимо: text edit, overlay apply, watcher+flush; каждый требует маркер, текст и неизменные project bytes. | [E1-S3](epoch-1-lifecycle-verification.md); [E4-S2/S4](epoch-4-workspace-write-boundary.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| [E1-07](../arbitration/finding-verdicts.md) — ACCEPT | Добавлен existing-correct-path fixture к missing-path; после семантики каждого write path требуется forced output-writing rebuild и actual load path. | [E1-S3](epoch-1-lifecycle-verification.md); [E3-S5](epoch-3-loader-contract-and-dependencies.md); [E4-S3/S4](epoch-4-workspace-write-boundary.md); [U-ARB-04](UNRESOLVED-v2.md) |
| [E1-08](../arbitration/finding-verdicts.md) — ACCEPT | FSW delivery отделена от production flush: bounded ожидание dirty event, затем flush и assertions; минимум один реальный watcher test. | [E1-S3](epoch-1-lifecycle-verification.md); README: инвариант 2; [LC-S1/S2](LIFECYCLE-v2.md) |
| [E1-09](../arbitration/finding-verdicts.md) — ACCEPT | Добавлена loaded-shadow→edit→failed reapply регрессия и сохранение результатов; target edit исключает preparation и не теряет активный mapping. | [E1-S2/S5](epoch-1-lifecycle-verification.md); [E2-S1/S4/S5](epoch-2-immutable-shadow-copies.md); [E3-S4](epoch-3-loader-contract-and-dependencies.md); [E4-S2](epoch-4-workspace-write-boundary.md); [LC-S2](LIFECYCLE-v2.md) |
| [E1-10](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Несколько Consumer стали обязательной матрицей 1/2; helper experiments допускаются после exact oracle и разделяют preparation/binding/execution. | [E1-S2/S4](epoch-1-lifecycle-verification.md); [E2-S5](epoch-2-immutable-shadow-copies.md); [E3-S2](epoch-3-loader-contract-and-dependencies.md) |
| [E2-01](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Main-only политика эпохи 2 отделена versioned namespace от dependency-set; identity включает политику и ordered path/hash set; helper-only gate перенесён в эпоху 3. | [E2-S2](epoch-2-immutable-shadow-copies.md); [E3-S2/S5](epoch-3-loader-contract-and-dependencies.md); [E6-S3](epoch-6-contract-and-documentation.md) |
| [E2-02](../arbitration/finding-verdicts.md) — ACCEPT | Каждый обещанный refresh перечитывает и хеширует bytes даже на cached graph и одинаковых размере/timestamp; edit вообще не проверяет analyzer files. | [E2-S1/S2/S5](epoch-2-immutable-shadow-copies.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| [E2-03](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Manifest пишется последним после закрытия файлов; same-volume no-replace move, полная проверка reuse, отказ без удаления/замены повреждённого destination; crash-durable транзакция не обещается. | [E2-S3/S5](epoch-2-immutable-shadow-copies.md) |
| [E2-04](../arbitration/finding-verdicts.md) — ACCEPT | Preparation ограничена load/enable и явными refresh/reopen; edit/flush/reconciliation/post-apply только reapply. Failed refresh и допустимый stale mapping имеют отдельный результат. | [E2-S1/S4/S5](epoch-2-immutable-shadow-copies.md); [E4-S2](epoch-4-workspace-write-boundary.md); [LC-S1–S3](LIFECYCLE-v2.md); [E6-S3](epoch-6-contract-and-documentation.md) |
| [E2-05](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Сохранён shared per-solution root; обязательна двухпроцессная гонка с завершением publisher и проверенным reuse выжившим. | [E2-S3/S4/S5](epoch-2-immutable-shadow-copies.md); [E1-S4](epoch-1-lifecycle-verification.md) |
| [E2-06](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Добавлены измерения объёма/роста, операционный бюджет, disk-full, владение и cleanup после остановки всех владельцев; clear не удаляет поколения, auto-GC не добавлен. | [E2-S4/S5](epoch-2-immutable-shadow-copies.md); [E3-S5](epoch-3-loader-contract-and-dependencies.md); [E6-S3](epoch-6-contract-and-documentation.md); [LC-S2](LIFECYCLE-v2.md) |
| [E3-01](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Безусловный in-process V2 заменён evidence gate выбора точно ограниченного in-process либо restart-required режима с отказом от неподдержанного исполнения. | [E3-S1/S5](epoch-3-loader-contract-and-dependencies.md); [E2-S4](epoch-2-immutable-shadow-copies.md); [LC-S1–S3](LIFECYCLE-v2.md); [U-ARB-02](UNRESOLVED-v2.md) |
| [E3-02](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Discovery проверяется сначала на явном main/helper fixture; production требует доказанного источника набора или явного ограничения поддержки, без blanket output DLL copy. | [E3-S2](epoch-3-loader-contract-and-dependencies.md); [E2-S2](epoch-2-immutable-shadow-copies.md); [U-ARB-03](UNRESOLVED-v2.md) |
| [E3-03](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | До условного ALC prototype требуется точный shared host contract list, совместимость версий и запрет private contract copies; positive/negative type-identity tests. | [E3-S3/S5](epoch-3-loader-contract-and-dependencies.md); [U-ARB-03](UNRESOLVED-v2.md) |
| [E3-04](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Конфликтующие helpers поддерживаются только при proven requester/generation scope либо явно отвергаются; instrumentation resolver и cleanup по реальному владельцу, не по workspace clear. | [E3-S3/S5](epoch-3-loader-contract-and-dependencies.md); [E2-S4](epoch-2-immutable-shadow-copies.md); [LC-S2](LIFECYCLE-v2.md); [U-ARB-03](UNRESOLVED-v2.md) |
| [E3-05](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Разделены завершение исследования и приёмка выбранной реализации; restart может быть финальным режимом, но измеренный failure сам по себе не является готовностью. | [E3-S1/S5](epoch-3-loader-contract-and-dependencies.md); [E6-S4](epoch-6-contract-and-documentation.md); [U-ARB-02](UNRESOLVED-v2.md) |
| [E3-06](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Prepared/rewrite/load failed/execution observed разделены; lazy loading сохранена, rewrite count не обещает исполнение, first-use ошибки коррелируются с project/generation/dependency. | README: состояние; [E3-S4/S5](epoch-3-loader-contract-and-dependencies.md); [E2-S4](epoch-2-immutable-shadow-copies.md); [LC-S2](LIFECYCLE-v2.md); [E5-S3](epoch-5-reference-provenance.md) |
| [E4-01](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Введён минимальный operation context base snapshot+session+mapping; stale preflight до side effects без snapshot history/MVCC/merge engine. | [E4-S1](epoch-4-workspace-write-boundary.md); [E2-S1](epoch-2-immutable-shadow-copies.md); [E1-S4](epoch-1-lifecycle-verification.md); README: сквозные границы |
| [E4-02](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Unknown analyzer diff отвергается до всех серверных записей; known overlay снимается точно, unknown изменения не стираются и не передаются workspace. | [E4-S1/S2/S4](epoch-4-workspace-write-boundary.md); [LC-S1/S2](LIFECYCLE-v2.md); [E6-S3](epoch-6-contract-and-documentation.md) |
| [E4-03](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Persistence boundary отделена от load isolation; existing-path load/lock matrix и raw readers inventory обязательны, redesign только после локализованной утечки. | [E4-S3/S4](epoch-4-workspace-write-boundary.md); [E1-S3/S4](epoch-1-lifecycle-verification.md); [E3-S5](epoch-3-loader-contract-and-dependencies.md); [U-ARB-04](UNRESOLVED-v2.md) |
| [E4-04](../arbitration/finding-verdicts.md) — ACCEPT | Определены preflight rejection, full success, partial persistence, reconciliation success/failure; structured internal status и cancellation/per-file tests, без multi-file rollback promise. | [E4-S2/S4](epoch-4-workspace-write-boundary.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| [E4-05](../arbitration/finding-verdicts.md) — ACCEPT | Точный inverse использует mapping эпохи 2, сохраняет unrelated refs/порядок/кратность, покрывает added/removed projects; whole-list wipe и temp-prefix inference не подтверждают происхождение. | [E4-S1/S4](epoch-4-workspace-write-boundary.md); [E2-S1](epoch-2-immutable-shadow-copies.md); README: эпохи |
| [E4-06](../arbitration/finding-verdicts.md) — ACCEPT | Общий workflow включает post-apply/reconciliation публикацию overlay по mapping без analyzer I/O; все три входа и fallback проверяются на маркер/текст/project bytes. | [E4-S2/S4](epoch-4-workspace-write-boundary.md); [E2-S1/S5](epoch-2-immutable-shadow-copies.md); [E1-S3](epoch-1-lifecycle-verification.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| [E5-02](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Добавлена таблица exact output, confirmed stale path, proven foreign, missing, inaccessible, ambiguity и missing source; недоступность не приравнивается отсутствию, открытые действия не выбраны. | [E5-S2](epoch-5-reference-provenance.md); [E1-S2](epoch-1-lifecycle-verification.md); [U-ARB-01](UNRESOLVED-v2.md) |
| [E5-03](../arbitration/finding-verdicts.md) — ACCEPT | Выбор идёт по фактически loaded inner Project, global properties и resolved output; unevaluated TargetFrameworks не создаёт выбор или ложную неоднозначность. | [E5-S1/S2/S4](epoch-5-reference-provenance.md) |
| [E5-04](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Стабильные внутренние reason codes и раздельные original/source path states, включая honest provenance_unconfirmed; публичная сводка остаётся компактной. | [E5-S3](epoch-5-reference-provenance.md); [E3-S4](epoch-3-loader-contract-and-dependencies.md); [E2-S4](epoch-2-immutable-shadow-copies.md); [U-ARB-01](UNRESOLVED-v2.md) |
| [E5-05](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Foreign same-name marker fixture и исходный missing-path repro обязательны для rollout; алгоритмическая независимость 5 от 2–4 не означает готовность серии. | [E1-S2](epoch-1-lifecycle-verification.md); [E5-S1/S4](epoch-5-reference-provenance.md); README: эпохи |
| [E6-01](../arbitration/finding-verdicts.md) — ACCEPT | Добавлены action/current/target/verification matrix и upgrade note о смене recopy-on-edit на mapping reuse, трёх операциях загрузки и отсутствии CLR unload при reset. | [LC-S1–S3](LIFECYCLE-v2.md); [E6-S3](epoch-6-contract-and-documentation.md); [E2-S1/S2](epoch-2-immutable-shadow-copies.md) |
| [E6-02](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Создана ранняя задача DOC-EARLY для трёх точных targets recompute-on-read; защищённые внешние артефакты этим проходом не меняются, эпоха 6 аудирует задачу. | [E6-S1](epoch-6-contract-and-documentation.md); README: текущее поведение и эпохи |
| [E6-03](../arbitration/finding-verdicts.md) — ACCEPT | Матрица покрывает реальные lifecycle ветки и семь состояний G/S/M/A/L/D/R, включая partial mapping, fallback, dirty delivery/flush и clear. | [LC-S1/S2](LIFECYCLE-v2.md); [E6-S2](epoch-6-contract-and-documentation.md); [E1-S2/S4](epoch-1-lifecycle-verification.md); [U-ARB-05](UNRESOLVED-v2.md) |
| [E6-04](../arbitration/finding-verdicts.md) — ACCEPT WITH MODIFICATION | Эпоха 6 только аудит; reuse принимается в 2/4. Аудит допускает explicit deferred 3, но не закрывает незавершённую runtime-работу всей серии. | [E6-S2/S4](epoch-6-contract-and-documentation.md); [E2-S5](epoch-2-immutable-shadow-copies.md); [E4-S4](epoch-4-workspace-write-boundary.md); README: статус |

## Закрытые и открытые решения

- R-04 — REJECT. Разделение файловой целостности и CLR binding сохранено; изменения
  терминов cached load относятся к принятому E1-02, а не к принятию R-04.
- E2-07 — REJECT. Content identity уже требовалась v1; её уточнение выполнено по
  E2-01/E2-02, дополнительный loader контракт из E2-07 не вводился.
- E5-01 — UNRESOLVED. Противоречивые безусловные требования заменены явным gate
  U-ARB-01; ни эвристика, ни skip-all, ни сужение repro не выбраны.
- U-ARB-01–05 полностью сохранены в [UNRESOLVED-v2.md](UNRESOLVED-v2.md).
  U-ARB-01 **выбран** (capture на load); F-09 эскиз принят, канал ждёт P0;
  U-ARB-02/03 **выбраны** (restart-required / main-only, эпоха 3).
  U-ARB-04/05 остаются открытыми gates. Эпоха 6 аудирует документы, не закрывает
  эти gates.

## Сквозные следствия принятых решений

- N-01 (E4-02/04/06): preflight не даёт multi-file transaction; full/partial и
  reconciliation статусы согласованы в E4-S2 и LC-S2.
- N-02 (R-02, E4-01/05): одна база операции и её mapping; никакой неограниченной
  истории snapshots или повторного получения базы после symbol resolution.
- N-03 (E1-02/04, E6-03): проверка concurrency dispatch и внутренняя привязка
  load→enable→summary к intended session; новый public token не введён.
- N-04 (E1-07, E4-03): write и load boundaries проверяются независимо; конкретная
  утечка не объявлена фактом и новый backend не выбран.
- N-05 (E2-03/05/06): safe manifest paths, ownership, shared root, no-replace
  publication и cleanup после остановки владельцев; хеши не названы trust boundary.
- N-06 (E1-04/09, E2-04, E3-06): requested/prepared/active/last refresh/execution
  разделены; успешный edit не стирает failed refresh и не обещает новую генерацию.
- N-07 (E1-03, E2-05, E3-01/04): процессная изоляция loader сценариев, кроме
  намеренной последовательности same-process reload; shared-publication использует два процесса.

Эти риски уже приняты арбитражем через cross-cutting constraints и не объявлены
новыми POST-ARB findings. Migration использует отдельный versioned namespace без
in-place обновления старого cache; optional PDB, отсутствие auto-build, публичного
SG search, automatic GC и общего MVCC сохранены из v1. Новых численных SLA,
публичных схем, authorization/messaging подсистем и выбора ALC не добавлено.

## Два прохода

Интеграция разнесла решения по модели, lifecycle, подготовке, записи, loader,
matcher, ошибкам, приёмке и совместимости. Проверка согласованности сверила полный
набор документов, 42 verdicts, 71 нормативный пункт и 10 сквозных ограничений;
подробное покрытие — в [TRACEABILITY-v2.md](TRACEABILITY-v2.md).
Редакционные неоднозначности сняты: I/O-free относится к reapply анализаторов,
а не чтению текстов/lazy load; partial persistence проверяет сохранённый subset,
а не невыполненный полный запрос. Новых существенных вопросов не выявлено.
