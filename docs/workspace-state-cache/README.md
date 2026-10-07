# Workspace state cache — specification v2

Статус: **revised-for-review после арбитража, 2026-10-07**. Реализация не начата;
runtime, версия и defaults этим пакетом не меняются.

Владелец выбрал постепенное сохранение/восстановление workspace state с индексом
оффлайн-изменений и существующими watcher’ами. Прежняя большая программа
[workspace load cache v2](../backlog/workspace-load-cache/README.md) отложена;
её experiments/findings используются по применимости, её O1–O8, пять verdicts,
closure gates и обязательные implementation packets не наследуются новым MVP.
Текущие production write/admission/lifecycle инварианты остаются обязательными.

## Цель и граница

Ускорить первый полезный semantic-запрос в новом PID за счёт восстановления
описания уже загруженного решения. Сохранять projects/options/documents/references
и индекс inputs. Исходники читаются с диска; Roslyn Solution/Compilation/SyntaxTree,
source/generated texts, session IDs, shadow paths и operation contexts не сериализуются.
Не использовать git/ast, `.vs` cache и private Roslyn storage для validation.

Поддержка наращивается по работающим контролам. Первый disk MVP допускает
whole-request fallback при изменении входов. Затем добавляются content-only
refresh и отдельно selective project reopen. Положительный результат нужен
на неизменённом реальном проекте/решении, не только на synthetic fixture.
Неподдержанные MSBuild inputs и ambiguous TFM честно идут на ordinary load;
универсальная полнота произвольного build graph не обещается.

## Маршрут

- [E0 — snapshot round-trip](epoch-0-snapshot-roundtrip/README.md): 9 задач;
  ordinary capture, минимальный DTO/codec, isolated hydrate, real query/edit,
  baseline и restore benchmark. Production path ещё не меняется.
- [E1 — disk cache MVP](epoch-1-disk-cache-mvp/README.md): 12 задач;
  input/binary probe, store, host/preparation/load/watch/write integration,
  restart/failure tests и hit/miss benchmark. Это первый полезный production scope.
- [E2 — content refresh](epoch-2-content-refresh/README.md): 7 задач;
  offline dirty owners, source/additional/config/metadata refresh, новый capture
  и edit/build → restart benchmark. Graph/membership change сохраняет fallback.
- [E3 — project refresh](epoch-3-project-refresh/README.md): 7 задач;
  narrow reopen spike, mapping/adapter/membership/publication, tests и benchmark.
  No-go spike оставляет E1/E2 пригодными и downstream selective scope deferred.

Всего **35 небольших задач**: исходные 33 и два отдельно выделенных bounded spikes S-001/S-002. Суффиксы `low/med/hi/xhi` обозначают классы
сложности, модели и reasoning указаны в шапках. Несколько рекомендованных моделей
допустимы; их равная пригодность не считается установленной. Новые внешние
альтернативы проверяются на фактическом scope, а история фиксирует точную модель.

## Индекс входов

При ordinary load сохраняются пути, роли и все projects/memberships-потребители:

- C# documents, AdditionalFiles и analyzer-config documents независимо от расширения.
- Загруженные `.sln`/`.slnx`/`.csproj`, реально импортированные `.props`/`.targets`,
  стандартные walk-up candidates `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props`, `global.json`, применимые NuGet.Config.
- Restore inputs по реальным путям: assets, lock/config files, импортируемые
  `*.nuget.g.props/targets`. Explicit inputs из obj и внешние linked paths
  проверяются точечно, даже если фоновой обход их каталогов pruning исключает.
- Реально используемые metadata/analyzer/generator binaries; signing key/ruleset
  и другие явно заданные compiler inputs, когда они присутствуют в выбранном срезе.
- Каталоги возможного появления source/config inputs; known absence фиксируется,
  чтобы обнаружить новый ancestor config/import. Каталог оценивается по sorted
  включённым именам/видам/child hashes, не по Directory.LastWriteTime.

Обход ограничен каталогами проектов, необходимыми ancestor candidates и explicit
dependencies; node_modules/VCS/build output trees целиком не сканируются.
Файл с двумя ролями получает более строгую evaluation роль. Новый path в region
привязывается к владельцам region и вызывает membership dirty до новой evaluation.
Профиль задаёт, как обнаруживаются новые файлы каждой поддержанной роли;
одни hashes уже известных Documents не заменяют membership scan.

`.config`/`.json` сами по себе не означают compiler input. Runtime-only appsettings,
web/App.config не включаются автоматически; actual build/additional role включает
их в manifest. Razor/XAML/resx входят при явно принятой поддержке соответствующего
build profile; пока такого capture/generation path нет, request получает fallback.
Custom targets с неизвестными дополнительными inputs не считаются поддержанными.

## Проверка бинарей

