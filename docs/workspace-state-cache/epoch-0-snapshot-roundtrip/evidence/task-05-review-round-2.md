# E0/task-05 — независимое ревью, round 2

Дата: **2026-10-08**, Europe/Moscow. Рецензент: Codex (GPT-6; точный runtime model ID
недоступен), новый независимый subagent, не исполнитель и не рецензент round 1.
Итог: **ACCEPT** для isolated hydrate adapter/session owner в принятом S-001
base-query профиле H-001/H-002. **T05-R1-001 closed / fixed**; новых must-fix
findings: **0**. Подтверждено одно исправление после round 1.
Final task acceptance, обязательная validation, статусы и commit принадлежат координатору.

## Проверенный scope

База: `c78764d`. Прочитаны task-05, spec и README эпохи, общие
README/execution/benchmark, accepted task-03/04/08 и их implementation/review evidence,
H-001/H-002, task-05-hydrate.md и task-05-review-round-1.md.
`docs/code-style.md` прочитан **целиком**; `.editorconfig` в репозитории не найден.
Стиль проверялся только у новых/изменённых строк.

Actual artifact:

- `Services/Workspace/StateCache/IsolatedWorkspaceHydrator.cs`;
- `Services/Workspace/StateCache/IsolatedHydratedSession.cs`;
- `Services/Workspace/StateCache/Models/HydratedInputOccurrence.cs`;
- изменения `SnapshotOptionFacts.cs` и `WorkspaceSnapshotCodec.cs`;
- `RoslynMcpServer.Tests/WorkspaceStateCache/IsolatedWorkspaceHydratorTests.cs`;
- implementation evidence task-05.

Посторонние dirty/untracked WorkspaceLoadCache/tooling files не входят в verdict
и не принимаются. Рецензент не менял C#, requirements, task/index statuses и не
выполнял commit. Единственная правка рецензента — этот отчёт.

## T05-R1-001 — closed / fixed

`WorkspaceSnapshotCodec.cs:32` предоставляет internal `ProjectKeyIdentity`, который
возвращает **существующий** `ProjectKeyComparer.Instance`. Schema validation не
ужесточена и не переписана. Comparer по-прежнему использует OS-aware
`InputPathCanon.Comparer` только для ProjectPath; Configuration/Platform/
TargetFramework сравниваются точно. Hash code согласован с той же identity.

Hydrate создаёт все свежие IDs с этим comparer (`IsolatedWorkspaceHydrator.cs:43–44`).
Эта map используется для target (`:171`), owner (`:265`) и producer (`:281`),
поэтому codec-admitted casing variant больше не попадает в default record lookup.
Session копирует map в **immutable** dictionary с тем же comparer
(`IsolatedHydratedSession.cs:37`): исправление сохраняется после возврата owner,
а не теряется при defensive copy.

Regression theory (`IsolatedWorkspaceHydratorTests.cs:454–526`) содержит шесть
cases: target, owner и producer независимо, каждый в двух project orders.
Перед hydrate каждый case проходит actual codec Encode/Decode (`:505`). Далее
проверяются edge binding, generated owner/producer/document ProjectId, metadata
producer, linked owners и lookup returned Projects по alternate casing.
Дополнительно точные instance tuple controls отвергают `DEBUG`, `anycpu`,
`NET10.0` (`:511–513`). Fresh IDs отдельно проверяет existing two-session control.
Это meaningful regression ранее допустимого payload, а не изменение admission
ради отказа. Для case-sensitive ОС сохранены codec refusal и различие returned
map lookup; эта ветка локально на Windows **not-run**, отдельный OS pass не заявлен.

Рецензент подтвердил исправление по actual codec/comparer/consumer/test code и
получил от координатора Release focused результат **27/27**. Собственный повтор
build/tests не запускался, чтобы не конкурировать с активным main runner.

## Повторная проверка остального contract

