# Вердикты по findings

`ACCEPT WITH MODIFICATION` означает: проблема подтверждена, но обязательная
граница изменения уже или уже сформулированной Grok. `PARTIALLY ACCEPT` из
ответов Astra не используется как финальный вердикт.

## README

### R-01 — ACCEPT WITH MODIFICATION

Grok верно обнаружил, что host гидрации и владелец persistence не выбраны, а
эквивалентность чтения не доказывает эквивалентность операций записи. Его
трилемма неполна: отдельный persistence adapter или явно ограниченный
reload-on-write возможны, хотя ещё не доказаны. Следующая редакция обязана до
production go выбрать host, карту capabilities и поведение каждой мутации; ни
read-only downgrade, ни custom writer не разрешаются молча.

### R-02 — ACCEPT WITH MODIFICATION

Без доказанного источника positive, negative и target dependencies безопасный
hit невозможен. Но любой вызов MSBuild или ограниченная закрытая модель не
тождественны запрещённому общему интерпретатору. Требуется versioned admission
protocol с reject-before-hit для unknown; binlog нельзя считать полным
автоматически.

### R-03 — ACCEPT

Baseline 1.3.5 устарел и способен вернуть уже устранённые lifecycle/write
дефекты. Следующая редакция привязывается к commit, отдельно фиксирует source и
running binary version и сохраняет A-LOAD/A-STICKY/A-WRITE/A-ADMISSION/
A-PROVENANCE/A-LOADER.

### R-04 — ACCEPT WITH MODIFICATION

Capture base, session overlay и semantic readiness смешаны. Disk graph source не
означает готовую semantic-сессию. Однако новый PID без opt-in законно остаётся
no-overlay; overlay не надо автоматически включать или обязательно включать в
ключ base DTO. Требуются requested/effective mode, prepare/gate до publication и
переносимый provenance contract.

### R-05 — ACCEPT WITH MODIFICATION

Epoch 2 действительно не выполняет исходные O4/O5 и не может называться
завершением исходной цели. Но из этого не следует удалять Epoch 3: O4 делает live
freshness обязательной частью полного результата. Epoch 2 разрешён только как
unchanged-restart эксперимент/промежуточный checkpoint; production completion
требует согласованного закрытия O4 и O5.

### R-06 — ACCEPT WITH MODIFICATION

Одно-TFM fixture не доказывает пригодность для целевой большой нагрузки, но
универсальный multi-targeting не установлен как обязательный первый профиль.
До production go нужно назвать целевое решение и обязательные TFM/instance
contexts. Если они multi-target, exact mapping становится gate; иначе ограничение
профиля должно быть явным и не выдаваться за поддержку иных больших решений.

## Cache contract

### C-01 — ACCEPT

Контракт требует обнаруживать зависимости, для которых не задаёт источник
evidence. Snapshot обязан хранить доказанное множество существующих,
отсутствующих и потенциально появляющихся входов; неизвестная полнота отключает
hit всего запроса.

### C-02 — ACCEPT WITH MODIFICATION

Событие из непокрытого источника невозможно наблюдать, но неполноту coverage
можно установить после discovery. Требуется разделить discovery, coverage,
capture и validation, описать consumed bytes, late/lazy reads и DTB-записи в
`obj`. Filesystem snapshot не предписывается; строгий hit запрещён, если выбранная
модель стабильности не доказана.

### C-03 — REJECT

Сценарий основан на неверном факте: текущая нормализация сохраняет `null`, а
RAM-key различает отсутствующее значение и `"Debug"`. Из finding не следует
снимать absent/explicit distinction или менять ключ. V15 всё равно обязан
проверять эту семантику.

### C-04 — ACCEPT

Отсутствие blocking diagnostic не доказывает полный граф. Нужен отдельный
completeness predicate для ожидаемых roots, instances, edges и compiler inputs,
с поддержкой легитимно пустого проекта. Ordinary load health, analyzer capture и
cache completeness не объединяются в один флаг.

### C-05 — ACCEPT WITH MODIFICATION

Успешная гидрация base graph не равна успешному prepare/admission overlay.
Обязательны раздельные source/policy/readiness состояния. Base DTO может быть
общим для on/off только при доказанном разделении; старый session-bound
provenance нельзя «перепривязать» заменой session ID.

### C-06 — ACCEPT WITH MODIFICATION

Частая инвалидация `obj` — правдоподобный риск, но «любой build → miss» не
доказан. Все реально значимые входы остаются hashed. Epoch 0 обязан измерить
no-op build, build и edit+build+restart по категориям изменившихся bytes; провал
бюджета ведёт к revise, а не к исключению входа без доказательства.

### C-07 — ACCEPT WITH MODIFICATION

Конус может совпасть почти со всей монорепой; произвольное усечение несовместимо
с correctness. Нужно задать roots/walk-up/explicit regions, resource budget и
bounded fallback. Превышение бюджета означает отказ cache probe, не частичный hit.

