# E0/task-04 — независимое ревью, round 3

Дата: **2026-10-08**, Europe/Moscow. Рецензент: Codex (GPT-6; точный runtime model ID
недоступен), отдельный subagent, не автор codec или исправлений.
Итог: **ACCEPT** для bounded schema-v1 codec в принятом S-001 профиле.
T04-R1-003 **closed / fixed**; T04-R1-001/002 остаются **closed / fixed**.
Все **3** замечания round 1 подтверждены исправленными. Final acceptance/status
task-04 остаются обязанностью координатора после применимой финальной validation.
Этот вывод не присваивает acceptance hydrate, cross-process round-trip или эпохе.

## Проверенный artifact

HEAD: `8151286` — codec и focused tests уже включены в этот commit.
Рабочего diff в этих двух файлах нет. Прочитаны task-04, spec, README эпохи,
общие README/execution/benchmark, schema evidence, implementation evidence и оба
предыдущих review reports. Code style прочитан **целиком** до style comments;
`.editorconfig` в репозитории не найден.

- [WorkspaceSnapshotCodec.cs](../../../../Services/Workspace/StateCache/WorkspaceSnapshotCodec.cs),
  SHA256: `CA4B6EDADA5EC5DEBC11DEF982256723F96EBA2D953584E6A886845A0F1CA366`.
- [WorkspaceSnapshotCodecTests.cs](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/WorkspaceSnapshotCodecTests.cs),
  SHA256: `D69F5F43B640E5BD474CC243A316F1FA31F941F1210584A7DD3D0DE2E9586170` —
  совпадает с round 2.

Посторонние dirty/untracked WorkspaceLoadCache/tooling files не входят в review
и не принимаются. Production C#, tests, requirements и activation permissions
рецензент не менял. Коммит не выполнялся.

## Recheck и bounded contract

Остаток T04-R1-003 устранён: `MembershipSlot` объявляет properties `Owner`,
`Path`, `DocumentKind`, `Roles`, `Consumed` на строках **1182–1190**, затем
constructor на **1192–1198**. Constructor сохраняет присваивания тем же properties.
`PayloadByteLimit` стоит перед methods (**32**); поле `_characters` завершает
`TextBudget` (**1177**). Обязательный порядок членов соблюдён.

Повторно прочитан действующий validation path: Decode отвергает вход свыше
**16 MiB** до JSON parse; ограничены depth, длины strings, каждый массив и сумма
memberships; duplicate/unknown keys отвергаются. SHA256 точных payload bytes
проверяется до DTO materialization. Encode применяет DTO/text/membership checks
до serialization и bounded stream с резервом envelope overhead, затем проверяет
созданный envelope через Decode. Это validation/serialization без file scan,
cache-directory locking или manager integration.

Parse options реконструируются публичным constructor; specified/effective version
и Errors проверяются. Graph validation сверяет owner/path/kind/role, полное
покрытие memberships и отсутствие лишних occurrences. Metadata producer требует
unique matching output и directed project binding. Linked owners и разные
document kinds на одном path сохранены. Регрессии T04-R1-001/002 присутствуют
на encode и decode с независимо пересчитанным checksum; mandatory focused
malformed/bounds/checksum/schema/round-trip cases покрыты. Новых must-fix
findings в проверенном bounded scope нет.

## Выполненные проверки

Рецензент запустил Roslyn MCP `run_specific_test` с `workspacePath` main test
project, `className=WorkspaceSnapshotCodecTests`, `configuration=Release`,
`noRestore=true`, `timeoutSeconds=240` (build не отключён):
**25 total / 25 passed / 0 failed**. Skip count runner отдельно не печатает;
passed совпадает с total. Это реальный run, не zero-test результат.

`git diff --check` — **passed**; warning о line endings относится к постороннему
dirty WorkspaceLoadCache file. Solution build, main suite и SourceStructure
рецензент в этом раунде **не запускал**; исторические результаты round 2 не
выдаются за новые проверки. Их финальный прогон перед acceptance выполняет
координатор. AnalyzerLifecycle дополнительно не требуется для самого codec;
его необходимость для последующего hydrate оценивается по actual changes.
Hydrate, new-PID semantic round-trip и benchmark здесь **not-run**.
