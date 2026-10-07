# E0/task-02.1 — решения Stage A

Дата: **2026-10-04**. Автор: `/root/dependency_remediation`; координатор: `/root`.
Проверенный HEAD: `d3c8d5241c1c1c5d95042ce75bc057b747284cf1`.
Статус: **owner choices A021-01/02/03/06 и D021-04/05 приняты 2026-10-04; finite proof/positive executable packet в работе**.
Владелец утвердил нижеуказанные строгие границы. Normative amendments внесены в spec/cache-contract/UNRESOLVED/design. Принят и реализован partial L021-1 (graph observations/framework selection/input recheck); independent code review и final limited validation приняты: Release build exit0, focused21/21, main1228/1228; reparse subcase NotRun. A021-06 принят и синхронизирован, его future adapter ещё не реализован. Исторические observations до approval сохраняются.
[Packet](implementation-packet-task-02.1.md) разрешает только explicit limited L021-1; полного positive Stage B нет. [Evidence](../evidence/task-02.1-dependency-admission.md)
разделяет фактические прогоны и невыполненные требования remediation.

## D021-01 — workload imports

Свежий evidence: `standard-a33f58cb360a4ef8b42f9937dd0b1e0a/artifacts/`
под `%LOCALAPPDATA%/Temp/roslyn-dependency-spike/`; дополнительный аудит —
`%LOCALAPPDATA%/Temp/task021-stage-a-0441efd9449a435d8fc0dddb8b9a8890/`.
`standard-fresh-audit.json` содержит 16 разрешённых workload imports. Current mono
manifest содержит **четыре Target** и имеет SHA-256
`4C34E5078F81FDD22144063F28EEB51C7027A40E9DF1B18547FE7C03A54AD71F`.
Его точный путь:
`C:/Program Files/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.108/WorkloadManifest.targets`.
Он находится вне `C:/Program Files/dotnet/sdk/10.0.300`.

Установленные исходные XML показывают цепочку:
`Microsoft.NET.Sdk.props:148–155` устанавливает `MSBuildEnableWorkloadResolver=true`
при отсутствии disabling sentinel; `Microsoft.NET.Sdk.BeforeCommon.targets:76`
импортирует `Microsoft.NET.Sdk.ImportWorkloads.targets`; строка 16 последнего
импортирует `WorkloadManifest.targets` через
`Microsoft.NET.SDK.WorkloadManifestTargetsLocator`.
Свежий read-only `-getProperty` на неизменённой standard fixture подтвердил
resolver=true, EnableNETAnalyzers=true, SDK 10.0.300, net10.0, Debug/AnyCPU.
Команда не передавала forcing, resolver/analyzer overrides; проверенные bytes
до/после совпали. Свежий DTB независимо подтверждает imported path.

Рассмотренные варианты:

1. Сохранить правило: любой Target вне фактического SDK directory даёт unknown.
2. После решения владельца допустить отдельное конечное workload-toolset extension
   с независимо повторно разрешённым manifest closure.
3. Отключить resolver, изменить запрос/SDK, доверять имени Microsoft или объявить
   весь dotnet directory SDK. Это не выполняет условия задачи.

**Исторический результат до решения владельца: вариант 1.** Для обязательных неизменённых fixtures при
текущей установке resolver положительный control не найден. Это вывод о заданных
requests/установке, а не доказательство невозможности любого другого SDK/request.

**Принятая владельцем поправка A021-01 — 2026-10-04:**

> SDK directory остаётся точным каталогом, разрешённым по Sdk.props. Отдельное
> workload-toolset extension может допустить Target-bearing imports вне него
> только при принятом version-specific контракте resolver inputs, который
> независимо определяет точный выбор manifests, все выбранные files/transitive
> imports и каждый влияющий на разрешение positive/negative input. Discovery
> записывает roots, identities и content hashes; fresh reuse повторяет выбор без
> MSBuild, subprocess и DLL execution и сравнивает полный упорядоченный inventory.
> Новый/исчезнувший/изменённый selector/member, unresolved conditional import,
> resolver override или неполный bounded probe отвергают весь request. Произвольные
> файлы под dotnet и package/custom targets это исключение не получают.

