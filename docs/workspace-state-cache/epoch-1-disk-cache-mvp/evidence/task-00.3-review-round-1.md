# E1/task-00.3 — независимое ревью, round 1

Дата: **2026-10-09**, Europe/Moscow. Рецензент: отдельный Codex reviewer subagent,
независимый от implementer; точный runtime model ID/reasoning не подтверждены
инструментами. Рекомендованная модель задачи не выдаётся за фактическую.
HEAD: `fc72dc80d330874ae5160950e972a8c8b4d67874`. Коммит не выполнялся.

Проверен финальный [contract audit](task-00.3-contract-scope.md), **374 строки**.
SHA256 reviewed report:
`75C65929DB673543FD39DE6C6223C78497B030CD09F3CA8E3A6187048C7D2575`.
Граница исходного dirty дерева: [baseline.json](task-00.3/baseline.json).

Вердикт: **ACCEPT — bounded docs-only negative contract audit**.
Must-fix findings `T003-R1-xxx`: **нет**; открытых must-fix: **0**.
Подтверждённых исправлений после этого independent review: **0**.

Принимается исследование с выводом **not-viable / insufficient evidence как
маршрут разблокировки task-00 сейчас**, одним неприменённым draft D1/D2 и
перечнем оставшихся blockers/будущих controls. Это не доказательство невозможности
профиля, не применение draft, не manifest readiness, не production acceptance и
не приёмка эпохи. Task-00 остаётся blocked; activation/dependencies не меняются.

## Проверенные inputs и фактические sources

Прочитаны [task-00.3](../task-00.3-manifest-contract-scope-hi.md),
[E1 spec](../spec.md), [E1 README](../README.md),
[series README](../../README.md), [execution](../../execution.md),
[benchmark protocol](../../benchmark.md), [task-00](../task-00-input-manifest-hi.md),
[change ledger](../../change-ledger.md) и [H-001/H-002](../../human-decisions.md).
Depends on проверены по actual outputs и acceptance:
[E0/task-07 benchmark](../../epoch-0-snapshot-roundtrip/evidence/task-07-benchmark.md),
[accepted round 2 / validation](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2.md),
[S-002 source](task-11-raw-evidence.md) и
[accepted recheck / validation](task-11-recheck-validation.md).
Дополнительно прочитаны [task-00.1 source](task-00.1-supplemental-source.md),
[validation](task-00.1-validation.md) и [actual controls](task-00.1/controls.json).

C1/C2 проверены непосредственно по текущим SolutionManager,
AnalyzerProvenanceCaptureService и WorkspaceInputMapBuilder. Все три recorded
SHA256 совпали. Caller open/logging path и unknown coverage facts поддерживают
отчёт; они не превращены в гарантию отсутствия hidden target/task consumption.
C# рассматривался только как factual source: C# edits и style review не выполнялись.

L1–L4 сверены с установленными SDK 10.0.300 Microsoft.Common.props,
Microsoft.Common.targets, Microsoft.PackageDependencyResolution.targets и
NuGet.targets по указанным строкам; все четыре SHA256 совпали. Wildcard project
extensions imports, redirected paths, ResolvePackageAssets settings/outputs и
restore settings/lock graph inputs соответствуют отчёту. L5 actual generated
props/targets SamplePlugin прочитаны; оба SHA256 совпали. Они действительно
импортируют package buildTransitive files и остаются ordinary evaluation inputs.

Primary sources независимо открыты online 2026-10-09. Разделение restore/build
evaluations и generated outputs подтверждается
[NuGet MSBuild targets](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets).
Lock opt-in через existing file, custom path и locked-mode behavior подтверждаются
[PackageReference locks](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies).
Project/solution/user/machine/explicit applicability и environment expansion
поддерживаются [NuGet configuration hierarchy](https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior).
Generated conditional imports описаны в
[package props/targets](https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets).
[ResolvePackageAssets](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/ResolvePackageAssets.cs)
и [LockFileCache](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/LockFileCache.cs)
соответствуют ограниченному тезису про assets как NuGet LockFile object.
Отчёт явно различает upstream main и installed binary; installed tasks internals
и exhaustive task closure этим review не аттестуются.

