# S3 — сохранять fail-closed при каждой публикации

Статус: **выполнено; независимая приёмка
[принята](s3-acceptance.md)** (v1.3.17, V3-R2 закрыт).
Зависимость: [S2](s2-write-base-freshness.md).
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

Выполнено 2026-09-12. V3-R2 закрыт: запрет raw publication переживает
edit / flush / reconciliation / cancel и cached false/omitted.

### Источники и среда

- **База:** `7fb86b3` (`fix: reject stale workspace write bases before persist`, v1.3.16)
- **Commit шага:** этот коммит; `SemanticPublicationState`, `SolutionManager`
  publication paths, write-context admission, inspector/probe counters,
  host Inspect fields, тесты S3, csproj `1.3.17`, README, этот файл и
  [s3-acceptance.md](s3-acceptance.md)
- **Версия csproj:** `1.3.17`
- **OS / host:** Windows, x64 process
- **SDK:** `10.0.204` (`run_dotnet_build` / `run_specific_test`)
- **MCP binary:** `RoslynMcpServer` (workspace tools; production publish/reload
  этого шага не делались)

### Реализация

- Отдельное состояние сессии: `NoOverlay` / `AllowedMapping` / `Banned`.
  Last execution observation больше не решает допуск ordinary publication.
- Набор исключённых references вычисляется на load/prepare boundary
  (`CaptureFromInSolutionReferences`) и применяется без inspector/probing.
- File-prepare failure с прежним mapping остаётся `AllowedMapping` (stale);
  restart-required банит и не исполняет stale V1.
- Cached false/omitted не меняют состояние; cached true восстанавливает
  только после успешного разрешённого prepare (identity gate на месте).
- Write freshness сравнивает и admission; смена допуска без raw mutation —
  `stale-publication`.

### Команды

1. `load_workspace` → `RoslynMcpServer.sln`
2. `run_dotnet_build` → `RoslynMcpServer.sln`
3. `run_specific_test` class=`V3RegressionBaselineTests`, `noBuild=true`
4. `run_specific_test` class=`V3PersistentPublicationStateTests`, `noBuild=true`, `timeoutSeconds=600`
5. `run_specific_test` class=`WorkspaceWriteBoundaryTests` / `SemanticPublicationStateTests`
6. Точечный прогон U-ARB-04 fail-closed/cancel и Epoch1 stale-mapping / cached-false overlay

### Фактические результаты

| Проверка | Результат |
| --- | --- |
| R2 `V3_R2_prepare_failure_stays_fail_closed_after_text_edit` | **passed** — после edit нет real published/loaded/process, marker отсутствует |
| Watcher flush / reconciliation / cancel+edit | **passed** — `PublicationAdmission=Banned`, нет publication I/O |
| Cached false/omitted | **passed** — запрет не снят; edit остаётся fail-closed |
| Cached true + successful prepare | **passed** — `AllowedMapping`, exact `V1` из shadow, `ReferenceRewritten` |
| No-overlay + successful opt-in | **passed** — raw refs / shadow marker как прежде |
| Ordinary publication I/O | inspector / probe / assembly-evaluation / OverlayPrepareCount не растут |
| R1 | **passed** — S2 не регрессировал |
| R3 | остаётся красным — это S4, не регресс S3 |
| Write boundary + apply exclusions | **16/16** и **2/2** |

### Самопроверка

- Production published accessor (`GetPublishedSolutionAsync` / `publishedDocument` / `oracle` без `oracleSource=workspace`).
- Fallback на raw отсутствует; accessors возвращают допустимый snapshot или `no-solution`.
- Публичная MCP-схема не менялась; catalog 63 / 44,503.

### Ограничения

- Production/MCP publish+reload не выполнялись; номер выпуска в исходниках `1.3.17`.
- R3 специально не закрывался.
- Inaccessible U-ARB-01 по-прежнему вне v3 S3.
