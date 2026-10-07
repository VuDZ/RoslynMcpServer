# E1 — Первый disk cache MVP

Статус: **spec-v2 / revised-for-review, 2026-10-07**. Реализация не начата.

Общие границы — [README серии](../README.md), [порядок исполнения](../execution.md),
[бенчмарки](../benchmark.md). Эти три документа обязательны для ревью эпохи.

## Результат

Первый production MVP: ordinary load сохраняет snapshot и manifest; новый PID
проверяет их, восстанавливает поддержанный request и подключает существующие
watcher’ы. Неизменный реальный request даёт disk hit. Любое обнаруженное
значимое изменение пока даёт whole-request ordinary load с причиной.

## Стартовая проверка

Manifest ограниченного profile должен покрывать known input bytes и появление/
удаление потенциальных inputs. Standard absent walk-up и actual imported/restore
paths не подменяются csproj/Documents. Источник import/restore evidence остаётся
открытым **S-002 / not-run** до его actual design-time проверки и принятия task-00.
Current map/analyzer snapshot не считаются готовой closure; None у BinaryLogger
не доказывает отсутствие raw path events. Второй log/Embed/source collection не
выбираются заранее. Required category → source event/property → path → all consumer
instances и completeness boundary должны быть traceable; missing/conflicting
context → incomplete/unsupported. All-unsupported request set не закрывает E1.

Presence/known-absence probes включают применимые ancestor `.editorconfig`/
`.globalconfig`, explicit config paths/regions для project и linked sources.
Разная applicability config видов отражена в profile. Repo root не является
универсальной границей discovery; unknown boundary даёт fallback.
Неподдержанный custom input/import/instance и budget/I/O failure не дают hit.

Schema/SDK/MSBuild/Roslyn/version-resolution fingerprint проверяется независимо.
Current loader globals определяются до open по ordinary precedence и сравниваются
с canonical path; absent не wildcard, evaluated saved defaults не oracle.
Profile описывает значимые environment dependencies и independently current
context; одна policy действует для capture и lookup. Unknown→unsupported;
совместимый conservative fingerprint или доказанный relevant-variable subset
допустимы. Raw secrets не сохраняются. Controlled environment, меняющий ordinary
semantics, не разрешён автоматически. Полный SDK installation scan не обязателен.

`mvp-fast` binary policy и её documented blind spot остаются прежними.
Dependency paths, generation binding, environment validation и role facts —
отдельные основания; одно не является доказательством остальных.

## Интеграция

Минимальный host ownership seam отделяет MSBuild-only open/diagnostics от manager-
owned host. Load/hydrate, prepare/admission и atomic publication выполняются
под одним acquisition. Failed/cancelled candidate освобождается без частичной
publication; пригодная same-key сессия сохраняется до успешной замены. Другой
старый key не выдаётся как новый результат. Есть одна input map/watcher session
на новую manager generation, с сохранённым non-CSharp layer и write invariants.

Watcher включён до финального candidate probe; revision comparison/retained pending
events закрывают scan→watch→publication interval. Failed watch или graph/membership
mutation отменяет disk candidate и вызывает ordinary fallback. После publication
сохраняется current live policy, без второго index или blanket freshness/read ban.

Hydrated map восстанавливает proven occurrence roles, all owners/memberships и
producer bindings с новыми IDs; unknown не становится UserInput, evaluation
role имеет приоритет. Public flags либо portable facts допустимы. On-disk
generated role не разрешает DTO generator text или cached execution provenance.
Ordinary/hydrated user/generated/unknown/additional/config/output/shared maps
должны иметь одинаковое подтверждённое значение.

Mode matrix: допустимый overlay-off base reuse требует пригодного fresh preparation
и остальных gates. Overlay-on без подтверждённого fresh binding вызывает whole-
request ordinary load **до** publication disk candidate. Этот fallback достаточен
для минимального E1. Overlay hit не обязателен, но permanent exclusion будущего
adapter также не устанавливается. Provenance не фабрикуется сменой IDs; refs
не удаляются ради pass. Не создавать Unavailable только из-за cache attempt
для пригодного ordinary request; реальные ordinary bans/restart-required остаются.
New-PID overlay controls и benchmark сообщают actual fallback, отдельно от overlay-off.

Production `Workspace.TryApplyChanges` имеет ровно один reference site в existing
SolutionManager.TryApplyWorkspaceChanges. Public candidate construction допустим
без второго site, но не обходит preflight/publication. После production integration
выполняется весь SourceStructure с exact limit inventory; isolated prototype
calls не копируются вслепую. Artifact ownership E0→E1 указан в acceptance evidence.

## Store и поведение

Store сохраняет один versioned snapshot+manifest envelope на key в user-private
directory, с bounded codec/integrity и unique temp + atomic replace. Readers
получают целый old/new envelope; multiwriter не повреждает payload. Большой
generation GC/lease/repair протокол не вводится. Cache I/O failure не превращает
successful ordinary load в ошибку; source/session/ProjectIds/shadow paths и
operation contexts не становятся переносимым state.

Capture eligibility: пригодный ordinary load без blocking load failure и с
evidence, что evaluated options/memberships/references принадлежат тому же input
generation, который описывает manifest. Post-load stability не является таким
evidence; pre/post equality не доказывает отсутствие ABA. При отсутствии binding
reusable capture запрещён, successful ordinary load сохраняется. Newly discovered
dependency/region требует предусмотренного bounded повторного подтверждения
либо отказа capture. Capture после prepare требует сохранения этого binding;
source held state и фактически consumed bytes не связываются новой hash задним числом.