Proposed fast policy: path + size + LastWriteTimeUtc для всех; full hash до
**16 MiB** включительно, для больших — SHA-256 первых/последних **64 KiB**.
Один physical path читается один раз. Изменившаяся stamp/bytes сразу даёт dirty;
full hash гигабайтного уже изменённого файла ради подтверждения dirty не нужен.
Порог и chunk size могут меняться по отдельному решению после benchmark.

Это эвристика `mvp-fast`: изменение середины большого файла при сохранении размера,
timestamp и краёв может остаться незамеченным. Manifest/response/report сохраняют
название validation policy и её параметры. Atomic/repeated stat не доказывает
невозможность такой подмены. Integrity checksum самого envelope остаётся полным.
Текущие analyzer DLL execution/shadow-copy checks этой эвристикой не заменяются.
Полный binary validation может появиться отдельным режимом позднее; он не prerequisite E1.

## Поведение первого MVP

API остаётся proposed до принятия revised spec: `useDiskCache=false`,
`forceReload=false`. False запрещает disk lookup/capture/write. Force обходит RAM
и чтение disk state; при включённом cache после успешной ordinary загрузки
разрешён новый пригодный capture. `reset_workspace` очищает RAM, не disk.

### Три уровня load properties

1. **Raw requested** — фактически переданные аргументы с отдельным absent marker.
2. **Loader globals** — независимо разрешённые до MSBuild open normalized значения
   по текущему ordinary precedence: explicit → current Loaded* → FileSettings.
   Отсутствие значения остаётся отсутствием; Configuration/Platform/TFM не
   угадываются из найденного envelope. Platform `Any CPU` нормализуется как сейчас.
3. **Evaluated project values** — значения после MSBuild, в том числе defaults
   и inner-instance context. Они сохраняются для восстановления/audit, но не
   являются oracle текущего request.

Disk admission сравнивает canonical workspace path и независимо полученные текущие
loader globals. Cold absent не является wildcard. Project defaults покрываются
проверкой inputs/environment, не подстановкой saved values. Raw requested-only
tuple недостаточен при разных FileSettings/inherited globals. Более строгий
storage partition допустим, если даёт лишь лишние misses. RAM matcher сохраняет
свой прежний omitted/sticky контракт; disk lookup его не переиспользует.

### Закрытая матрица параметров

- `workspacePath`, Configuration/Platform/TargetFramework — canonical base context
  по independently resolved loader globals и explicit absence.
- `shadowCopyInSolutionAnalyzers` — current mode admission и fresh preparation.
  Separate storage key допустим; sharing clean base допустим только при доказанной
  parity и обязательной проверке/prepare режима каждого request. Overlay session,
  provenance и shadow paths в envelope не переносятся.
- `buildArgs` — suffix будущих CLI builds, вне base graph identity. Current
  session suffix сохраняется/обновляется по existing rules независимо от disk lookup.
- `briefOutput`, `logProjectOutputDiagnostics` — presentation/diagnostics, вне base identity.
- `useDiskCache`, `forceReload` — policy/bypass, вне semantic base identity.

### Load entry и modes

**ExplicitLoad:** `useDiskCache=true` включает предусмотренный supported scope.
**ConfigFile:** без отдельного opt-in lazy load disk-disabled; lookup/capture/write
не выполняются, outcome `disabled`/`not-attempted`, current FileSettings globals
работают. Новый config key и default не вводятся подразумеваемо. Explicit benchmark
не является evidence ускорения lazy entry; будущее config opt-in — отдельный scope.

Overlay request без подтверждённого fresh binding выполняет whole-request ordinary
load до publication disk candidate. Этот fallback достаточен для минимального E1;
обязательный overlay hit и постоянный запрет будущего adapter не вводятся.
Нельзя фабриковать provenance, удалять references или создавать Unavailable только
из-за cache attempt для пригодного ordinary request. Настоящие ordinary
Banned/Unavailable и restart-required сохраняются. Новый PID/key создаёт новую session;
current sticky RAM behavior остаётся прежним. Overlay-on/off outcomes измеряются отдельно.

### Capture, restore и ответ

Reusable capture выполняется после пригодного ordinary load без blocking failure
и только при evidence, связывающем graph с manifest input generation. Стабильные
post-load hashes сами по себе не являются binding. Capture до ответа load
включён в load latency. На enabled RAM hit без evidence скрытый force capture
не обязателен: `capture=not-attempted` с причиной. При новом PID read/probe/hydrate/
fresh prepare/publication предшествуют выдаче semantic workspace. E1 обнаруженные
changes дают ordinary fallback; E2/E3 расширяют reuse по собственным predicates.
Cache I/O fault не отменяет успешную ordinary загрузку; незаконченный candidate
не публикуется, старый законченный envelope не повреждается.

