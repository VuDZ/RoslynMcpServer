# Ответы: Epoch 3

Исходные замечания: [review/epoch-3-live-consistency.md](../review/epoch-3-live-consistency.md). O/A: [основания](sources-and-scope.md).

## E3-01

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Нужна спроектированная схема вызовов и publication; одного запрета recursive lock мало. Однако выбора только между deadlock и unlock во время load нет. A-LOAD S2 и A-WRITE E4-S1 прямо предусматривают internal under-lock этапы без повторного захвата. Единый refresh workflow под уже удерживаемым semaphore — возможная схема, хотя её производительность и реализация ещё не доказаны. O4 требует автоматического обновления после sync; замена его одним hint меняет требование.

**Предлагаемые изменения.** Epoch 3 «Изменения», Epoch 1 lifecycle, contract §7: диаграмма acquisition/under-lock work/publish/release и владения snapshot; все prepare/gate гарантии A-LOAD сохраняются. V21 плюс существующий deterministic load/prepare seam: concurrent semantic/edit/reset, stale candidate после refresh, cancel. Не утверждать, что сам semantic computation целиком выполняется под lock: accessor выдаёт удерживаемую immutable базу, запись позже перепроверяет её.

**Последствия.** Internal manager API, latency/очередь, publication generation и freshness checks A-WRITE. MVCC/merge не нужны автоматически. Если бюджет неприемлем, policy меняется через A-03, а не ослаблением исходной O4 в коде.

## E3-02

**Вывод: NEEDS CLARIFICATION.**

**Основание.** Полный hash/scan на каждый query может дорого стоить; это существенный риск. Но O4 требует свежести при живом sync, включая overflow, а O8 не разрешает скрытое stale. V2 усиливает механизм до strict на каждом вызове, первоначальный план использовал инкрементальные события и полный recovery. Ни допустимый overhead, ни требование обнаруживать потерянное без сигнала событие на каждом вызове не зафиксированы первичным заданием. Дороговизна не измерена, а «только load probes» не закрывает O4.

**Что требуется уточнить.** A-03: точное обещание свежести, допустимые условия стабильного дерева, реакция на lost events и latency budget. Не принимать blanket watcher trust как эквивалент strict. `trusted` может обозначать состояние evidence, а не нулевую цену следующей проверки.

**Затрагиваемые разделы и последствия.** Epoch 3 «Состояния/Приёмка», contract §6/§7/§9, Epoch 4B и V19–V23. Возможны разные freshness policies с явной metadata, но это изменение пользовательской гарантии. Горячий путь, CPU/I/O и конкуренция требуют отдельного измерения до выбора.

## E3-03

**Вывод: ACCEPT.**

**Основание.** O3 ограничивает сканирование значимым конусом, O4 требует видеть новые входы, но не требует DTB на каждый editor temp или unrelated `.md`. Epoch 3 формулирует unknown path слишком широко. Один фильтр расширений тоже недостаточен: AdditionalFiles/imports могут иметь произвольное расширение. §5 уже разделяет recursion/probes/events, но не задаёт конкретную классификацию.

**Предлагаемые изменения.** Epoch 3 «Изменения» и contract §5: таблица known explicit dependency, potential membership/negative path, proven irrelevant path, unknown coverage. Новые пути проверять по областям возможного членства, не только существующему списку. Релевантный unknown → untrusted/graph-dirty; доказанно нерелевантный не запускает load. V19/V22 дополнить временными файлами, `nuget.config` и custom-extension AdditionalFile.

**Последствия.** Shared classification model с разными политиками watcher/scan, причины refresh и счётчики предотвращённых/выполненных reload. Не скрывать неизвестные входы blacklist-ом ради скорости. Риск baseline AddDocument — отдельный N5.

## E3-04

**Вывод: NEEDS CLARIFICATION.**

**Основание.** Различать обнаруженное изменение и невозможность проверки полезно. Но рекомендация всегда отдавать last trusted + unknown выбирает доступность вместо строгой свежести. Это не следует из O4/O8 автоматически. Кроме того, A-ADMISSION запрещает raw fallback после opt-in failure: ранее доступный snapshot не всегда разрешён для исполнения сейчас. `refresh-failed` в плане уже допускает error/hint, поэтому без контракта их конкретного поведения спор неразрешим.

**Что требуется уточнить.** A-03: допускается ли read-only результат с unknown freshness; какие операции обязаны отказать; можно ли использовать старое решение после graph change/другого key; как unknown сочетается с Banned/Unavailable. Запись со stale/unknown базой не получает исключения из A-WRITE.