## Обоснование ACCEPT

- C0 и D1/D2 образуют **одно** narrowing proposal: qualified restore-only
  config/lock exclusion из ordinary freshness. Остальные actual ordinary inputs
  остаются mandatory. Current contract не изменён; exclusions для real control
  явно имеют пустое доказанное множество. Возможная польза и риск stale semantic/
  write outcome названы, применение сейчас не рекомендуется.
- R1/R2 различают mutations при прежних assets и последствия нового restore,
  content/create/delete, explicit/default/individual locks, project-vs-solution
  hierarchy и user/machine discovery. Ни stale assets.configFilePaths, ни отсутствие
  raw event не используются как proof обычного non-consumption. Failed restore
  не объявляется successful ordinary route и не создаёт reusable admission.
- R3–R8 сохраняют assets и прочие resolution dependencies, generated imports/
  package build files, project/package declarations, imports/SDK/tasks/resolvers,
  environment, redirected intermediates, generated source ownership, linked/
  external/shared inputs и applicable presence/absence regions. Unknown completeness
  требует whole-request ordinary route. Broad obj pruning и filename-based roles
  не предложены.
- F1–F6 опираются на accepted bounded observations по options, references,
  memberships, root/later final instances и generation. Report не приписывает
  supplemental evaluation equality с BuildHost instances. P-004 остаётся отдельным
  blocker: pre/post equality и existing-file retention не связывают consumed bytes
  и не закрывают namespace/ABA. Missing imports и P-004 не объявлены решёнными
  сужением restore scope.
- All-instance binding/P-011, independently current loader globals/environment,
  H-001/H-002, source comments, supported write/preflight/admission и fallback
  сохранены явно. E0/S-002 accepted results остаются bounded facts; из них не
  выводится production disk hit/performance. Affected tasks/controls перечислены
  как consequences draft, без изменения dependencies или разрешения integration.

## Достаточность docs-only negative audit и пределы проверки

Новые config/lock mutations с fresh ordinary parity **not-run**. Success/failure
restore comparisons и forced generated regeneration под retention также **not-run**.
Отчёт отмечает эти gaps в R1/R2/R7 и в списке будущих controls; measurements из
task-00.1 не переименованы в runs task-00.3. Отсутствие proof прямо запрещает
применение exclusions/admission. Поэтому новые mutation controls не являются
условием принятия **этого отрицательного docs-only результата**. Они нужны для
будущего положительного решения о draft, и review их не заменяет.

Review не утверждает полный аудит произвольных MSBuild task bodies, universal
ordinary semantic equality, sufficient current config discovery, consumable
generation mechanism или работоспособность production fallback adapter.
Это проверка корректности contract reasoning и его traceability на доступном
evidence, а не новая экспериментальная аттестация.

## Checks и передача координатору

Независимо проверены **18 local links** отчёта: missing links **0**.
Все **44 baseline dirty paths** имеют прежний SHA256; исходные изменения не
затронуты. `git diff --check` прошёл (только существующие LF/CRLF warnings).
SHA256 отчёта повторно проверен после чтения sources и совпал.

Build/main/SourceStructure/AnalyzerLifecycle: **not-run / not required** для
данного docs-only scope. Accepted прежние passes не засчитываются за новые runs.
Координатор владеет final consistency/link/dependency/diff checks, status metadata
и task/index/report bookkeeping после этого ACCEPT. До этого planned metadata
в task/index является pending bookkeeping, а не отменой отрицательного verdict.
Рецензент изменил только этот review file; canonical requirements, source report,
C#/tests, task statuses и dependencies не редактировались.