### C-08 — ACCEPT WITH MODIFICATION

Reader protection не специфицирован, но PID+heartbeat — не единственный
допустимый механизм. Следующая редакция обязана определить acquisition, lifetime,
lazy reads, cleanup, pause/crash/PID reuse и безопасный отказ GC.

### C-09 — ACCEPT WITH MODIFICATION

«Encoding при необходимости» недостаточно. DTO и loader обязаны воспроизводить
effective decoding/BOM/fallback/null policy и round-trip write. Одного checksum
недостаточно: oracle сравнивает символы, policy и semantics.

## Epoch 0

### E0-01 — ACCEPT

Host и operational capabilities должны быть первым feasibility gate. Создание
`ProjectInfo` не доказывает persistence, а Open+подмена не считается skip-DTB.

### E0-02 — ACCEPT WITH MODIFICATION

Для каждого unsupported случая нужен detector до hit; negative admission test не
является passed equivalence test. Обязательные positive features определяются
целевой нагрузкой, а не удобством fixture.

### E0-03 — ACCEPT WITH MODIFICATION

Diagnostics/name search не доказывают generated output. Публичный MCP tool не
нужен: существующий in-process oracle следует расширить до полного generated set,
текстов, marker и diagnostics; unavailable output не может дать success.

### E0-04 — ACCEPT WITH MODIFICATION

E2E включает probes, capture, optional prepare, compilation и первый полезный
semantic call. 70% — предложение, не авторитетный порог. Functional experiment
может продолжиться без большой нагрузки, но не разрешает production hit или
утверждение пользы.

### E0-05 — ACCEPT WITH MODIFICATION

Metadata fast-open сохранён в исходном маршруте O7, поэтому его нельзя молча
вынести за пределы исследования. В Epoch 0 он остаётся отдельным comparator с
собственным outcome и измерениями; его результат не является evidence для
full-source disk hydrate и не меняет cache profile.

## Epoch 1

### E1-01 — ACCEPT WITH MODIFICATION

Правильная проблема — отсутствие выбранного persistence contract для другого
host. Требуется матрица operation→capability→writer→result для text,
add/remove/rename document и project mutation. Требование корректной записи не
снимается; конкретный adapter/reload должен пройти feasibility и A-WRITE.

### E1-02 — ACCEPT

Текущее dispose-before-open противоречит обещанию сохранить старую сессию.
Нужна таблица переходов same-key, other-key, cancel и failed prepare, ownership
двух кандидатов и admission каждого исхода. Старый workspace другого key нельзя
публиковать автоматически.

### E1-03 — ACCEPT WITH MODIFICATION

Все memberships нужны для disk sync, но это не предписывает агрегировать
семантику разных TFM. Следует раздельно определить path→memberships и
детерминированный query/edit context; публичный selector или ambiguity error
требуют отдельного compatibility решения.

### E1-04 — ACCEPT WITH MODIFICATION

Overlay остаётся session-sticky opt-in. Все readers получают только разрешённый
published snapshot; hydrate не обходит existing prepare/gate, exact inverse,
ban/unavailable или main-only policy. Нужна on/off/omitted/reset/restart матрица.

### E1-05 — ACCEPT WITH MODIFICATION

Инвариант «disk reconciliation не пишет project files» сквозной, а не только
Epoch 1. При этом текущая dirty-ветка уже может создавать candidate с новым
Document. Нужны byte-level baseline tests и повтор V19; намеренный AddDocument не
запрещается как класс.

### E1-06 — ACCEPT

Refactor production `SolutionManager` является production change даже при
выключенном cache. Обычный load и hydrate проходят одну regression matrix,
включая U-ARB-04, stale-write и admission.

### E1-07 — ACCEPT WITH MODIFICATION

Нужны две раздельные таблицы: semantic data для hydrate и dependency evidence
для admission. Documents-only comparison и temp-path check не доказывают ни одну
из них.

## Epoch 2

### E2-01 — ACCEPT WITH MODIFICATION

«Сомнение → miss» не защищает от неизвестного входа. Hit разрешён только через
доказанный admission/evidence protocol C-01. Evaluation-only допустима как
измеряемый механизм, но не считается автоматически полной для target inputs.
V12 сохраняется.

### E2-02 — REJECT

Спецификация уже требует одновременно zero-DTB, equivalence matrix и perf gate с
first-semantic e2e. Закрытие эпохи по одному счётчику было бы нарушением текста,
а не его разрешённым следствием.

### E2-03 — ACCEPT

Включённая policy не означает существующую disk generation. Ответ обязан
различать effective policy, source, capture attempt и write result, включая
`not-attempted: unverified-ram`; скрытый force capture не требуется.

### E2-04 — ACCEPT WITH MODIFICATION

Miss после source edit делает Epoch 2 только unchanged-restart checkpoint.
Исключать source hash без доказательства нельзя. Полный маршрут обязан измерить
и затем закрыть O5 отдельным content/membership reuse contract до объявления
цели выполненной.

