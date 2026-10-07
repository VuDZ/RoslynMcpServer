# Proposed Changes

Дата: **2026-10-07**. Все P-* имеют Human state: **PROPOSED**.
Это вход для отдельной стадии изменения specification, не применённые правки,
не acceptance эпох и не разрешение activation. У каждого P указаны минимальный
outcome и проверяемое последствие; классы/сервисы реализации не назначаются.

## P-001 — Ранний capability gate

Reason: [ARB-002](findings/ARB-002.md), S-001.

Affected areas: E0 spec «Поддерживаемый срез/Приёмка», task-00/02/03/04/05,
карта dependencies E0.

Required change: закрепить порядок U-001 → bounded ordinary capability check S-001 →
принятый executable positive scope. До positive capability verdict не начинать
dependent codec/hydrate реализацию как доказанно пригодную для real control.
Schema sketch и inspection могут предшествовать проверке; full новый capture adapter
не должен становиться prerequisite собственного schema design. Negative outcome
фиксирует blocked reason и возврат scope на решение; не удаляет references и не
объявляет negative-only E0 выполненным. Исторические getter limitations используются
непосредственно, без их повторного исследования.

Dependencies: U-001 и S-001. Implementation freedom: минимальный способ inspection/
proof-of-capability. P фиксирует открытый эксперимент, не предрешает его результат.

## P-002 — Identity и матрица параметров

Reason: [ARB-003](findings/ARB-003.md), review-ds/F-001 + review-grok/F-005.

Affected areas: README «Поведение первого MVP», E1 spec/task-06/task-09, E0 schema,
benchmark compare-key description.

Required change: различать raw requested с absence, независимо разрешённые до open
merged globals и evaluated project values. Disk admission проверяет normalized path
и текущие loader globals; отсутствие — не wildcard. Сохранённые evaluated defaults
не используются как oracle текущего request. RAM matcher остаётся прежним.

Closed matrix должна явно определить:

- workspacePath и Configuration/Platform/TFM — canonical base context; globals
  получены по ordinary precedence, включая current Loaded*/FileSettings.
- shadowCopyInSolutionAnalyzers — обязательный current mode admission/fresh prepare;
  separate storage key допустим, sharing clean base тоже допустим при доказанной parity.
- buildArgs — suffix последующего build, не base graph identity; current session
  behavior сохраняется независимо от disk lookup.
- briefOutput/logProjectOutputDiagnostics — presentation/diagnostics, не base identity.
- useDiskCache/forceReload — policy/bypass, не новый semantic graph; forced чтение
  запрещено по existing contract, enabled ordinary capture после него допустим.

Проверки: omitted versus explicit Release, разные FileSettings при одинаковом raw
request, inherited Loaded* при смене workspace, Any CPU normalization, mode matrix.
Dependencies: P-003/P-005 для admission. Implementation freedom: key encoding,
locator layout, допускающие только лишние misses более строгие partitions.

## P-003 — Overlay support и честный fallback

Reason: [ARB-004](findings/ARB-004.md).

Affected areas: E1 spec «Интеграция», task-05/09/10, benchmark E1 scenarios.

Required change: описать supported mode matrix. Без подтверждённого fresh binding
overlay request идёт на whole-request ordinary load до publication disk candidate.
Этот разрешённый task-05 путь достаточен для минимального E1; обязательный overlay
disk hit не вводится, постоянный запрет будущего adapter также не вводится.
Не фабриковать provenance, не удалять references, не превращать пригодный ordinary
request в Unavailable только из-за cache attempt. Настоящие ordinary Banned/Unavailable
и restart-required сохраняются. New-PID overlay control и отдельный benchmark outcome
должны показывать actual fallback; overlay-off speedup не переносится на overlay-on.

Dependencies: P-002. Implementation freedom: classifier либо доказанный fresh adapter
в пределах existing task-05; расширенный adapter scope требует собственного review.

## P-004 — Связь graph с поколением inputs

Reason: [ARB-005](findings/ARB-005.md).

Affected areas: E1 spec «Store», task-00/06/09; E2/task-04 consistency.

