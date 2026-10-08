# E0/task-04 — независимое ревью, round 2

Дата: **2026-10-08**, Europe/Moscow. Рецензент: Codex (GPT-6; точный runtime model ID
недоступен), не автор проверяемой реализации и исправлений.
Итог: **REQUEST CHANGES**, осталось одно **P3** по обязательному code style.
Функциональные T04-R1-001 и T04-R1-002 **closed / fixed**.
T04-R1-003 **partially fixed / open**: прежние два места исправлены, новый nested
type нарушает тот же обязательный порядок членов.
Подтверждённых исправлений после round 1: **2 из 3**. Accepted scope task-04
**не присвоен**; hydrate, cross-process round-trip и приёмка эпохи не выполнялись.

## Проверенный artifact

HEAD: `9daf0b56c9de10dd4c7e033c4b4dbd083d390527`; codec и tests ещё не закоммичены.
Проверены actual [codec](../../../../Services/Workspace/StateCache/WorkspaceSnapshotCodec.cs),
[tests](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/WorkspaceSnapshotCodecTests.cs),
[implementation/fix evidence](task-04-codec.md) и [round 1](task-04-review-round-1.md).
Code style прочитан целиком перед проверкой стиля. Requirements/schema/S-001
и activation permissions не менялись. Посторонние WorkspaceLoadCache/tooling
изменения не ревьюились и не принимались; CI запускалась по текущему рабочему дереву.
Production source и штатные tests рецензент не менял. Коммит не выполнялся.

SHA256 проверенного source:

- `WorkspaceSnapshotCodec.cs`: `1B1D7EE728B542E3280D4DDA4D6ABE89C58F38BB5F614E6A0A63D44FB5D8DBF7`.
- `WorkspaceSnapshotCodecTests.cs`: `D69F5F43B640E5BD474CC243A316F1FA31F941F1210584A7DD3D0DE2E9586170`.

## Recheck T04-R1-001 — closed / fixed

`ValidateParse` выполняет public-constructor reconstruction + `WithFeatures`,
проверяет effective `LanguageVersion` и отсутствие `Errors`. Новые tests проверяют
contradictory language version, invalid symbol и обе стороны codec с пересчитанным
checksum. Positive/negative `Default` controls проверяют нормализацию через Roslyn.
Прежние independent probes для CSharp10/CSharp1 и `NOT A SYMBOL` теперь получают
`InvalidDataException` на encode и decode.

## Recheck T04-R1-002 — closed / fixed

`ValidateGraph` сопоставляет occurrences с DTO memberships по owner/path/kind/role,
проверяет полное покрытие и отвергает лишние occurrences. `ValidateMetadataProducer`
сверяет matching output, единственность owner и directed project reference.
Три прежние independent probes — недоказанный producer, пустой InputPaths и
generated flag с ролью UserInput — теперь получают отказ на обеих сторонах codec.
Positive tests для linked owners + двух document kinds на одном path и proven
metadata producer проходят; actual ordinary-capture control также проходит.
File scan/fresh admission не добавлены; bounded byte/membership checks сохранены.

## Recheck T04-R1-003 — partially fixed / open, P3

`PayloadByteLimit` перемещён перед methods; `_characters` завершает `TextBudget`.
Эти два исходных места исправлены. Однако новый `MembershipSlot` в
`WorkspaceSnapshotCodec.cs:1180–1198` объявляет constructor **перед** properties:
constructor начинается на **1182**, первая property `Owner` — на **1190**.

[Обязательный порядок](../../../code-style.md#class-member-order): properties,
затем constructors, затем methods, nested types и private fields. Он применяется
и к новым private nested types. Требуется переместить группу properties
`Owner/Path/DocumentKind/Roles/Consumed` перед constructor. Runtime behavior
менять не требуется. Других must-fix findings в этом раунде не добавлено.

## Validation рецензента

Roslyn MCP, Release, actual final source:

- `run_dotnet_build`, solution, default `noIncremental=true`: **passed**.
- `run_specific_test`, `WorkspaceSnapshotCodecTests`, `noBuild=true`, `noRestore=true`:
  **25/25 passed**, 0 failed.
- `run_test_by_filter`, `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`,
  `noBuild=true`, `noRestore=true`: **123/123 passed**, 0 failed.
- `run_test_by_filter`, `Category!=AnalyzerLifecycle`, `noBuild=true`, `noRestore=true`,
  timeout 300 seconds: **1323/1323 passed**, 0 failed.

Passed совпадает с total; skips и zero-test runs не наблюдались. Structural limit
inventory не менялся. AnalyzerLifecycle **не запускался и дополнительно не требуется**:
изменения ограничены инертным codec и его focused tests, без analyzer/shadow-copy/
load/prepare/publication, lifecycle host или test/build configuration.
Hydrate, new-PID semantic round-trip и benchmark — **not-run**, отдельный scope.

Independent executable probe round 1 повторно запущен против новых Release assemblies;
[исходник](task-04-review-probe.cs.txt), [raw результат round 2](task-04-review-probe-round-2.txt).
Все пять функциональных reproduction cases отказаны на encode/decode.
Exploratory preamble/null-name cases сохраняются как наблюдения без новых findings.

`git diff --check` и локальные ссылки текущего review/status packet проверены отдельно.
Общий docs validator не запускался повторно: его зафиксированное в
[round 1](task-04-review-round-1.md) ограничение актуальности статусов не исправлялось;
успешная общая docs validation не заявляется.

Зелёная CI подтверждает функциональные исправления; mandatory style requirement
ещё не выполнен. После перестановки members требуется recheck и применимая final
validation перед acceptance согласно AGENTS.md.