### E2-05 — UNRESOLVED

Текст не выбирает foreground или background capture, а evidence частоты гонок и
допустимой latency отсутствует. Решение отложено в U-ARB-01.

### E2-06 — ACCEPT WITH MODIFICATION

Store correctness и product activation — разные checkpoints. Публичный hit/flags
разрешены только после admission, equivalence и perf gate; reader protection и
corruption fallback нельзя откладывать после activation.

## Epoch 3

### E3-01 — ACCEPT WITH MODIFICATION

Одного запрета recursive lock недостаточно, но deadlock и unlocked publication —
не единственные варианты: under-lock внутренний workflow совместим с A-LOAD.
Нужна точная lock/queue/publication диаграмма и concurrency matrix. Hint-only
не заменяет O4.

### E3-02 — UNRESOLVED

Strict hash/scan на каждый semantic call может уничтожить hot path, а blanket
watcher trust не доказывает O4/O8. Требования не задают cadence обнаружения silent
lost events и допустимый overhead. Решение отложено в U-ARB-02.

### E3-03 — ACCEPT

Любой unknown/editor temp не должен вызывать DTB. Нужна role/coverage
классификация explicit, potential membership/negative, proven irrelevant и
unknown; relevant unknown даёт untrusted, irrelevant игнорируется.

### E3-04 — UNRESOLVED

Всегда выдавать last trusted выбирает availability, всегда отказывать выбирает
freshness. Требования не определяют допустимость unknown read и совместимость с
Banned/Unavailable. Решение отложено в U-ARB-02; stale/unknown write всё равно
запрещён A-WRITE.

### E3-05 — ACCEPT WITH MODIFICATION

Content-only разрешён только для поддержанной document role при доказанной
независимости evaluation и targets от bytes. Graph role имеет приоритет; missing
document не может считаться applied.

### E3-06 — ACCEPT WITH MODIFICATION

RAM/index freshness, validity старой generation и cadence durable capture —
разные состояния. Полная запись после каждой правки не следует из требований,
но старое поколение обязано давать miss. Cadence зависит от U-ARB-01.

### E3-07 — ACCEPT

Текущий watcher не покрывает ancestor и explicit `obj` inputs. Нужна coverage
map, grouped watch strategy, resource limits и переход в untrusted при
недоступном/потерянном watcher.

### E3-08 — ACCEPT WITH MODIFICATION

Time-window suppression может скрыть external write. Own-write path должен
фиксировать фактически сохранённые bytes/hash и revision; несовпадение или
нестабильность оставляют pending/untrusted. Hash сверяет состояние, но не
доказывает автора события.

## Epoch 4

### E4-01 — REJECT

Ограниченная доказанная модель closed subset не является общим MSBuild
интерпретатором. 4C остаётся допустимым направлением при unknown→load и полной
equivalence matrix; это не доказывает выполнимость конкретной модели.

### E4-02 — ACCEPT WITH MODIFICATION

Strict/stat/watcher guarantees должны быть видимы и совместимы. Отдельная schema
нужна только при несовместимой форме evidence; strict reader может полностью
перепроверить weak payload. Effective validation mode не смешивается с semantic
admission.

### E4-03 — ACCEPT WITH MODIFICATION

Partial keys необходимы, а expand cost допустим для явно partial режима. Однако
каждый semantic/refactoring tool обязан иметь contract partial result либо
отказа; полнота не обеспечивается одним hint.

### E4-04 — ACCEPT

Нужно явно развести syntax test discovery по загруженному graph и CLI target на
диске, сообщать coverage/requested roots и не угадывать неоднозначный project/
binary.

## Verification и handoff

### V-01 — REJECT

Fresh MSBuild — независимый oracle, production hydrate host — объект проверки.
Одинаковый CLR host скрыл бы общую ошибку. Нормализация не может скрывать
semantics/persistence differences.

### V-02 — ACCEPT WITH MODIFICATION

V11 разделяется по reason: membership change, graph input change и non-member
control. Fallback equality остаётся обязательной, но не заменяет positive hydrate.

### V-03 — ACCEPT WITH MODIFICATION

Supported generator обязан пройти full generated-output oracle; rejected profile
проходит только admission test. Обязательность SG в первом positive profile
определяется названной целевой нагрузкой.

### V-04 — ACCEPT WITH MODIFICATION

Release gate требует фактического прохождения заранее согласованного e2e budget и
hit-rate на названной репрезентативной нагрузке. Fixture-only разрешает experiment,
но не public activation.

### H-01 — ACCEPT WITH MODIFICATION

Handoff обязан раздельно разрешать experiment, implementation, activation и
следующую эпоху. Отклонение не отменяет MUST/O4/O5/A-WRITE. Для каждого profile
нужны positive/negative/not-run evidence и явный список разрешённых следующих
действий.