Required change: определить evidence принадлежности evaluated options/memberships/
references тому же generation, который описывает manifest. Post-load stability
не считается таким evidence. При отсутствии binding reusable capture запрещён,
но успешный ordinary load сохраняется. Newly discovered dependency/region требует
предусмотренного bounded повторного подтверждения или отказа capture. Не выдавать
pre/post equality за доказательство отсутствия ABA. Добавить deterministic mutation
props после consumption до первого post-load probe; такой envelope не допускается.

Dependencies: P-011 для dependency source, без смешения paths и consumed bytes.
Implementation freedom: механизм binding ограниченного профиля; filesystem snapshot
и universal closure не предписаны.

## P-005 — Evaluation environment policy

Reason: [ARB-006](findings/ARB-006.md).

Affected areas: README inputs/identity, E1 spec/task-00/06/09.

Required change: profile описывает значимые environment dependencies и независимую
проверку current context до reuse; capture и lookup применяют одну policy.
Неизвестная значимая dependency даёт unsupported/fallback. Required controls:
absent→present и changed-value restart с прежними project bytes и различающимся
semantic result. Raw secrets не сохраняются в envelope/report.

Dependencies: P-002. Implementation freedom: совместимый conservative fingerprint
или доказанный relevant-variable subset. Controlled environment, меняющий ordinary
semantics, не разрешён автоматически; blanket allowlist без доказанного scope не подходит.

## P-006 — Capture eligibility и portable health

Reason: [ARB-007](findings/ARB-007.md).

Affected areas: E1 spec/task-00/06/07/09, envelope contract, load outcome.

Required change: определить successful capture как пригодный ordinary load без
blocking load failure; непригодный load не записывает usable envelope. Для допустимого
snapshot сохранить portable graph-health/coverage facts и происхождение необходимых
diagnostics. Пустые diagnostics hydrate не повышают Unknown до Complete и не стирают
прежний load verdict. Fresh watcher/execution/session состояние пересчитывается;
полный перенос LastDiagnostics или old Banned/Unavailable не требуется.
Проверки: blocking load no-capture; ordinary/hydrated health, loadGraphComplete и
coverage parity; новая watcher failure остаётся видимой.

Dependencies: P-003 и P-010 для отдельных admission/map свойств.
Implementation freedom: bounded summary/representation, без ложной whole-graph completeness.

## P-007 — Policy по load entry

Reason: [ARB-008](findings/ARB-008.md).

Affected areas: README API scope, E1 spec/task-06/09/10, будущая help/reporting surface.

Required change: явно записать ExplicitLoad и ConfigFile policy. При отсутствии
отдельного opt-in config-driven lazy load остаётся disk-disabled: нет lookup/capture/
write, outcome disabled/not-attempted; FileSettings globals продолжают работать.
Явный useDiskCache=true включает только предусмотренный scope. Benchmark explicit
tool не является evidence ускорения lazy entry. Добавить negative lazy-control без
opt-in и explicit enabled control. Новый config key/default не вводить подразумеваемо.

Dependencies: P-002. Implementation freedom: reporting details. Будущий config opt-in
может быть отдельным scope change с precedence; это не prerequisite E1.

## P-008 — Executable artifact E0→E1

Reason: [ARB-009](findings/ARB-009.md).

Affected areas: execution, E0/task-02/04/05, E1/task-03.

Required change: определить ownership и путь reuse DTO/codec/hydrate. E1 использует
тот же executable codec либо явно фиксирует перенос/замену; bounds/malformed/round-trip
проверки выполняются против фактически поставляемого artifact. Prototype evidence не
заменяет эти checks. Production не зависит от Tests assembly. E0 может иметь inert
production-compatible components без изменения runtime entry.

Dependencies: выбранный E0 contract. Implementation freedom: location/assembly,
перенос или раннее reusable размещение; обязательная двойная реализация не назначается.

## P-009 — Единственная apply boundary

Reason: [ARB-010](findings/ARB-010.md).

Affected areas: E0/task-05, E1/task-04/05/08, execution checks.

Required change: production Workspace.TryApplyChanges имеет ровно один reference site
в existing SolutionManager.TryApplyWorkspaceChanges. Новый host не добавляет второй
site; candidate construction через public APIs допустим, если не обходит write
preflight/publication invariants. Isolated experiment вызовы не переносятся вслепую.
После production integration выполняется весь SourceStructure; exact limits сохраняются.

