# Неразрешённые вопросы v2

Вопросы перенесены из авторитетного [arbitration/unresolved.md](../arbitration/unresolved.md).
U-ARB-01/02/03 — политика выбрана; matcher E5-S2 [принят](epoch-5-s2-acceptance.md)
в v1.3.10; приёмка E5-S4 [принята](epoch-5-s4-acceptance.md) в v1.3.12.
U-ARB-05 [принят](u-arb-05-acceptance.md) как session-sticky в v1.3.13.
U-ARB-04 evidence [принят](u-arb-04-acceptance.md); atomic load/prepare
boundary [реализована и принята](u-arb-04-implementation-acceptance.md) в
v1.3.14. Inaccessible в U-ARB-01 остаётся открытым.

## U-ARB-01 — Происхождение ссылок при отсутствующем пути

Статус: **реализован и принят — capture provenance на load + confirmed-only
matcher** (v1.3.10); fixture-приёмка E5-S4 в v1.3.12.
Evidence: [epoch-5-s1-results.md](epoch-5-s1-results.md),
[epoch-5-s2-results.md](epoch-5-s2-results.md),
[epoch-5-s4-results.md](epoch-5-s4-results.md).
Inaccessible **не** выбран.

Связанные findings: E5-01, E5-02, E5-04, E5-05.

E5-S1: в публичном Roslyn 5.9.0 связи `AnalyzerReference → Project` нет;
в design-time MSBuild она есть (`%(Analyzer.MSBuildSourceProjectFile)` +
effective Configuration / inner TFM). BuildHost схлопывает item в
`/analyzer:<path>` и теряет metadata.

**Выбранный режим.** На `load_workspace` / явной смене графа захватить
design-time provenance (source `.csproj` + effective globals / выбранный
inner TFM). Rewrite только confirmed item → загруженный inner project.
Недоказанное missing — skip, не unique-name. Filename остаётся кандидатом,
не доказательством. Считать snapshot не на semantic query и не на edit `.cs`.

Политика реализована и принята E5-S2–S4. [Эскиз F-09](epoch-5-f09-capture-design.md)
выбрал production-кандидатом binlog той же design-time загрузки
`MSBuildWorkspace`: replay событий вместо второго target/evaluation pass.
Эскиз **принят** ([epoch-5-f09-acceptance.md](epoch-5-f09-acceptance.md)).
P0-spike подтвердил `Analyzer.MSBuildSourceProjectFile`, effective
Configuration/inner TFM и exact join к загруженному `ProjectId`
([результаты](epoch-5-f09-p0-spike-results.md)); F09-01 закрыт. Production
snapshot [принят](epoch-5-f09-production-capture-acceptance.md) в v1.3.9.
Rollout E5-S2 реализован в v1.3.10: rewrite только для complete confirmed
binding текущей load-сессии; unconfirmed missing и same-name external ссылки
сохраняются без filename fallback. Production snapshot закрыл F09-PROD-1/2.
`ProjectInstance` и второй `dotnet msbuild` оценены, но не выбраны production
fallback.

**Запас, если capture не взлетит** (не выбирать заранее, не забывать):

| ID | Режим | Следствие |
| --- | --- | --- |
| Alt-2 | Оставить unique-name как **явную** opt-in эвристику | Дешево; исходный missing-path repro жив; provenance нет |
| Alt-3 | Сузить: rewrite только при точном path = loaded output | Честно; missing-path половину исходного repro сдаём |

Переход на Alt-2/Alt-3 — отдельное решение владельца после провала capture,
с записью здесь **до** смены тестов/matcher. Capture принят; unique-name
fallback снят в v1.3.10. Alt-2/Alt-3 не внедрялись.

Inaccessible не наследует missing-file и не наследует этот выбор. E5-S4
различает `access_failure` как reason/path state и не назначает rewrite
или skip. Точки v2:
[E5-S1–S4](epoch-5-reference-provenance.md), E1-S2 и LC-S1.

## U-ARB-02 — Поддерживаемый режим обновления генератора

Статус: **выбран — restart-required** (эпоха 3, v1.3.7).

Связанные findings: E1-02, E1-03, E3-01, E3-05, E6-01.

Evidence эпохи 1 и повтор эпохи 3: V1→V2 cached и reset+load не исполняют V2
(CLR identity живёт дольше workspace); process restart исполняет точный V2;
A→B same identity без restart исполняет чужую сборку, поэтому отказ.

Контракт: неподдержанный in-process refresh отклоняется с действием
«restart the MCP server process». Restart — окончательный режим, не временный
workaround. Точки v2:
[E3-S1/S5](epoch-3-loader-contract-and-dependencies.md), E1-S2, LC-S1–S3.

## U-ARB-03 — Production discovery зависимостей и scope binding