Допустимый envelope хранит portable graph-health/coverage facts и происхождение
нужных bounded diagnostics. Непригодная blocking загрузка не записывает usable
envelope. Пустые hydrate diagnostics не повышают Unknown до Complete и не
стирают ordinary verdict. Fresh watcher/execution/session state пересчитывается;
old Banned/Unavailable и полный LastDiagnostics не обязательны к сериализации.

Executable E1 codec совпадает с tested E0 artifact либо перенос/замена явно
зафиксированы с bounds/malformed/round-trip suite против поставляемого кода.
Production не зависит от Tests assembly; prototype evidence не заменяет checks.

Raw requested/merged loader globals/evaluated values и закрытая parameter matrix
изложены в [README](../README.md#поведение-первого-mvp). BuildArgs/diagnostic/policy
flags не меняют base graph; overlay admission обязателен независимо от разбиения
storage. Force bypass и RAM omitted behavior сохранены. ConfigFile без отдельного
opt-in остаётся disk-disabled (lookup/capture/write отсутствуют); ExplicitLoad
useDiskCache=true включает только supported scope. Default/new config key не
вводятся подразумеваемо. Enabled force ordinary capture допустим после bypass.

Existing `.cs` persistence проходит current write preflight/exact inverse и
session/base checks. Unsupported add/remove/rename/`.csproj` edits на hydrated
host отвергаются до disk side effects с ordinary-load route. Partial persistence
не подменяется обещанием multi-file rollback.

## Приёмка

Новый PID/unchanged hit, изменённый source или graph → miss, corruption/version
mismatch, capture/store failure, cancellation, watcher startup race, shared file,
safe `.cs` write и штатные analyzer lifecycle regressions. Проверки используют
production entry, а не только isolated host. Для сравнения ordinary и hydrate
нужны project/document inventories и заранее выбранные semantic assertions.
Измеряются unchanged hit, forced load и miss overhead; time-to-first-useful
должен улучшаться на поддержанном реальном контроле. Отрицательный результат
фиксируется как неприёмка performance с причиной, без изменения benchmark после замера.

## Дополнительные обязательные controls после арбитража

- Identity: omitted vs explicit Release, different FileSettings при одинаковом
  raw request, inherited Loaded* при смене workspace, normalized Any CPU, mode matrix.
- Environment: absent→present и changed-value new PID, прежние project bytes,
  сравнение semantic result со fresh ordinary load.
- Capture: props mutation после consumption до первого post-load probe не допускает
  usable envelope; dependency source и bytes generation проверяются раздельно.
- Health/roles: blocking load no-capture, ordinary/hydrated verdict/loadGraphComplete/
  coverage и occurrence maps parity; новая watcher failure остаётся видимой.
- Discovery: create/delete применимого ancestor config до первого E1 hit,
  linked-source boundaries и known config change как отдельный case.
- Entry/mode: lazy без opt-in disabled и explicit enabled; overlay-on без доказанного
  fresh binding даёт actual ordinary fallback, не artificial Unavailable.

Все первоначальные restart/corruption/cancellation/write/semantic controls и
performance criteria остаются обязательными. U-001/S-001/S-002 должны иметь
необходимые для supported request результаты; text revision не засчитывает их runs.

## XML scope восстановленной сессии

[H-001](../human-decisions.md) исключает documentation provider/text бинарных
references из гарантии cache equivalence и XML-only validation. Остальные metadata
properties и inputs/graph gates сохраняются; XML с mandatory другой ролью проверяется.
Source comments загруженных проектов сохраняются. Support profile/help/outcome
явно обозначает XML ограничение; documentation-dependent запросы и analyzer/generator
execution идут на ordinary route либо получают явный отказ до side effects.
Required controls: source-comment parity, объявленная external XML потеря и
documentation-dependent fallback/refusal. Strong-name граница принята по H-002 ниже.

## Strong-name boundary H-002

[H-002](../human-decisions.md#h-002--ограниченный-strong-name-contract-mvp) закрывает
U-001 выбором B. Hydrated scope сохраняет заявленные semantic results, diagnostics
поддержанных операций и корректность поддержанных writes только в подтверждённом
signing-independent profile. Signing-dependent/unknown request идёт целиком через
ordinary load до зависимой операции/side effects; невозможность перехода даёт отказ.
Emit/signing из hydrated compilation вне первоначального scope. Effective options,
assembly attributes и project bindings учитываются; подписанная внешняя DLL сама
по себе не означает unsupported. Flags/csproj/references не меняются ради pass.
Изменение inputs либо planned write, создающее signing-зависимость/неопределённость,
требует повторного допуска и ordinary route до зависимого результата или persistence.
Required controls: реальный supported semantic/write scope и signing-dependent,
unknown, excluded-emit cases с fallback/refusal до execution/side effects.
S-001 подтверждает конечный профиль; exact hidden-state fidelity не заявляется.
Support/help/outcomes и benchmark compare key отражают H-001/H-002 ограничения.
H-002 bypass/capture skip/fallback/refusal явно логируются: stage, project instance,
configuration/TFM, operation, конкретная signing-dependent/unknown/excluded причина
и ordinary/refusal route. Общего cache miss недостаточно; ordinary failure отделён
от ограничения cache profile, secrets/key contents не выводятся. Обязательны controls
причины/маршрута в S-001 и actual log records на E1 integration.
