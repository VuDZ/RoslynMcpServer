# E1/task-00.1 — независимое ревью, round 1

Дата: **2026-10-09**, Europe/Moscow. Рецензент — отдельная Codex subagent
сессия, унаследованная модель текущей сессии; точный runtime model ID/reasoning
не подтверждены инструментами. Рецензент не автор проверяемого scope и C# не
редактировал. Рекомендуемая модель из task не выдаётся за фактическую.

Verdict: **CHANGES REQUIRED** — один открытый обязательный style finding.
Функциональный вывод **partial source / insufficient reusable capture** подтверждён;
после исправления T001-R1-002 и final validation допустима приёмка только bounded
исследования. Production manifest readiness, task-00 adapter, P-004 mechanism,
usable envelope, E1 readiness и public activation **не принимаются**.

## Проверенный actual scope

Прочитаны [task](../task-00.1-supplemental-manifest-source-hi.md),
[execution](../../execution.md), [spec](../spec.md), [индекс](../README.md),
accepted dependency [E0/task-07](../../epoch-0-snapshot-roundtrip/evidence/task-07-benchmark.md)
и [S-002](task-11-recheck-validation.md), [implementation evidence](task-00.1-supplemental-source.md)
и [validation status](task-00.1-validation.md). [Code style](../../../code-style.md)
прочитан целиком до style comments; применимые .editorconfig в repository и
owned code directories отсутствуют.

Actual new code:
[collector](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/SupplementalManifestCollector.cs),
[tests](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/SupplementalManifestTests.cs),
[DTO](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/Models/SupplementalEvaluation.cs).
Existing RawImportEvidenceFixture прочитан как dependency; посторонний dirty
WorkspaceLoadCache не включён в scope и не считается принятой реализацией.

Проверены пять сохранённых JSON: [real](task-00.1/real.json),
[controlled](task-00.1/controlled.json), [controls](task-00.1/controls.json),
[expected inventory](task-00.1/expected.json),
[real identities](task-00.1/real-copy-hashes.json). Все JSON parse успешны.
24 локальные ссылки в task/source/validation существуют. `git diff --check`
не выявил whitespace errors tracked diff; owned новые C# прочитаны непосредственно.

## Findings

**T001-R1-001 — CLOSED: timezone traceability.** Initial source report называл
17:39:44 UTC, тогда как raw real.json содержит `2026-10-09T14:39:44.4480888+00:00`.
Координатор исправил параллельно с обнаружением. Повторное чтение подтвердило
UTC 14:39:44 и отдельное 17:39:44 Europe/Moscow. Raw observation не менялась.

**T001-R1-002 — OPEN / must-fix: expanded argument lists.** Mandatory
[code style](../../../code-style.md) требует: “when the argument list is expanded,
put one argument per line.” Новые Collector.cs lines 28–29 (`new Project`),
39–41 (`new SupplementalEvaluation`) и Tests.cs lines 27–29 (`ProbeAsync`)
содержат несколько аргументов на строке уже раскрытого вызова. В expanded
`JsonSerializer.Serialize(new …, _jsonOptions)` lines 94–106 и multiline
`File.WriteAllText(projectPath, original.Replace(… raw literal …))` та же
проблема с complex arguments. Исправить только owned expanded calls: по одному
аргументу на строку на соответствующем уровне. Длинные неразрывные строки и
короткие single-line calls не требуют дробления. Это обязательная formatting
поправка, не требование менять architecture или переразбивать весь тест.

## Основания функционального bounded вывода

- Candidate `ProjectCollection` использует public API и явные design-time globals.
  Каждый из двух заранее назначенных consumers получает собственный collection-local
  evaluation ID, путь/globals/evaluated Configuration/Platform/TFM. Root и referenced
  imports не подменяются общим physical path. IDs 3/6 не связываются с BuildHost
  final instances; multiple contexts/TFM/SDK/environment прямо остаются unproven.
- Independent controlled inventory задаётся fixture до collector. Nested/shared/
  conditional/external imports проверяются у обоих consumers; redirected assets и
  lock paths — отдельно. Presence lock/assets/config не выдаётся за consumption
  или current applicability. Новый ancestor config отсутствует в stale assets,
  удалённый config остаётся в них. Current NuGet Settings discovery не выполнялось.
- Real raw подтверждает 150/151 imports, ноль candidate-only Compile и по три
  ordinary-only generated sources; ReferencePath пуст при 226/236 ordinary refs.
  Controlled refs — 167/167. Эти mismatches запрещают достаточный manifest verdict.
  Дополнительная evaluation не служит oracle ordinary graph. Ordinary snapshots
  сравнивают выбранные documents/hashes/options/references и project edges;
  symbol assertion и workspace diagnostics не объявлены полным semantic proof.
- 234 non-obj copied identities повторно сравнены с текущими repository файлами:
  **234 matches / 0 mismatches**. Fixture проверяет сохранение этих bytes после
  ordinary/candidate probes. Real csproj/source/refs не изменены ради pass.
- Retention controls фактически проверяют Windows sharing write/delete refusal,
  затем потребление newly appearing conditional import при удерживаемом props,
  новый source/.editorconfig, consumed-B/equal-A pre/post и post-consumption ABA.
  Hash equality прямо не даёт reusable generation proof. Generated writer конфликт
  проверен, forced ordinary regeneration под lock не проверялась и не заявляется.
- Explicit obj/external Compile сохраняются; ordinary custom target потребляет
  custom.txt/define, отсутствующий в candidate inventory. Missing assets и zero
  path budget вызывают actual exceptions. Hardcoded `UsableCapture=false` описан
  как negative experiment outcome; executable admission/fallback отсутствует,
  поэтому эти asserts не объявлены production gate/fallback validation.
- Один measurement real ordinary 857.8477 ms / supplemental 232.4308 ms совпадает
  с raw. Он не включает полный generation protection и не выдаётся за E1 useful
  performance/new-PID hit. Evaluation synchronous/non-interruptible limitation,
  два fixed consumers и неполная budget closure явно описаны.

Исследование даёт новый root+referenced public source inventory и проверяемые
counterexamples retention/hash-only гипотезе. Не поддержанный real capture
positive, unbound all-final instances, current config/lock applicability,
target-produced options/references/memberships и P-004 generation — явные
оставшиеся blockers task-00. Расширение до arbitrary task model/fork BuildHost
не требуется для приёмки этого честно ограниченного negative результата.

## Validation и ownership

Независимый reviewer не повторял expensive build/main suite: финальная validation
принадлежит координатору и на момент round 1 pending. Source status содержит
focused Release solution build и SupplementalManifestTests 1/1; эти авторские
наблюдения не подменяются независимым execution claim reviewer.

После C# style fix необходимы final Release solution build и полный main
`Category!=AnalyzerLifecycle` против финального кода; zero tests/skips/failures
нельзя объявить pass. Production C# и lifecycle configuration не менялись;
отдельный AnalyzerLifecycle не требуется. До final response координатор обязан
синхронизировать task/index/report date, bounded accepted scope, review rounds,
fixed findings и validation links. Текущий planned/pending статус не принимается
как финальный bookkeeping; его обновление остаётся частью завершения задачи.