Codec round-trip перед первым await валидирует schema и отделяет DTO arrays от
вызывающего кода; он не выдаётся за fresh cache admission. Project IDs создаются
до graph construction, Document IDs — заново для memberships. Directed edges,
aliases/EmbedInteropTypes, все document kinds, roles/role sources и confirmed
producer sources сохраняются. Unknown остаётся Unknown, path/name не используются
для guessing роли. Returned InputMap сохраняет все occurrences; presence обновляется.

Current document bytes читаются через OS-aware path map один раз на physical path,
включая linked source/additional memberships. Source text берётся с диска, encoding
и checksum policy восстановлены публично; current checksum вычисляется заново.
Null encoding не получает неявную persistence policy. Current text mutation и
source XML comments покрыты focused controls.

Metadata получают свежие owned images и полные reference properties. Образ заносится
в ownership list до callback/validation, публичный reader принудительно проверяет
его до возврата session. Failure на втором invalid image и cancellation после
первого/второго image освобождают уже выделенные images. Assembly с внешними
metadata modules, script source и cyclic graph явно отказаны; positive поддержку
этих случаев отчёт не заявляет. H-001 external XML исключение объявлено отдельно
через Default provider, references не удаляются.

Parse/options reconstruction использует принятый public witness, fresh host
metadata resolver и fresh project analyzer-config provider; scalar Equals oracle
объявляет identity normalizations. H-002 empty-search-path policy ограничена
signing-independent base-query slice. Current effective compilation options,
assembly attributes и dependency compilations проверяются до return; signing-
dependent и unresolved-attribute controls дают contextual reason/route=refusal.
Hidden-state fidelity, emit/signing и production ordinary fallback/log integration
не заявляются.

Base compilation строится вручную через CSharpCompilation.Create по dependency
order. Hydrated GetCompilationAsync не вызывается; analyzer identity переносится
с throwing loader. DTO не разрешает analyzer/generator assembly execution.
Candidate Solution не публикуется; owner Workspace.CurrentSolution остаётся пустым.
Нового production TryApplyChanges reference site нет.

Оба ownership paths вызывают Workspace.Dispose с disposal всех Metadata в finally.
Session disposal идемпотентен; immutable maps не раскрывают mutable dictionaries.
Behavioral image-disposal checks дополнены semantic SourceSetAnalysis witness
реальных Dispose calls, а notification callback не выдаётся за самостоятельный oracle.

Structural owned-path control проверяет transitive calls без MSBuild OpenProject/
OpenSolution, MSBuildProjectLoader LoadProjectInfoAsync, Process.Start и
Workspace.TryApplyChanges. Exact limit inventory не расширен: ровно три identities
`_readBytes` ← `readBytes`, `_metadataCreated` ← `metadataCreated`,
`_workspaceDisposed` ← `workspaceDisposed`; каждый **TargetOutsideTheScope**.
Утверждаются полные semantic site identity strings и виды limits, не только count.
Произвольные supplied callbacks и существующие interface/virtual dispatch limits
остаются явно указанной границей structural proof.

## Validation и граница verdict

Полученные от координатора результаты против замороженного исправленного C#:

- Release solution build — **PASS**;
- IsolatedWorkspaceHydratorTests — **27/27 passed**;
- WorkspaceSnapshotCodecTests — **25/25 passed**;
- SourceStructure — **123/123 passed**;
- complete main suite `Category!=AnalyzerLifecycle` — **1350/1350 passed**,
  0 failed; passed=total, skips/timeouts не наблюдались;
- diff check tracked/new scope — **PASS**, только CRLF conversion warnings.

Это coordinator validation, не независимый run рецензента. Рецензент выполнил
read-only проверку diff и кода; solution/test runners не запускал. AnalyzerLifecycle
здесь **not-run**; применимость окончательно фиксирует координатор. Inert isolated
scope не меняет production analyzer/shadow-copy/load/prepare/publication, lifecycle
host или его configuration.

Verdict **ACCEPT** закрывает independent review task-05; task acceptance и отдельный
final validation report оформляет координатор после полученного green main. Cross-process
round-trip, persistent envelope, disk .cs edit, benchmark/speedup, E1 runtime
admission/publication и public activation этим verdict не принимаются.