**Затрагиваемые разделы и последствия.** Epoch 3 таблица состояний, contract §9, V09/V21 и public error/metadata contract. Риски — вводящий в заблуждение поиск, retry storm и потеря доступности. Ослабление fail-closed overlay не предлагается ни при каком выборе freshness policy.

## E3-05

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Конкретная классификация нужна: наличие `config` в имени или расширении ничего не доказывает. Но предлагаемый review allowlist «документ и не evaluation input» тоже является частью доказательства профиля; он не заменяет его. Нужно исключить влияние не только evaluation, но и DTB targets, способных читать source/additional contents. V2 contract §1 специально отличает эти этапы.

**Предлагаемые изменения.** Epoch 3 content-only пункт и contract §4/§5: content-dirty допустим только для существующих поддержанных document categories, если профиль доказал независимость DTB результата от bytes; graph inputs имеют приоритет при совмещённых ролях. Отсутствие document при sync → отказ/graph-dirty, не applied. V20/V22: props, generated config, AdditionalFile и linked memberships.

**Последствия.** DTO ролей входа, классификатор, обновление всех memberships и повторная генерация там, где необходимо. Расширение content-only на новый профиль требует tests; просто убрать `.cs` из hashes нельзя. Отдельный N7 охватывает свежесть toolset/environment, не являющихся document contents.

## E3-06

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Schedule записи действительно не определён. Но «обновляется только после подтверждения» не означает «после каждой правки». Согласованность cache означает также корректный отказ от старого поколения; RAM и persisted cache не обязаны быть побайтно одинаковы после каждого edit. При этом O4 явно требует обновлять живой индекс, а O5 влияет на пользу следующего restart — забыть эти цели из-за допустимого miss нельзя.

**Предлагаемые изменения.** Epoch 3 «Изменения», contract §7/§8 и handoff: отдельно определить актуальность RAM/живого индекса, reuse validity старого cache и cadence публикации нового. Разрешённое отставание/момент durable capture выбрать совместно с E2-05. Не выводить обязательный full hash/write на каждую правку и не предписывать автоматически «только graph-load».

**Последствия.** Generation/dirty state, debounce или иная политика лишь после выбора, crash/restart tests и hit-rate. Несовпадающее старое поколение обязано дать miss; новое не публикуется из untrusted данных. Отложенная запись не разрешает stale semantic result в живой сессии.

## E3-07

**Вывод: ACCEPT.**

**Основание.** Текущий [watcher](../../../../Services/SolutionManager.cs:1713) и [фильтр](../../../../Services/WorkspaceDiskPathFilter.cs) не покрывают ancestor/explicit `obj` inputs. V2 уже требует это изменить; её сильная гарантия не может быть подтверждена reuse нынешней конфигурации. Strict probes способны обнаружить изменение без события, но это не доказательство watch coverage и не разрешение затем ослабить probes.

**Предлагаемые изменения.** Epoch 3 «Изменения»/handoff и contract §5/§7: явный watch/probe coverage map — project regions, нужный walk-up, explicit paths вне roots и места появления отсутствовавших файлов. Не требовать отдельный watcher на каждый файл; стратегия группировки и лимитов должна быть определена. Неустановленный/потерянный watcher → untrusted с предусмотренной проверкой.

**Последствия.** Watch resource limits, I/O, startup/overflow/cancellation lifecycle и V19/V22. Prune не подавляет значимый explicit event. Новый охват может ухудшить производительность, поэтому нужны бюджеты и статистика; запрет полного ненужного обхода O3 остаётся.

## E3-08

**Вывод: PARTIALLY ACCEPT.**

**Основание.** [IsSelfWriteSuppressed](../../../../Services/SolutionManager.cs:1844) подавляет путь по времени и может скрыть внешнюю запись — реальная угроза O4. Однако FileSystemWatcher не сообщает hash bytes в момент события: последующее чтение показывает текущее состояние, а не доказанный источник echo. Удаление suppress также не означает неизбежный DTB для каждого Changed: content-only допускается для подходящих входов.

**Предлагаемые изменения.** Epoch 1 notification и Epoch 3 suppress/dedup: после фактической собственной записи фиксировать подтверждённый текст/hash и revision; не отбрасывать весь путь по времени. Если сверка наблюдаемых bytes не совпала либо нестабильна — сохранить pending/untrusted. Описать dedup так, чтобы более позднее событие не очищалось старым flush. V20/V21: внешняя запись в suppress window, ABA, cancel и partial persistence.

**Последствия.** Own-write result, per-path versioned pending state, точный учёт частичных saved paths и повторный I/O при сомнении. Hash помогает сверке состояния, но не создаёт filesystem transaction и не обеспечивает полную атрибуцию writer. Более широкий вопрос стабильности остаётся N6/A-03.
