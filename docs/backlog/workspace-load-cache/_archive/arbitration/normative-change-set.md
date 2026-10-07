# Нормативный набор изменений

Этот файл содержит только изменения, обязательные для следующей редакции
proposal. Не разрешённые вопросы находятся в [unresolved.md](unresolved.md) и не
могут быть выбраны исполнителем спецификации.

## 1. Статус, цель и этапность

1. Перебазировать «Текущее основание» на commit
   `9867318ddb5294ce144bf024b9a61a1a2e3814c3`, source version 1.3.21.
   Running MCP version записывать отдельно и не угадывать. Сохранить ограничения
   A-LOAD, A-STICKY, A-WRITE, A-ADMISSION, A-PROVENANCE и A-LOADER.
   [R-03, E1-06]
2. Не объявлять соседний v1 отменённым. Сохранить исходные цели O1–O8, в том
   числе O4 live freshness, O5 reuse после изменения source/membership и O7
   metadata/partial направления. [R-05, E2-04, E0-05]
3. Назвать Epoch 2 `unchanged-restart checkpoint`: он доказывает только reuse
   полностью неизменного поддержанного дерева. Его experiment/implementation не
   означает завершение исходной цели или разрешение public activation.
   [R-05, E2-04]
4. Полный основной маршрут обязан закрыть O4 и O5. Конкретный механизм O5 не
   предписывается; 4C остаётся допустимым только как доказанная closed-subset
   модель с unknown→ordinary load. Запрет общего MSBuild interpreter сохраняется.
   [R-05, E2-04, E4-01]
5. До production go назвать реальное целевое решение, обязательные SDK/TFM,
   project-instance contexts, generators/imports и workload. Если target требует
   multi-targeting, exact inner-instance mapping является gate. Иное ограничение
   профиля явно не обобщается на «большие решения». [R-06, E0-02, V-04]
6. Разделить решения: `experiment allowed`, `implementation allowed`,
   `public activation allowed`, `next epoch allowed`, `series complete`.
   Ни одно поле `go` не заменяет остальные. [E0-04, E2-06, H-01]

## 2. Термины и состояния

1. Определить и использовать единообразно:
   - `request identity`;
   - `compatibility fingerprint`;
   - `base graph source`: `ram | disk | msbuild`;
   - `cache policy`: requested/effective;
   - `capture status` и `write status`;
   - `validation mode`;
   - `overlay mode`: requested/effective;
   - `semantic readiness`;
   - `cache completeness`;
   - `generation` и `publication`.
   [R-04, C-04, C-05, E2-03, E4-02]
2. Не объединять ordinary load health, cache completeness, analyzer-provenance
   completeness и semantic readiness в один bool. [C-04, C-05]
3. Load/semantic metadata должны быть bounded и сообщать фактические policy,
   graph source, validation mode, capture/write outcome, overlay mode и readiness.
   RAM-hit не должен подразумевать наличие disk generation. [E2-03, E4-02]
4. Сохранить session-sticky overlay: false/omitted в same-key RAM session не
   отключают активный режим; reset/new key/restart создают новую сессию. Disk
   hydrate не включает overlay автоматически. [R-04, C-05, E1-04]

## 3. Host, domain model и persistence

1. В Epoch 0 до production go выбрать production hydrate host и доказать, что
   hydrate path не скрывает DTB. `Open*`+подмена не считается skip-DTB.
   [R-01, E0-01]
2. Добавить capability matrix:
   `read`, `text edit`, `add/remove/rename document`, `project mutation`,
   `disk reconciliation`; для каждой операции указать host capability, владельца
   disk write, preflight, result/failure/partial semantics и необходимость reload.
   [R-01, E1-01]
3. Не разрешать custom project writer, read-only downgrade или reload-on-write
   только ради прохождения acceptance. Выбранный вариант должен сохранить
   согласованный public scope и A-WRITE либо явно заблокировать production go.
   [R-01, E1-01]
