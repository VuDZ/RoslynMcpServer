# Неразрешённые вопросы v2

Вопросы перенесены из авторитетного [arbitration/unresolved.md](../arbitration/unresolved.md).
U-ARB-01/02/03 — политика выбрана; rollout matcher (эпоха 5) всё ещё ждёт
дизайн захвата. U-ARB-04/05 и inaccessible в U-ARB-01 остаются открытыми.
Ни наличие v2, ни запасные варианты ниже не разрешают менять matcher без
принятого capture design.

## U-ARB-01 — Происхождение ссылок при отсутствующем пути

Статус: **выбран — capture provenance на load** (2026-09-12, владелец требования).
Evidence: [epoch-5-s1-results.md](epoch-5-s1-results.md). Matcher **не** изменён.
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

Это выбор политики, не готовый rollout. [Эскиз F-09](epoch-5-f09-capture-design.md)
выбрал production-кандидатом binlog той же design-time загрузки
`MSBuildWorkspace`: replay событий вместо второго target/evaluation pass.
Эскиз **принят** ([epoch-5-f09-acceptance.md](epoch-5-f09-acceptance.md)).
Канал в коде не открыт, пока P0-spike не подтвердит
`Analyzer.MSBuildSourceProjectFile`, effective globals/inner TFM и exact join к
загруженному `ProjectId` на redirected fixture (F09-01). Rollout E5-S2 до
зелёного P0 запрещён.
`ProjectInstance` и второй `dotnet msbuild` оценены, но не выбраны production
fallback.

**Запас, если capture не взлетит** (не выбирать заранее, не забывать):

| ID | Режим | Следствие |
| --- | --- | --- |
| Alt-2 | Оставить unique-name как **явную** opt-in эвристику | Дешево; исходный missing-path repro жив; provenance нет |
| Alt-3 | Сузить: rewrite только при точном path = loaded output | Честно; missing-path половину исходного repro сдаём |

Переход на Alt-2/Alt-3 — отдельное решение владельца после провала capture,
с записью здесь и в E5-S4 **до** смены тестов/matcher. Пока держать
выпущенный unique-name matcher.

Inaccessible не наследует missing-file и не наследует этот выбор. Точки v2:
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

Связанные findings: E1-07, E4-03.

Write boundary предотвращает сохранение известных overlay references, но ни одна
сторона не показала конкретную операцию, которая загружает существующий analyzer
напрямую из `workspace.CurrentSolution` до overlay или вне него.
Эпоха 4 (принята): inverse + forced rebuild на full-success write paths
(missing и existing-correct-path) меняет hash real output — lock-утечки не видно.
Это измерение persistence, не выбор load boundary.

Недостающие evidence:

- Existing-correct-path tests с семантикой, всеми write paths и фактическим
  loaded assembly path.
- Forced rebuild после каждой операции, доказывающий либо опровергающий lock output.
- Временная последовательность загрузки до включения overlay и raw-workspace readers.

После evidence: если утечка воспроизведена, специфицировать отдельную semantic/load
boundary. Если не воспроизведена на поддержанной матрице, документировать измеренную
область без утверждения, что write workflow вызвал anti-lock гарантию. Эта v2
не выбирает новую load boundary и не сужает цель до missing paths. Точки v2:
[E1-S3/S4](epoch-1-lifecycle-verification.md), [E4-S3/S4](epoch-4-workspace-write-boundary.md).

## U-ARB-05 — Семантика флага при повторных вызовах

Связанные findings: E1-04, E6-03.

Публичный параметр — optional non-nullable bool: omission неотличим от явного
`false` на границе метода. На same-key cache hit действует session-sticky
поведение; reset или другая загрузка его очищает. Opt-in означает отсутствие
автоматического включения, но не определяет desired state каждого вызова.

Недостающие требования/evidence:

- Полагаются ли существующие клиенты на сохранение overlay при false/omitted.
- Включение относится к каждому load request или к workspace session.
- Политика совместимости при изменении sticky поведения.

Требуется выбрать и описать desired-state (`false`/omitted отключают) либо
session-sticky (отключение через reset). Tri-state или новый параметр — отдельное
публичное API-изменение с отдельным обоснованием. До решения сохранить и точно
документировать текущее поведение; старый mapping не отбрасывается неявно.
Точки v2: README, E1-S2, E2-S1, LC-S1–S3, E6-S3.
