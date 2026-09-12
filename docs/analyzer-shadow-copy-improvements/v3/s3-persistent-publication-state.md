# S3 — сохранять fail-closed при каждой публикации

Статус: **не выполнено**. Зависимость: [S2](s2-write-base-freshness.md).
Результат шага: V3-R2 устранён; ошибка prepare не превращается в raw publication
после обычной мутации workspace.

## Основание

`SetFailClosedPublishedSolution` удаляет analyzer references лишь из одного
snapshot. Последующая публикация после text edit снова строится от raw
workspace, где исходные references остаются. Последний execution status не
является устойчивой политикой допуска: повторная проверка пригодности real
DLL не доказывает разрешение её исполнения после opt-in failure.

Targets: `SolutionManager` load/prepare, `CompletePrepare`, `CompleteFailedPrepare`,
`PublishInMemorySolution`, `SetPublishedSnapshot`, published accessors;
`AnalyzerExecutionGate` и внутренний write context.

## Работа

Явно хранить состояние допустимости semantic publication для load-сессии,
отдельно от последнего refresh/execution observation. Различать честную
no-overlay сессию, активный разрешённый mapping и запрет после неуспешного
opt-in. Причина запрета и stale mapping не заменяют само состояние.

Все пути публикации должны соблюдать это состояние: load/cache,
prepare/enable, edit, watcher flush, reconciliation и recovery после отмены.
При запрете разрешён безопасный snapshot с заранее определёнными исключёнными
references либо отсутствие semantic snapshot. Raw workspace может сохранять
original references для корректного persistence; это не разрешает отдавать
их semantic readers. S4 отдельно задаёт поведение без пригодного provenance.

Допуск и набор запрещённых references вычисляются на load/prepare boundary
и применяются к Solution в памяти. Edit/flush/reconciliation не повторяют
provenance discovery, filesystem probing, dependency inspection или hash
анализаторов ради решения о публикации. Lazy compilation разрешённого
snapshot остаётся отдельным действием.

Совместимый прежний mapping после файловой ошибки refresh можно сохранить
как явно stale. Restart-required не разрешает исполнять stale V1.
Cached false/omitted после отказавшего opt-in не снимают запрет; cached true
может восстановить работу только после успешной разрешённой подготовки.
Новая сессия сбрасывает состояние согласно её собственному флагу загрузки.

## Приёмка

- R2 из S1 зелёный: prepare failure → text edit не возвращает real reference,
  oracle не загружает real DLL и не получает marker.
- Та же гарантия проверена после watcher delivery+flush и после успешной
  reconciliation частичной записи, а также после cancellation load boundary.
- Проверено сохранение запрета на cached false/omitted и восстановление
  после разрешённого successful prepare без отключения identity gate.
- No-overlay load по-прежнему допускает raw references; успешный opt-in
  по-прежнему исполняет exact marker из shadow path.
- Ordinary publication не выполняет analyzer I/O. Проверка охватывает не
  только счётчик publisher, но и отсутствие вызовов inspector/probing в
  publication path, поскольку старый счётчик не измеряет все виды I/O.
- Все production semantic accessors возвращают только допустимый snapshot
  либо явное отсутствие/ошибку; fallback на raw отсутствует.

## Результат

Не выполнено.