4. Разделить:
   - `path -> all memberships` для disk sync;
   - детерминированный project/TFM context для query/edit.
   Порядок проектов после hydrate не может менять выбранный context случайно.
   Публичный selector/ambiguity error оформляется отдельным compatibility
   решением, если потребуется. [E1-03]
5. Зафиксировать сквозной инвариант: отражение внешнего disk event не создаёт
   project mutation и не переписывает `.csproj`; намеренная пользовательская
   AddDocument/project mutation проходит capability/write contract. Проверить
   существующую unknown-dirty-source ветку byte-level тестом. [E1-05]
6. Разделить две таблицы данных:
   - semantic DTO, необходимый для hydrate;
   - dependency/admission evidence, необходимый для reuse.
   Import не обязан становиться Roslyn Document, но его отсутствие в evidence
   запрещает reuse. [E1-07]

## 4. Dependency evidence, profile и completeness

1. Snapshot/admission DTO должен versioned-образом представлять:
   - positive dependencies;
   - known-absent paths/conditions;
   - ограниченные regions появления wildcard/negative inputs;
   - target-produced compiler inputs;
   - источник и версию evidence для каждой категории.
   [R-02, C-01, E2-01]
2. Для каждого support-profile правила admission должны доказуемо отличать
   `supported`, `unsupported` и `unknown` до hydrate/publication и до загрузки DLL
   из DTO. Unknown отключает disk-hit всего запроса. [C-01, E0-02, E2-01]
3. Binlog, resolved Documents/imports или имя SDK не объявляются полным
   evidence автоматически. Evaluation-only измеряется отдельно и не считается
   доказательством target-produced inputs. [R-02, E2-01]
4. Формальный cache-completeness predicate включает все ожидаемые roots,
   inner instances, directed edges и обязательные compiler inputs. Источник
   expected set должен быть указан. Законно пустой project/instance допускается
   явной policy; отсутствие hard diagnostic само по себе не доказывает полноту.
   [C-04]
5. Для каждого profile хранить раздельные результаты:
   `positive equivalence passed`, `negative admission passed`, `not run`.
   Unsupported fixture не засчитывается как equivalence. [E0-02, V-03]
6. Не менять ordinary `HasBlockingLoadFailure` classifier ради cache admission.
   Ordinary load может быть доступен, когда capture запрещён. [C-04]

## 5. Snapshot, decoding и analyzer provenance

1. Capture выполняется только из base solution без shadow paths. Snapshot не
   сериализует session IDs, `ProjectId` как переносимую identity или temp paths.
   После hydrate допустимы только разрешённые provenance reconstruction,
   prepare, gate и затем atomic publication по A-LOAD. [R-04, C-05]
2. Старый session-bound analyzer provenance нельзя сделать валидным заменой
   `LoadSessionId`. Межпроцессный portable evidence и его повторная валидация
   должны быть доказаны до overlay-ready disk hit. [C-05]
3. Для Documents задать effective encoding, BOM handling, fallback, null/
   unspecified representation и write-back policy. Непредставимый случай
   profile отвергает. SourceText/generated text не сериализуются. [C-09]
4. Oracle сравнивает decoded character sequence, encoding policy, semantics и
   bytes после round-trip edit; checksum используется только дополнительно.
   Fixtures: non-UTF8 без BOM, BOM, invalid bytes. [C-09]
5. Все реально значимые generated compile/config/provenance inputs, включая
   explicit `obj` inputs, остаются content-hashed. Нельзя исключить input только
   потому, что он часто меняется. [C-06]

## 6. Generation, capture и publication lifecycle

1. Разделить protocol на:
   dependency discovery → establishing coverage → capture/hydrate →
   validation → prepare/gate → publication. Для каждого шага указать владеемые
   resources, generation и cancellation result. [C-02, E1-02]
2. Описать, какие bytes фактически consumed DTB/hydrator, когда материализуются
   SourceText/metadata/analyzers, какие lazy reads живут после publication и как
   lease защищает их. Поздний обнаруженный input или недоказанное coverage
   запрещают reusable capture. [C-02, C-08]