Dependencies: P-008 при переносе prototype. Implementation freedom: construction
без apply либо existing manager wrapper. Не предписывается wrapper для любого создания DTO/host.

## P-010 — Role и owner round-trip

Reason: [ARB-011](findings/ARB-011.md).

Affected areas: E0 schema/capture/hydrate, E1/task-00/07/09, E2/task-00.

Required change: определить источник и восстановление portable occurrence roles,
all-memberships/owners и подтверждённых producer bindings в единственной input map
с новыми IDs. Unknown не становится UserInput; имя/path не заменяет provenance;
evaluation role сохраняет приоритет. Перенос GeneratedOnDisk не разрешает перенос
generator text или execution provenance. Проверка ordinary↔hydrate: user/generated/
unknown/additional/config/output, shared и multi-role paths, owner/producer mapping.

Dependencies: P-006 для отдельного health dimension. Implementation freedom: public
construction flags или применение доказанных role facts в единственной map.

## P-011 — Явный evidence source и его проверка

Reason: [ARB-012](findings/ARB-012.md), S-002.

Affected areas: E1 spec/task-00 и его входные evidence requirements.

Required change: записать S-002 как открытый выбор source с deadline до принятия
manifest adapter. Для каждой required категории назвать source events/properties,
instance binding, completeness boundary и incomplete→unsupported. Current map/
analyzer snapshot не считать готовой import closure; ProjectImports=None также
не считать доказательством отсутствия raw paths. Никаких обязательных второго log,
Embed или source text collection до результата эксперимента. При недостаточном
evidence no reusable capture; no positive request означает неготовность E1.

Dependencies: S-002; P-004 остаётся отдельным generation requirement.
Implementation freedom: verified существующий канал либо обоснованное расширение.
Это P фиксирует открытый experiment/ограничение, не утверждает ещё не выбранный канал.

## P-012 — Config discovery и known absence

Reason: [ARB-013](findings/ARB-013.md).

Affected areas: README inputs, E1/task-00/01/09, E2 spec/task-05.

Required change: явные probes для новых/удалённых применимых .editorconfig/.globalconfig,
explicit config paths и соответствующих regions. Profile задаёт actual discovery
boundary для project/linked sources, учитывая различную applicability config видов;
repo root не принимается за универсальную границу. Known absence проверяется уже
до первого E1 hit. Unknown discovery→fallback. Create/delete ancestor controls
сравниваются со fresh ordinary load; изменение known config тестируется отдельно.

Dependencies: P-011 там, где inclusion задаётся evaluated inputs.
Implementation freedom: bounded point/region probes, без полного recursive ancestor scan.

## P-013 — Content-only eligibility

Reason: [ARB-014](findings/ARB-014.md).

Affected areas: E2 spec/task-00/01/02/05, описание support profile.

Required change: content-only разрешён лишь при проверяемом profile evidence, что
изменяемые bytes не определяют evaluated graph/options вне refresh mechanism.
Unknown/non-admitted target/import/condition даёт graph dirty и ordinary fallback.
Known Source role или имя SDK недостаточны. Не требуется анализ произвольных task
bodies. Сохранить positive supported content case и negative target, меняющий
DefineConstants/AllowUnsafe от existing source bytes.

Dependencies: P-011, P-010; imports inventory не равен content independence.
Implementation freedom: finite profile/доказанные расширения; постоянный запрет всех
пользовательских imports не устанавливается.

## P-014 — Resolution-preserving metadata refresh

Reason: [ARB-015](findings/ARB-015.md).

Affected areas: E2 spec/task-00/03/05 и metadata benchmark scope.

Required change: разрешить refresh changed metadata только при доказанном сохранении
relevant resolution constraints/inventory выбранного профиля. Same path и даже same
identity сами по себе не являются полным predicate. Identity/resolution-sensitive
или unknown случай даёт ordinary fallback. Добавить exact-version DLL replacement
negative control против fresh ordinary result и сохранить реально работающий safe
positive metadata refresh. All-fallback не закрывает заявленный E2 positive scope.

Dependencies: P-011 для source resolution evidence, U-001 для принятых reference semantics.
Implementation freedom: narrow predicate либо проверка resolution; ordinary MSBuild
не называется no-DTB reuse. Full binary hash вместо mvp-fast не предписывается.