Принятие текста само по себе не доказывает полноту resolver. Точный versioned
selector/negative-input set остаётся предметом D021-03.
Affected normative locations: epoch-0/spec — выбранный admission profile и правило
custom-target Razor/Web; cache-contract §3.2; U-ARB-06. Affected design: §3.2/§6/§7,
packets 01/03/04. Решение владельца получено; contracts согласуются до использования исключения в коде. Finite resolver proof по-прежнему обязателен.

## D021-02 — SDK analyzer provenance

В свежем standard compiler inventory восемь различных DLL; в реальном TaskOutput
Analyzer — шесть pack DLL. Не имеют TaskOutput:

- `Sdks/Microsoft.NET.Sdk/analyzers/Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll`;
- `Sdks/Microsoft.NET.Sdk/analyzers/Microsoft.CodeAnalysis.NetAnalyzers.dll`.

`Microsoft.NET.Sdk.Analyzers.targets:123–134` добавляет их через ItemGroup, не task
Output. Это различие источников, не проблема hash. Свежий
`App-net10.0-dtb-None-observed.json`, compiler-derived admission entries, hashes
SDK XML и `standard-fresh-audit.json` сохраняют оба независимых inventory.
Реальный TaskOutput для этой пары в проверенных командах не получен.

Варианты: сохранить TaskOutput-only; принять independently evaluated SDK item
provenance как отдельный источник; изменить fixture/targets, чтобы искусственно
получить output, или исключить NetAnalyzers. Третий вариант отвергнут.
**Исторический результат TaskOutput-only: OverlayReady=false.** Compiler path/hash
и IsImplicitlyDefined не создают TaskOutput и не разрешают loading.

**Принятая владельцем поправка A021-02 — 2026-10-04:**

> Analyzer компилятора может иметь реальный TaskOutput Analyzer provenance либо
> отдельно захваченный SDK-evaluated-item provenance из admitted exact SDK XML
> producer. Эти источники остаются различимыми. Второй указывает определяющий
> XML file/item/condition и exact evaluation/consumer inner instance, сохраняет
> evaluated metadata, доказывает отсутствие замены/добавления contribution из
> project/import/target и совпадает с полным compiler set. SDK identity, executable
> dependency closure и fresh no-build revalidation producer/selection обязательны.
> Explicit non-provenance DLL остаётся запрещённой. Evaluated items нельзя
> перекодировать в вымышленные TaskOutput events.

Потребуются отдельное portable representation источника и проверенный adapter
contract до создания existing CapturedAnalyzerProvenanceItem. Текущая модель
кодирует task context; придуманные task IDs запрещены. Если truthful representation
невозможно при запрете production edits, интеграция остаётся blocked и возвращается
владельцу. Affected normative: spec выбранный overlay, U-ARB-04/06 supporting record,
cache-contract §5 reference. Design §3.1/§7 и packets 01/03/04 меняют TaskOutput-only
предпосылки согласно полученному решению владельца; truthful representation остаётся обязательным.

## D021-03 — restore, toolset и executable closure

Runner безусловно добавляет три unproven категории; полного restore/toolset/
dependency inventory он не создаёт. Existing no-build test проверяет exact
global.json и один BundledVersions selection для default net10 pack, не общий
resolution algorithm. AnalyzerPrivateDependencyInspector прямо описан как
main-only refusal detector. AssemblyRef не доказывает отсутствие reflection,
native/resource/config/file reads или других dynamic dependencies. Suitability
gate проверяет session/status и не доказывает closure за вызывающий код.

**Действующее правило: сохранить все unproven reasons и оба false flags.**
Ниже конечные proof obligations для proposed bounded contract, а не утверждение,
что accepted complete input set уже получен:

- Restore: ограничить начальный positive research неизменёнными package-free
  framework fixtures, exact local packs, существующим clear-feeds config и
  отсутствием downloads/NuGet-reference fallback/overrides. Установить применимые NuGet configs
  и ancestor absence, restore graph/dgspec, assets, generated .nuget.g.props/targets
  и metadata/reference selection. Independent resolver воспроизводит compiler
  reference paths/properties/aliases каждого exact inner instance. Packages и
  ambiguous generated imports отказывают до отдельного аудита.