3. Отличать atomic store pointer, atomic manager publication и filesystem
   consistency. Не обещать rollback файлов или CLR loader state. [C-02, E1-02]
4. Для same-key refresh, different key, cancel, load failure и prepare failure
   задать state transition table: old/candidate ownership, loaded path/key,
   stale state, admission, publication, disposal. Старый workspace другого key
   не является fallback. [E1-02]
5. Production lifecycle refactor проходит обычный MSBuild path и hydrate path с
   одной regression matrix; test-only hydrator не подтверждает production.
   [E1-06]
6. Capture scheduling не фиксировать до U-ARB-01. Спецификация должна оставить
   явный decision gate и одинаковый safety predicate для foreground/background.
   [E2-05, E3-06]

## 7. Store и межпроцессное владение

1. До public activation выбрать reader-ownership protocol: acquire/release,
   owner identity, long pause, crash, PID reuse, heartbeat/эквивалент, cleanup
   race и failure-to-prove-owner. Expiration сама по себе не разрешает delete.
   [C-08]
2. Защита поколения длится до последнего payload/lazy read, а не только до
   возврата метода hydrate. Cleanup при сомнении пропускает deletion/new write.
   [C-08]
3. Store correctness checkpoint включает immutable complete generation,
   corruption/oversize/no-space/permissions, competing readers/writers и safe
   cleanup. Эти гарантии нельзя отложить после public activation. [E2-06]
4. Product activation checkpoint дополнительно требует dependency admission,
   semantic equivalence и пройденный performance gate на целевой нагрузке.
   Параметры не публикуются только потому, что store tests зелёные. [E2-06, V-04]
5. Новый evaluation cache имеет собственный namespace/lifetime и не очищает
   analyzer shadow generations с A-LOADER contract. [C-08]

## 8. Scan, probes и resource bounds

1. Задать отдельно membership roots, walk-up inputs, explicit dependencies,
   negative/potential regions и symlink/junction retarget policy. Root solution
   directory не считается автоматически membership root каждого проекта.
   [C-07]
2. Resource budget включает visited entries, bytes, time, cancellation и I/O
   concurrency. Его превышение даёт bounded fallback/unsupported, не успешный
   усечённый scan. [C-07]
3. Prune ограничивает recursion, но не explicit probes/watch. Explicit input в
   `obj`, ancestor directory или вне root остаётся проверяемым. [C-07, E3-07]
4. Event classifier имеет роли:
   `known explicit`, `potential membership/negative`, `proven irrelevant`,
   `unknown coverage`. Editor temp/unrelated файл не вызывает DTB; relevant
   unknown переводит state в untrusted/graph-dirty. [E3-03]
5. Content-only разрешён лишь для существующей поддержанной Document category,
   если profile доказал независимость evaluation и DTB targets от bytes. При
   совмещённых ролях graph role приоритетна. Missing document не даёт applied.
   [E3-05]

## 9. Live consistency, locking и own writes

1. Epoch 3 должен содержать точную схему:
   acquire → under-lock classify/refresh/load/prepare/gate/publish → release.
   Internal under-lock методы не захватывают semaphore повторно. Указать, какой
   immutable snapshot semantic operation удерживает после accessor и как write
   preflight проверяет его generation. [E3-01]
2. Добавить concurrency tests: simultaneous semantic/edit/reset, graph refresh,
   stale candidate, cancel, event during flush и no recursive acquire. Сохранить
   A-LOAD/A-WRITE; MVCC/merge не вводить. [E3-01]
3. До U-ARB-02 не выбирать strict-every-call, watcher-trust или stale-read
   policy. Текст обязан показывать unresolved gate, а не противоречащие друг
   другу MUST. [E3-02, E3-04]
4. Watch/probe coverage map включает project regions, walk-up, explicit paths и
   места появления absent inputs. Определить grouping, watch limits, startup,
   overflow/error/cancel. Недоказанное coverage переводит state в untrusted.
   [E3-07]
