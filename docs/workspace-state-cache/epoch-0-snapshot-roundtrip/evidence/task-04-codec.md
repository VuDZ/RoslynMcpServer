# E0/task-04 — Bounded snapshot codec

Дата: **2026-10-08**, Europe/Moscow. Исполнитель: Grok 4.7.
Статус реализации: **accepted, round 3, 2026-10-08**, bounded schema-v1 codec в принятом S-001 профиле.
T04-R1-001/002 закрыты round 2; T04-R1-003 закрыт [round 3](task-04-review-round-3.md).
[Final validation / acceptance](task-04-validation.md); подтверждённых исправлений после ревью: **3**.
Приёмка эпохи не присвоена.
Version / AssemblyVersion / FileVersion не менялись (остаётся **1.5.5**).

Принятые входы: [schema v1](task-02-schema.md), [S-001](task-08-capability.md),
[P-001](../../change-ledger.md#p-001), [P-008](../../change-ledger.md#p-008).
Cache directory, locking, file scan, hydrate и `SolutionManager` не менялись.

## Artifact

Единственный executable codec: `Services/Workspace/StateCache/WorkspaceSnapshotCodec`.
Он кодирует и читает schema-v1 `WorkspaceSnapshot`. Production не зависит от Tests assembly.
E1 использует этот тип; перенос или замена требуют повторной проверки bounds, malformed payload
и round-trip против фактически поставляемого кода.

Envelope — компактный UTF-8 JSON:

- `Schema` = `workspace-snapshot-envelope-v1`
- `ChecksumAlgorithm` = `SHA256`
- `Checksum` — hex SHA-256 точных UTF-8 байт `Payload`
- `Payload` — DTO schema **1**, профиль `s001-single-target-debug-base-query-h001-h002-v1`

Checksum сравнивается до `Deserialize`. Повреждённый envelope не становится snapshot,
который можно было бы передать в hydrate.

Закрытые ключи, enum options, canonical paths, роли вхождений и длины document checksum
(`Sha1` = 20 байт, `Sha256` = 32 байта) проверяются до возврата snapshot.
Предел envelope — **16 MiB**. Сумма длин массивов — **1_000_000**. Строка — **32_768**.
На encode сумма символов отвергается до записи JSON, если она уже больше предела байт.

Отказ — `InvalidDataException`, snapshot не возвращается:

- неизвестные envelope schema, schema version и profile
- обрезанный JSON
- повторяющийся ключ объекта, включая вложенный
- лишний ключ payload
- неверное значение option
- document checksum не той длины или неизвестного алгоритма
- checksum envelope не сходится с payload; эта ошибка сообщается раньше проверки options
- payload длиннее 16 MiB отвергается до parse
- по отдельности допустимые массивы и строки отвергаются, когда их сумма выходит за aggregate bound

Round-trip синтетического DTO и ordinary capture контроля `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`
даёт те же байты при повторном encode. Текст исходников, Roslyn ID и shadow path в envelope не пишутся.

HEAD на момент правок: `9daf0b56c9de10dd4c7e033c4b4dbd083d390527`. Новые файлы поверх этого commit не закоммичены.

## Исправления после round 1

T04-R1-001. Parse options собираются публичным конструктором `CSharpParseOptions` и `WithFeatures`.
`LanguageVersion` в DTO должен совпасть с эффективной версией этого конструктора: `Default` и `Latest`
принимаются, когда payload называет нормализованную версию, и отвергаются при другом имени.
Непустой `Errors` (невалидный preprocessor symbol) отвергает payload. Сравнение двух строк DTO между собой не используется.

T04-R1-002. Каждое вхождение input map должно закрыть ровно одну membership проекта: evaluation на пути проекта,
документ с ролью своего kind (`IsGenerated` у source допускает только `Generated`), один output на каждый различный output path,
одна metadata- и одна analyzer-зависимость на путь владельца. Лишнее вхождение и пустой `InputPaths` при непустом графе отвергаются.
Producer metadata принимается только если единственный проект имеет этот output path и consumer ссылается на него.
Два владельца одного пути и два kind на одном пути остаются допустимыми. Диск не читается.

T04-R1-003. `PayloadByteLimit` стоит перед методами. Поле `_characters` стоит в конце `TextBudget`.
Порядок инициализации статических полей envelope не менялся.
Properties `MembershipSlot` стоят перед constructor. Перестановка подтверждена [round 3](task-04-review-round-3.md);
[round 2](task-04-review-round-2.md) фиксирует прежний порядок.

## Validation

До исправлений ревью зафиксировало: codec **16/16**, SourceStructure **123/123**, main suite **1314/1314**.
Первый прогон codec был **15/16**: метод real capture ссылался на MSBuild до `MsBuildBootstrapper.Register`.
Регистрация перенесена в конструктор фикстуры.

После исправлений, 2026-10-08, Release:

- solution build **passed** (`RoslynMcpServer.sln`, `noIncremental=true`);
- `WorkspaceSnapshotCodecTests`: **25/25 passed** (`run_specific_test`, `noBuild`, `noRestore`);
- SourceStructure: **123/123 passed**;
- main suite `Category!=AnalyzerLifecycle`: **1323/1323 passed**, 0 failed / 0 skipped, `--no-build` после этой компиляции.

Инвентарь structural limits не ослаблялся.
После перестановки members `MembershipSlot` тот же Release solution build и main suite повторены: **1323/1323 passed**.

AnalyzerLifecycle дополнительно **не запускался**: правка не касается analyzer/shadow-copy,
load/prepare/publication, lifecycle host и test/build configuration. Hydrate и benchmark не запускались.

Final validation координатора при acceptance: codec **25/25**, SourceStructure **123/123**,
main **1323/1323** после initial existing editorconfig failure **1322/1323** и class repeat **4/4**;
[все результаты и ограничения](task-04-validation.md). Codec/tests закоммичены в HEAD 8151286.
