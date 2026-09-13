# Контракт workspace load cache v2

Статус: **normative proposal**. Невыполнимое MUST блокирует admission или public
activation; оно не заменяется эвристикой.

## 1. Термины и независимые состояния

- **Request identity**: нормализованный workspace path, реально переданные global
  properties с различием absent/explicit, graph scope и canonical roots,
  semantic mode.
- **Compatibility fingerprint**: schema/producer, Roslyn, SDK/MSBuild resolution,
  ОС/path rules, language/profile и allowlisted significant environment.
- **Base graph source**: `ram | disk | msbuild`; сообщает происхождение base,
  но не readiness, overlay или наличие disk generation.
- **Cache policy** и **overlay mode**: у каждого есть requested/effective value.
- **Capture status**: `not-attempted | running | succeeded | rejected | failed`.
- **Write status**: `not-attempted | written | skipped | failed`, с bounded reason.
- **Validation mode**: фактически применённая гарантия reader.
- **Semantic readiness**: candidate прошёл prepare/admission и опубликован для
  semantic tools.
- **Cache completeness**: доказана полнота roots, instances, edges и compiler
  inputs для profile.
- **Generation**: immutable version base/evidence; **publication**: atomic смена
  manager/store pointer, не filesystem transaction.

Ordinary load health, cache completeness, analyzer-provenance completeness и
semantic readiness не объединяются в один bool. `cacheHit` не заменяет их.

Trace: R-04, C-04, C-05, E2-03, E4-02 — ACCEPT/ACCEPT WITH MODIFICATION.

## 2. Identity и session semantics

`RequestIdentity` включает:

- абсолютный `.sln`/`.slnx`/`.csproj`;
- Configuration, Platform, TargetFramework с сохранением `null` против explicit;
- graph scope и отсортированные canonical selected roots;
- metadata mode и другие режимы, меняющие semantic graph.

Inner TFM/effective properties принадлежат каждому project instance; outer
cross-targeting instance не выдаётся за обычный проект. Multi-target target
workload допускается только с exact instance/edge mapping.

Overlay requested/effective — session state, а не переносимый analyzer result.
Один base DTO может обслуживать on/off только если prepare/evidence полностью
разделены. false/omitted на same-key RAM session не отключают активный overlay;
reset/new key/restart создают новую сессию. Disk hydrate не включает overlay.

`CompatibilityFingerprint` сверяется с **текущим независимо полученным**
toolset/environment resolution, а не только с сохранённой копией самого себя.
Изменение или невозможность проверки означает incompatible/unsupported miss.
Секретные environment values не сохраняются и не логируются.

Trace: R-03, R-04, R-06, C-03 — REJECT (absent/explicit сохранено), C-05.

## 3. Две таблицы snapshot

### 3.1 Semantic hydration DTO

Versioned DTO содержит roots, inner project instances, directed references,
language/name/assembly/file/output paths, effective properties, полные
поддержанные parse/compilation options, compile/additional/analyzer-config
documents, reference properties и portable analyzer identity без shadow paths.

Один physical path может иметь несколько memberships. `path -> all memberships`
используется для disk sync; query/edit отдельно выбирает детерминированный
project/TFM context. Порядок hydrate не влияет на выбор.

### 3.2 Dependency/admission evidence

Отдельная versioned таблица содержит:

- positive dependencies;
- known-absent paths/conditions;
- bounded regions возможного появления wildcard/negative inputs;
- target-produced compiler inputs;
- evidence source и version для каждой категории;
- источник expected roots/instances/edges/inputs.

Import не обязан быть Roslyn Document, но отсутствие его evidence запрещает
reuse. Binlog, resolved Documents/imports, SDK name или evaluation-only не
считаются полным evidence автоматически. Unknown в любой обязательной категории
отключает disk-hit всего request.

### 3.3 Completeness

`CacheCompleteness=true` только если получены все expected roots, inner instances,
directed edges и compiler inputs. Законно пустой project разрешён явной policy.
Отсутствие blocking diagnostic не доказывает completeness, а обычный load может
быть usable при запрещённом capture.

Trace: R-02, C-01, C-04, E0-02, E1-03, E1-07, E2-01.

## 4. Bytes, decoding и generated inputs

Capture берёт base solution без shadow paths, session IDs, переносимых
`ProjectId` и temp paths. Source/generated text не сериализуется.

Для каждого Document фиксируются effective encoding, BOM handling, fallback,
null/unspecified representation и write-back policy. Непредставимый случай
отвергает profile. Oracle сравнивает character sequence, encoding policy,
semantics и bytes после round-trip edit; checksum только дополнительный сигнал.

Все реально значимые compile/config/provenance inputs, включая explicit `obj`,
content-hashed. Частая изменчивость не разрешает исключить input. No-op build,
build и edit+build+restart измеряются по категориям изменившихся bytes.