5. Own-write notification записывает только фактически сохранённые paths/bytes,
   content hash и revision. Partial persistence сообщает только успешные paths.
   Time-window suppression не удаляет более позднее событие по тому же пути;
   нестабильность/несовпадение оставляет pending/untrusted. [E3-08]
6. Разделить live RAM/index freshness, validity старой disk generation и cadence
   новой durable capture. Старое несовпадающее поколение обязано дать miss, но
   полная disk write после каждой правки не предписывается. [E3-06]

## 10. Optional validation, partial graph и tool contracts

1. Для strict/stat/watcher определить effective guarantees и compatibility
   `producer evidence -> reader policy`. Strict reader после weak validation
   выполняет полную проверку либо miss. Schema/namespace меняется только при
   несовместимой форме evidence. [E4-02]
2. Partial/metadata mode имеет отдельные canonical roots и cache keys. Для
   каждого semantic/refactoring tool указать: допустимый partial result с
   coverage либо обязательный отказ/full-scope request. Refactoring не
   выполняется вслепую по одному hint. [E4-03]
3. `get_test_list` сообщает syntax-based coverage, requested roots и неполноту.
   CLI build/test следует явно заданной цели на диске, не partial semantic graph.
   Project/binary ambiguity не разрешается выбором первого совпадения.
   [E4-04]

## 11. Verification и performance

1. Fresh `MSBuildWorkspace` с теми же effective properties/toolset/mode остаётся
   независимым oracle для production hydrate host. Нормализуются случайные IDs и
   допустимые temp paths, но не semantics, generated output, persistence или
   encoding differences. [V-01]
2. Расширить source-generator oracle: полный generated document set и texts,
   exact marker/constant и diagnostics. Недоступный обязательный output =
   failed/not-run, не success. [E0-03, V-03]
3. V11 разделить:
   - add/delete member при неизменных old hashes/csproj → membership reason;
   - Compile Remove/graph input → graph reason;
   - создание non-member → no invalidation;
   - каждый miss → no disk publication и корректный fallback.
   Positive unchanged hit проверяется отдельно. [V-02]
4. Добавить completeness fixtures: missing root/reference, wrapped soft warning,
   legally empty project; admission fixtures: unknown condition/custom target и
   known absent path becoming present. [C-01, C-04]
5. Добавить scan/watcher fixtures: root project worst case, ancestor props,
   explicit `obj`, custom-extension AdditionalFile, temp file, junction retarget,
   watcher unavailable/overflow. [C-07, E3-03, E3-07]
6. Добавить generation/store fixtures: paused reader, PID reuse, crash, lazy read,
   competing cleanup, no space, corrupt/oversize payload. [C-08, E2-06]
7. Измерения включают disabled baseline, absent-cache miss+capture, disk hit,
   RAM hit, source edit, no-op build, build, edit+build+restart, live unchanged/
   content/graph/overflow. Для всех attempts считать source, hit/miss reason,
   capture success, changed input categories, bytes, probe/capture/prepare/
   first-semantic, peak memory. [C-06, E0-04, V-04]
8. Budget и workload утверждаются до результатов. Public activation требует
   фактического прохождения e2e budget и приемлемого hit-rate на названной
   нагрузке; fixture-only позволяет только experiment. [E0-04, V-04]

## 12. Handoff

Каждый handoff обязан содержать:

1. Отдельные verdicts для experiment, implementation, activation, next epoch и
   series completion.
2. Обязательные requirement IDs/features и явно согласованные изменения scope.
3. Host/persistence capability matrix.
4. Hydration-data и admission-evidence tables.
5. Positive/negative/not-run test outcomes без смешения.
6. Target workload, заранее выбранный budget, raw metrics и gate result.
7. Schema/producer compatibility, lifecycle/ownership, public contract и
   migration impact.
8. Точный список разрешённых следующих действий. Поле «отклонения» не разрешает
   отменить MUST, O4/O5 или A-WRITE. [H-01]