- Toolset: раздельно SDK tree, runtime MSBuild/host contracts, выбранные packs
  и только при A021-01 workload extension. Нужны membership и bytes manifest,
  а не несколько наблюдённых файлов. Включить exact pin selection, разрешённые
  resolution properties, sentinels, ancestor/global config и absent selectors.
  Принятый algorithm должен задать конечные selectors и внешние чтения; hash
  каталога сам по себе этого не доказывает.
- Analyzer: каждой допускаемой binary version нужен проверенный по исходникам
  executable-input contract: main/private/transitive DLL, exact shared host
  contracts, resources/native/config/additional inputs и способы resolution.
  PE inspection проверяет только static references. Необъяснённые dynamic
  operations отказывают. Все разрешённые executable/resource inputs хешируются;
  proof привязан к exact producer/binary versions. Discovery не выполняет DLL.
- Fresh reuse: повторить принятые selectors только чтением диска, сверить absence,
  весь membership и bytes, заключить чтение между двумя probes и отказать при
  изменении, reparse, unreadability или превышении bounds. Запрещены restore, DTB,
  Open*, subprocess и DLL loading из DTO. Каждый proof имеет source/version.

**Принятое решение A021-03 — 2026-10-04:** владелец разрешил разработку перечисленных bounded proof contracts; это не заменяет closure доверием к установленному Microsoft code.
Каждый конечный контракт по исходникам и точная schema требуют review до positive
packet. Если владелец выбирает installation trust вместо dependency proof, это
другая нормативная гарантия, здесь не предложенная как уже выполненная.
A021-01/02 сами по себе не закрывают D021-03 и не разрешают supported.
Affected: design §3.2/§7, packet-01 completeness/loader, packet-04 stability;
cache-contract §§2/3/5/8 — если владелец меняет гарантию.

## D021-04 — graph и binding новой сессии

Для отказа при ambiguity нормативное расширение не нужно. Runner обходит только
SelectedRoots, делает outer evaluation, сохраняет только inspected root instances,
возвращает ExpectedEdges=[], imports с Owner=null и path-only compiler/TaskOutput
owners. Exact context/metadata для multi-inner proof теряются.
DependencyContextObservation.InstanceId имеет смысл только внутри своего лога.

Решение D021-04 подтверждено владельцем: полный independent graph обязателен; до proof сохраняется refusal. Executable packet должен определить portable roots,
все transitive exact inner keys/edges с aliases/EmbedInteropTypes; отдельно raw
(binlog identity + full event context) association и portable identity.
Independent evaluated edges сверяются с reference resolution/compiler contexts
каждого instance, включая missing reference, soft warning и legally empty project.
-getItem и переданные roots сами по себе graph proof не дают. Для baseSolution
нужен ровно один кандидат на доказанный portable key; Project.Name, первый path
match и runtime IDs не задают TFM. Single-instance path допустим только когда
independent inventory доказывает uniqueness. Multi-inner ambiguity остаётся refusal
до explicit map contract с host/reader. Новые model fields/types этим blocked
packet не dispatch-ятся. Реализация D021-04 и обязательный positive graph M021-01
отложены за D021-01/02/03.

## D021-05 — Razor/Web и exact golden

Решение D021-05 принято владельцем 2026-10-04: сохранить negative Razor/Web scope; CSS regions/golden не менять. В exact Marker.razor generated class и member
Marker конфликтуют (CS0542); переименование и подавление не выполняются.
Scoped CSS witness показывает изменение generated output при совпадающих selected
regions/preexisting hashes. Наличие Web hidden asset не закрывает CSS/package
external assets. Linked Content не является NuGet static-asset experiment.

В этой задаче выбран Razor/Web negative. Возможное будущее отдельное расширение должно явно изменить
regions/negative producer inputs для CSS и всех принимаемых static asset sources
с mutation/absence tests, а failing golden исправить отдельным fixture-design
решением. CSS glob не добавляется молча. Сохранение отказа не требует изменения
нормативного профиля; positive SDK control не разрешил бы positive Razor/Web.

## Gate и handoff

Owner approval A021-01/02/03 и D021-04/05 получен; дальнейший gate D021-03 —
конечного source-backed closure proof; D021-04 implementation отложена;
D021-05 сохраняет negative scope. Reporting tests не закрывают эти решения.
M021-01 supported control и M021-10 fresh Complete overlay остаются NotRun.
Dependency readiness и oracle public-API readiness — два отдельных hold;
task-04 stages 2–7 вне этой задачи.