Статус: **выбран — main-only + явный отказ** (эпоха 3, v1.3.7).

Связанные findings: E2-01, E3-02, E3-03, E3-04.

Production источник полного private runtime-набора не установлен. ALC не выбран.
Конфликтующие helpers не имеют requester/generation-scoped resolution.

Контракт: исполняются только main-only генераторы; AssemblyRef вне точного
`AnalyzerHostContractCatalog` отклоняется; first-match simple-name probing снят;
private DLL из real output не используются как fallback. Точки v2:
[E2-S2](epoch-2-immutable-shadow-copies.md), [E3-S2–S5](epoch-3-loader-contract-and-dependencies.md).

## U-ARB-04 — Возможная загрузка analyzer из raw workspace

Статус: **закрыт — evidence и решение приняты; atomic load/prepare boundary
[реализована и принята](u-arb-04-implementation-acceptance.md) в v1.3.14**.
Результаты: [u-arb-04-load-boundary-evidence.md](u-arb-04-load-boundary-evidence.md).
Независимая приёмка evidence: [u-arb-04-acceptance.md](u-arb-04-acceptance.md).
Решение: [u-arb-04-decision.md](u-arb-04-decision.md).
Leftover U-ARB-04-1 [принят](u-arb-04-acceptance.md). Независимая приёмка
кода: [u-arb-04-implementation-acceptance.md](u-arb-04-implementation-acceptance.md).
Low U-ARB-04-IMPL-1 закрыт в v1.3.15: implicit `FindDocument` auto-load удалён.

Связанные findings: E1-07, E4-03.

Write boundary предотвращает сохранение известных overlay references, но ни одна
сторона ранее не показала конкретную операцию, которая загружает существующий
analyzer напрямую из `workspace.CurrentSolution` до overlay или вне него.
Эпоха 4 (принята): inverse + forced rebuild на full-success write paths
(missing и existing-correct-path) меняет hash real output — lock-утечки не видно.
Это измерение persistence, не выбор load boundary.

Новый evidence-проход установил:

- Physical load и prepare без semantic query не загружают Generator; forced
  rebuild real output успешен.
- Published `GetCurrentSolution()` без активного overlay содержит raw real
  analyzer reference. Обычный `GetCompilationAsync` загружает точный real output
  и воспроизводит lock (`MSB3021`); последующий enable отклоняется identity gate.
- Overlay semantic загружает точный shadow path. Все три write paths на
  existing-correct fixture оставляют real path незагруженным, а forced rebuild
  успешен и меняет hash.
- Production inventory не нашёл явного raw-workspace semantic reader. При
  выключенном overlay production semantic paths используют published snapshot
  с raw references; это воспроизведённая opt-in boundary, но не обход активного
  overlay. Остаётся не измерена фактическая concurrent-dispatch вставка между
  physical load и отдельным prepare одного enable=true вызова.

После evidence выбранная boundary реализована: `LoadWorkspace` вызывает единый
manager workflow под одним `_workspaceLock`, а production semantic readers ждут
published-snapshot accessor. Новый opt-in raw snapshot между physical load и
prepare не публикуется; prepare failure/cancellation новой сессии fail-closed.
Норматив: [u-arb-04-atomic-load-prepare.md](u-arb-04-atomic-load-prepare.md),
приёмка: [u-arb-04-implementation-acceptance.md](u-arb-04-implementation-acceptance.md).
Цель не сужалась до missing paths.
Точки v2:
[E1-S3/S4](epoch-1-lifecycle-verification.md), [E4-S3/S4](epoch-4-workspace-write-boundary.md).

## U-ARB-05 — Семантика флага при повторных вызовах

Статус: **выбран, реализован и принят — session-sticky** (v1.3.13).
Решение: [u-arb-05-decision.md](u-arb-05-decision.md).
Независимая приёмка: [u-arb-05-acceptance.md](u-arb-05-acceptance.md).

Связанные findings: E1-04, E6-03.

Публичный параметр — optional non-nullable bool: omission неотличим от явного
`false` на границе метода. На same-key cache hit действует session-sticky
поведение: `true` включает или обновляет overlay текущей load-сессии;
последующие `false`/omitted сохраняют активные mapping/generation и не запускают
prepare. Reset, graph reopen или другая загрузка создают новую сессию и очищают
состояние overlay.

Выбранная политика сохраняет shipped-поведение v1.3.5+ и совместима со старыми
клиентами, которые не передают добавленный optional параметр. Desired-state
отклонён: при non-nullable default он превратил бы omission в неявный disable.
Tri-state/новый disable-параметр не вводится. Ответ cached load теперь явно
сообщает о сохранении активного overlay. Точки v2: README, E1-S2, E2-S1,
LC-S1–S3, E6-S3 и решение выше.