Ответ/лог сообщает `baseGraphSource=ram|disk|msbuild`, effective cache policy,
validation profile, capture/write outcomes, bounded reason и число dirty проектов,
а также проверенные portable graph-health/coverage facts и происхождение нужных
diagnostics. Пустые hydrate diagnostics не повышают Unknown до Complete.
Fresh watcher/execution/session состояние вычисляется заново; old ban не становится
fresh admission. RAM hit не называется disk hit. Различаются `corrupt`, `incompatible`,
`unsupported`, `inputs_changed`, `membership_changed`, `hydrate_failed`,
`prepare_failed`, `disabled`, `forced`. Diagnostic/log strings в C# — English.

Hydrated writes проходят current preflight/exact inverse/session/base checks.
Unsupported existing операции отвергаются до side effects с ordinary-load route.
Public initial candidate construction допускается, но production
`Workspace.TryApplyChanges` имеет ровно один reference site в existing manager
wrapper. Ни construction, ни перенос isolated prototype не обходят publication/write gates.

Rationale: [identity/modes, capture/health и load-entry решения](archive/arbitration/decision-ledger.md).

## Ревью и исполнение

[Порядок исполнения](execution.md), [benchmark protocol](benchmark.md),
[инструкции рецензенту](review/README.md) и [общий стандарт учёта](../README.md#учёт-задач-ревью-и-статистики).
Spec и задачи каждой эпохи проверяются как один пакет; review task на модель
не заводится. Принятие плана, task acceptance и приёмка реализации различаются.
Public activation отдельной работающей эпохи может рассматриваться после её
validation; будущие эпохи не блокируют уже принятый scope. Default остаётся
выключенным, пока владелец отдельно не выбрал изменение default.

До запуска E0 требуется принять его план с общими документами. Реализация
следующей эпохи начинается после принятого применимого scope предыдущей.
Результат E3 может быть deferred; это не отменяет принятый MVP E1/E2.
Серия не обещает закрыть требования отложенного v2.

## Статистика

- Исторический review исходной версии: три catalogs, 16 findings; 15 ARB outcomes.
  [Decision ledger](archive/arbitration/decision-ledger.md) сохраняет source identities.
- Revision P-001–P-014: применены к тексту; не засчитаны как reviewer-confirmed fixes.
- Решения владельца H-001/H-002, 2026-10-07: external XML exclusion и strong-name B
  применены к тексту; U-001 resolved. [Scope и validation](h-002-report.md).
- Ревью specification v2: модели/даты/раунды/исправления/отчёты —; recheck не выполнен.
- Ревью задач v2: охват всех 35 задач; реализация не начата; раунды/исправления —.
- Приёмка реализации E0–E3: не начата; раунды/исправления —.

Применение P не означает принятия epochs. U-001 закрыт отдельными H-001/H-002;
S-001/S-002 остаются not-run.
Plan/task/implementation counts не смешиваются; unknown values остаются `—`.
Дубли источников и разные models сохраняются по общему стандарту учёта.

## Подготовка пакета

**2026-10-07: specification v2 revised-for-review.** Scope: все 14 P-изменений,
сохранённые original requirements, четыре эпохи и 35 небольших tasks, два
открытых spikes, один resolved owner decision point и traceability.
[Revision report](preparation-report.md) и [change ledger](change-ledger.md)
фиксируют проверку и ограничения. U-001 resolved по H-001/H-002 (B); positive E0/dependent
implementation требуют successful S-001. [H-002 report](h-002-report.md) фиксирует
принятый 2026-10-07 scope и docs-only validation.

## Revision provenance и открытые gates

Применены P-001–P-014 из завершённого [арбитража](archive/arbitration/result.md).
[Change ledger](change-ledger.md) перечисляет actual sections и derived consistency
changes. [Исходная версия](archive/README.md) и review/defense/arbitration перенесены
в [archive](archive/index.md); ссылки rebased, исторические решения и snapshots
сохранены. Historical prescriptions не отменяют arbitration outcomes.

> **RESOLVED U-001 — H-001/H-002, вариант B, 2026-10-07**
>
> XML-документация бинарных зависимостей исключена из гарантии по H-001;
> source comments загруженных проектов сохраняются. H-002 ограничивает hydrated
> scope подтверждёнными signing-independent проектами/операциями. Signing-dependent
> и unknown cases используют ordinary load до операции; emit/signing из hydrated
> compilation вне MVP scope. Remaining state и correctness сохраняются.
> Negative-only E0 не принимается; successful S-001 остаётся обязательным.
> Подробности — [decision point](unresolved.md#u-001--fidelity-положительного-e0).

Открытые [S-001/S-002](spikes.md) **not-run**. S-001 следует за U-001; S-002 должен
подтвердить import/restore evidence до принятия E1 manifest. Они являются gates,
не выбранными implementation mechanisms. Новых DEFERRED нет; optional E3 сохраняет
прежний no-go contract. [Revision blockers](revision-blockers.md) отделены от
resolved owner decision point. Public activation/default/implementation
permissions revision не расширяет.

## Evaluation context и переносимые input facts

Profile обязан описать significant environment dependencies; capture и lookup
используют одну совместимую policy и independently current context до reuse.
Unknown dependency означает unsupported/fallback. Допустим conservative fingerprint
либо доказанный relevant-variable subset; controlled environment, меняющий ordinary
semantics, не разрешён автоматически. Raw secrets не сохраняются в envelope/reports.
SDK/MSBuild/Roslyn/schema fingerprint не означает неизменность каждого SDK файла
и не возвращает mandatory full installation closure audit.

Для каждого occurrence сохраняются role, все memberships/owners и подтверждённые
producer bindings с traceable source. Hydrate восстанавливает их в единственной
input map с новыми IDs через public flags либо доказанные portable facts.
Unknown не становится UserInput; имя/path-under-obj не заменяет provenance;
evaluation role имеет приоритет. On-disk generated input не разрешает сериализовать
generator text или cached execution provenance.

Конкретный evaluated import/restore source открыт до S-002. Current input-map/
analyzer snapshot не являются готовой closure; ProjectImports=None не доказывает
отсутствие raw paths. Source contract должен связывать category → event/property →
actual path → all consumer instances и задавать completeness boundary.
Incomplete→unsupported; no positive request означает неготовность E1, не успешный detector.

Actual config discovery profile включает presence/known absence для применимых
`.editorconfig`/`.globalconfig`, explicit config paths и regions. Границы задаются
для project и linked sources с учётом разной applicability config видов;
repo/solution root не является универсальным stopping point. Новые/удалённые
configs обнаруживаются уже до первого E1 hit; unknown discovery даёт fallback.
Bounded point/region probes допустимы без полного recursive ancestor scan.

## XML-документация бинарных зависимостей — решение H-001

В MVP не гарантируется XML-документация символов metadata/DLL references, включая
NuGet/framework/manual DLL и собственные проекты, подключённые как бинарь.
Исходный DocumentationProvider не обязан переноситься; одна эта непрочитываемая
часть не вызывает capture refusal. Остальные reference/options/semantic invariants
обязательны. Support profile/help/outcome явно сообщает ограничение.

Комментарии в исходниках загруженных проектов и parse options сохраняют прежний
contract; ordinary build generation XML не отключается. XML-only documentation
mutation не обязана давать miss, но actual AdditionalFile/import/config/custom
input роли не исключаются по расширению. DLL/restore/graph validation сохраняется.

Documentation-dependent queries/analyzers/generators вне supported hydrated scope:
ordinary route либо явный отказ до execution/side effects. Acceptance включает
source-comment parity и заявленное внешнее XML ограничение/fallback control.
Strong-name fidelity ограничена отдельным H-002; подробности ниже.

[Решение владельца, rationale и принятый риск](human-decisions.md#h-001--документация-бинарных-зависимостей-исключена-из-гарантии-mvp).

## Расположение канона и истории

**2026-10-07: канон specification v2 поднят в корень темы** по запросу владельца.
Четыре эпохи/35 tasks и common contracts находятся здесь; прежний каталог `spec-v2/`
удалён после переноса всех файлов. [Историческая specification/reviews/defense/arbitration](archive/index.md)
сохранены в `archive/` с исправленными links. [Relocation report](relocation-report.md)
фиксирует scope, path/hash mapping и validation; [revision report](preparation-report.md)
сохраняет application evidence. Статус остаётся revised-for-review, tasks planned,
на момент relocation strong-name U-001 был unresolved, S-001/S-002 not-run.
Последующее H-002 закрывает U-001; relocation не acceptance реализации.

## Strong-name — решение H-002

**2026-10-07: accepted owner decision / applied to requirements; U-001 resolved, B.**
[H-002](human-decisions.md#h-002--ограниченный-strong-name-contract-mvp) разрешает
подтверждённый signing-independent hydrated scope без точного переноса hidden provider
state. Signing-dependent и unknown requests используют whole-request ordinary load
до execution/side effects; невозможность перехода означает явный отказ. Emit/signing
из hydrated compilation вне первоначального scope. Подписанные внешние references
не исключаются автоматически; semantic/write correctness и remaining facts обязательны.
Явные логи объясняют H-002 bypass/capture skip/fallback/refusal: project/configuration,
operation, причина и ordinary/refusal route; общего cache miss недостаточно.
Support/help/outcome и compare key описывают ограничения; effective options,
attributes и project bindings входят в admission evidence S-001.

[Отчёт применения и validation](h-002-report.md). Task-00 planned, S-001/S-002 not-run;
реализация, independent review и public activation этим решением не принимаются.