Trace: C-06, C-09 — ACCEPT WITH MODIFICATION.

## 5. Admission protocol

Порядок обязателен:

`dependency discovery -> establish coverage -> capture/hydrate -> validation
-> prepare/gate -> publication`.

Для каждой стадии реализация определяет owner, generation, consumed bytes,
cancellation result и ресурсы. Support profile до hydrate, publication и
загрузки DLL классифицирует request как `supported | unsupported | unknown`.
`unknown` означает ordinary load, не partial hit.

Hydrator должен определить, когда материализуются texts, metadata и analyzers,
какие lazy reads переживают publication и какой lease их защищает. Late input,
unknown coverage или неподтверждённая связь между consumed bytes и evidence
запрещают reusable capture.

После hydrate portable provenance реконструируется только разрешённым способом,
затем выполняются A-LOAD prepare/gate и atomic manager publication. Старый
session-bound provenance нельзя сделать валидным заменой session ID. Пока
U-ARB-04 не закрыт, disk base не становится overlay-ready.

Trace: C-01, C-02, C-05, E2-01; U-ARB-04/U-ARB-06 preserved.

## 6. Lifecycle и атомарность

Кандидат строится отдельно. State-transition table реализации обязана покрывать
same-key refresh, different key, cancellation, load failure и prepare failure:
old/candidate ownership, loaded key/path, dirty state, admission, publication и
disposal. Старый workspace другого key автоматически не публикуется как fallback.

Различаются:

1. atomic store pointer для immutable payload;
2. atomic manager publication под workspace lock;
3. filesystem/CLR state, для которого rollback не обещается.

Capture scheduling и durable cadence остаются gate U-ARB-01. Foreground и
background варианты обязаны применять один safety predicate; ни один вариант
не является normative до решения.

Trace: C-02, E1-02, E2-05 — UNRESOLVED, E3-06.

## 7. Store и reader ownership

Evaluation cache хранится в отдельном namespace под
`LocalApplicationData/RoslynMcpServer/eval-cache/<schema>`. Он не управляет
analyzer shadow generations.

- Writer создаёт unique immutable generation и atomically публикует manifest
  pointer после полной записи/schema/size/checksum validation.
- До public activation выбирается reader protocol: acquire/release, owner
  identity, pause, crash, PID reuse, heartbeat/эквивалент и cleanup race.
- Protection действует до последнего payload/lazy read, не только до возврата
  hydrate.
- Expiration сама по себе не разрешает deletion. При сомнении cleanup пропускает
  deletion/new write.
- Corrupt/oversize/unknown schema/no-space/permissions/competing writer дают
  bounded fallback; project files из cache не восстанавливаются.
- Cache payload недоверенный: DTO не исполняет команды и сам не разрешает
  загрузить DLL. Trusted storage/threat model и portable provenance требуются
  отдельно.

Store-correctness gate и product-activation gate независимы.

Trace: C-08, E2-06 — ACCEPT WITH MODIFICATION.

## 8. Scan, probes и events

Profile отдельно задаёт membership roots, walk-up inputs, explicit dependencies,
negative/potential regions и symlink/junction retarget policy. Solution directory
не считается автоматически membership root каждого проекта.

Budget включает visited entries, bytes, time, cancellation и I/O concurrency.
Превышение означает отказ cache probe/ordinary load, не truncated hit. Prune
ограничивает recursion, но не explicit probes/watch ancestor/`obj` paths.

Event roles:

- `known explicit`;
- `potential membership/negative`;
- `proven irrelevant`;
- `unknown coverage`.

Relevant unknown переводит generation в untrusted/graph-dirty; proven irrelevant
не вызывает DTB. Content-only допустим только для существующей поддержанной
Document role при доказанной независимости evaluation/targets от bytes; graph
role имеет приоритет; missing document не считается applied.

Trace: C-07, E3-03, E3-05, E3-07.

## 9. Public behavior и observability

Предлагаемые параметры остаются opt-in до activation gate:

- `useDiskCache=false`;
- `forceReload=false`, обходящий RAM и disk.

`reset_workspace` очищает RAM, но не disk. Ответ возвращает bounded:

- requested/effective cache policy;
- base graph source;
- effective validation mode;
- capture status и write status/reason;
- requested/effective overlay;
- semantic readiness;
- graph scope/coverage и fallback reason.

RAM hit не подразумевает disk generation. Переход false→true на RAM hit может
вернуть `not-attempted: unverified-ram`; скрытый force capture не требуется.
Reason set включает disabled, forced, absent, incompatible, unsupported,
inputs_changed, membership_changed, untrusted, corrupt, hydrate_failed,
prepare_failed и capture/write outcomes.

Trace: E2-03 — ACCEPT; E4-02 — ACCEPT WITH MODIFICATION.