### Уточнение ordered workload selection — 2026-10-04

Принятая A021-03 запрещает NuGet/restore/reference fallback, downloads и overrides;
SDK выбирается exact pin с `rollForward:disable`. Штатный ordered выбор workload
manifests по feature-band входит в A021-01 только как доказанный version-specific
algorithm. Название upstream `FallbackForMissingManifest` не исключает эту ветвь
и не разрешает произвольный fallback. Для текущей установки отсутствие primary
`10.0.300`, выбор secondary `10.0.100`, installstate, workloadsets, installer/userlocal
markers и все более приоритетные положительные/отрицательные selectors входят
в independent no-build re-resolution, повторный probe и mutation matrix.



## D021-06 / proposed truthful envelope binding location — не принято

Это новый узкий representational вопрос после source/code audit; A021-01/02/03 и
D021-04/05 остаются принятыми и повторного approval не требуют. Production
CapturedAnalyzerProvenanceItem требует genuine TaskContext. Production Bind выдаёт
MissingSourceMetadata для pack TaskOutput без source project, а путь Confirmed
создаёт SourceProjectId. У SDK-evaluated items нет genuine TaskContext вообще.
Production snapshot Complete означает отсутствие replay failures, не equality
всего compiler inventory; это подтверждено CaptureService.BuildSnapshot. Поэтому
нельзя получить нужные8consumer bindings через production Bind, подставляя SDK
или pack producer как фиктивный project.

Предлагаемый truthful seam: fresh isolated envelope содержит production snapshot
с genuine TaskOutput/context observations и отдельный discriminated source model
с полными independent fresh consumer bindings всех8DLL. Production snapshot
bindings сохраняют его реальный смысл; неподходящие bindings не переименовываются
в Confirmed. Existing gate остаётся обязательным для same-session Complete snapshot,
а isolated host дополнительно проверяет полноту union/compiler/DTO/closure и
однозначность fresh consumer bindings. Наличие envelope без доказанного R07 или
неизвестный source по-прежнему отказывает до DLL loading.

Reviewer `/root/independent_review` указал буквальную коллизию с R021-08: там полные
bindings требуются именно внутри AnalyzerProvenanceSnapshot. A021-02 явно разрешает
distinct SDK source, но не переносит binding obligation в envelope. Этот вариант
поэтому **не executable** до отдельного решения по месту представления bindings;
это не утверждение, что любой другой truthful isolated adapter невозможен.

Concrete proposed amendment только к binding representation R021-08:

> Создать новый isolated admission envelope с новым nonempty LoadSessionId,
> текущим toolset, fresh AnalyzerProvenanceSnapshot реальных TaskOutput/context
> observations и полными independent consumer bindings для объединения genuine
> TaskOutput и SDK-evaluated sources. Production snapshot сохраняет свой native
> TaskOutput/binding смысл без fabricated task IDs, SourceProjectId или Confirmed.
> Existing suitability gate должен принять его actual same-session Complete
> capture; isolated host отдельно и обязательно проверяет полную typed-source
> coverage и fresh consumer bindings всех compiler/DTO DLL перед DLL loading.

При принятии потребуются синхронные точечные clarifications task R021-08,
spec A021-02, cache-contract A021-02 и packets01/02.1 consumer seam; production
models/gate не меняются. R07 closure, M021-08/10, full graph, no-process revalidation,
wrong-session/missing/extra/refusal matrix и required generator positive не
ослабляются. До решения affected envelope C# не реализуется, independent source
proof/selection work продолжается. Не заменять это amendment общей установочной
доверенностью или consumer-only отказом от diagnostics.

### A021-06 принято владельцем — 2026-10-04

Владелец явно выбрал вариант1: truthful typed envelope с описанным выше exact
R021-08 binding-location amendment. Предыдущий раздел D021-06 сохраняет предложенный
текст и причину решения как историю; текущего отсутствия owner approval больше нет.
Нормативный paragraph R08 и supplements spec/cache-contract/packet01 синхронизированы.
Новые concrete signatures/schema/host consumer integration и finite proof positive
packet всё ещё требуют independent review до C#. Остальные принятые owner choices
не пересматриваются; full closure/positive control/fresh overlay не объявляются ready.
