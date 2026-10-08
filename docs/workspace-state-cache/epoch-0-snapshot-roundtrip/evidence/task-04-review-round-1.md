# E0/task-04 — независимое ревью, round 1

Дата: **2026-10-08**, Europe/Moscow. Рецензент: Codex (GPT-6; точный runtime model ID
недоступен), отдельная сессия, не автор проверяемого scope.
Итог: **REQUEST CHANGES**. Два функциональных must-fix findings (**P2**) и одно
нарушение обязательного code style (**P3**). Исправления в этом раунде не выполнялись.
Accepted scope task-04 **не присвоен**; задача остаётся в `review`, требуется исправление
и повторное независимое ревью. Приёмка hydrate, cross-process round-trip и эпохи не выполнена.

## Проверенный scope

HEAD: `9daf0b56c9de10dd4c7e033c4b4dbd083d390527`; codec и его tests — новые
untracked файлы поверх HEAD. Проверены actual source и tests:

- [WorkspaceSnapshotCodec.cs](../../../../Services/Workspace/StateCache/WorkspaceSnapshotCodec.cs)
- [WorkspaceSnapshotCodecTests.cs](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/WorkspaceSnapshotCodecTests.cs)
- [implementation evidence](task-04-codec.md)

Прочитаны [task-04](../task-04-snapshot-codec-low.md), [spec](../spec.md),
[карта выполнения](../README.md), [общий README](../../README.md),
[execution](../../execution.md), [benchmark](../../benchmark.md),
[schema v1](task-02-schema.md), [S-001](task-08-capability.md) и code style целиком.
Source contracts сопоставлены с `SnapshotOptionFacts` и `SnapshotOccurrenceBinder`.
Prerequisites task-02/S-001 приняты. Новый architecture decision или расширение
профиля не требуется: findings относятся к уже зафиксированным DTO contracts.

Посторонние dirty/untracked изменения WorkspaceLoadCache и tooling не ревьюились
и не принимались; они присутствуют в рабочем дереве, по которому запускалась CI suite.
Production C# и штатные tests рецензент не менял. Коммит не выполнялся.

## T04-R1-001 — P2: проверка enum names не обеспечивает согласованность parse options

Место: `WorkspaceSnapshotCodec.cs:466–478`, `ValidateParse`.

`SpecifiedLanguageVersion` и `LanguageVersion` проходят отдельные `Enum.IsDefined`,
а `PreprocessorSymbols` проверяется как произвольный string array. Проверки
public-constructor reconstruction и effective-version equality, уже используемые
в принятом `SnapshotOptionFacts.Parse`, здесь отсутствуют.

Воспроизведение на штатном `PortableSnapshot`:

1. Оставить `SpecifiedLanguageVersion="CSharp10"`, заменить `LanguageVersion` на
   `"CSharp1"`. `Encode` и `Decode` принимают DTO. Public `CSharpParseOptions`
   из specified version получает effective **CSharp10**, тогда как payload утверждает **CSharp1**.
2. Заменить `PreprocessorSymbols` на `["NOT A SYMBOL"]`. Обе стороны codec принимают
   DTO; восстановленные Roslyn options содержат **CS8301**, invalid preprocessing symbol.

Во всех decode-пробах checksum пересчитан по точному payload. Отказ не подменяется
checksum failure. Такой DTO проходит границу codec с противоречивыми/некорректными
options; hydrate должен либо потерять заявленное поле, либо отказать уже после decode.
Это не выполняет task-04 check «invalid options дают отказ до hydrate».

Требуется проверить представимость parse options публичным constructor, согласованность
specified/effective version и допустимость symbols; добавить negative encode/decode
tests для этих случаев. Нормализация `Default`/`Latest` должна учитываться через Roslyn,
а не через простое равенство двух исходных строк. Если допустимость options с diagnostics
ограничивается иначе, граница должна быть явной; противоречие effective version
в любом случае должно отвергаться.

## T04-R1-002 — P2: portable input map не сверяется с project graph

Место: `WorkspaceSnapshotCodec.cs:687–703`, `ValidateInputPath`, и `:751–762`,
`MetadataDependency` в `ValidateOccurrence`.

В occurrence validator не передаётся physical path и не проверяется соответствующая
document/reference/output membership owner. Для metadata producer достаточно наличия
key в `Projects` и строки `unique-output-and-directed-project-binding`;
matching output, единственность producer и directed edge не проверяются.

Подтверждённые пробы на штатном `PortableSnapshot`, все приняты обеими сторонами codec:

- Metadata occurrence для `lib/System.Runtime.dll` получает producer `App`, хотя
  `App` имеет output `bin/App.dll`, а directed project references пусты.
- `InputPaths` заменяется на `[]` при сохранённых documents, metadata/analyzer
  references и output. DTO теряет все portable owners/memberships.
- Source document получает `IsGenerated=true`, а его occurrence остаётся `UserInput`
  без producer. Два представления роли противоречат друг другу.

Принятая [schema v1](task-02-schema.md) требует всех owners/memberships,
согласованности generated flag и proven producer через unique output + directed edge.
`SnapshotOccurrenceBinder` обеспечивает эти свойства при capture, но checksum
подтверждает bytes, а не происхождение DTO из этого binder. Encode/decode принимают
и прямой DTO, и корректно checksummed malformed payload.
В дальнейшем восстановленная единственная input map может пропустить dependency
либо принять недоказанный producer и ошибочно классифицировать источник.

Требуется проверить DTO graph consistency без file scan: соответствие path/kind/owner
и полноту требуемых occurrences, согласованность generated facts, matching output
и направленное project binding для producer. Добавить negative encode/decode tests,
сохранив linked owners и несколько разных document kinds на одном path.
Fresh file presence/provenance admission остаётся отдельным scope; это finding
не требует environment validation или нового dependency closure research.

## T04-R1-003 — P3: новый codec нарушает обязательный порядок членов

Место: `WorkspaceSnapshotCodec.cs:956` и `:1057`.

В `TextBudget` поле `_characters` стоит перед methods; private property
`PayloadByteLimit` находится в конце внешнего типа после methods/nested types,
среди private fields. [Code style](../../../code-style.md#class-member-order)
требует properties перед methods, а private fields — в конце соответствующего type.
Файл новый, поэтому исключение для неизменяемого старого кода здесь неприменимо.

Требуется переместить property и поле по правилам, сохранив порядок инициализации
static fields. Constants переставлять ради этой проверки не требуется.

## Проверки рецензента

Против actual Release code выполнены Roslyn MCP runners:

- `run_specific_test`, `className=WorkspaceSnapshotCodecTests`, `noRestore=true`:
  **16/16 passed**, 0 failed.
- `run_dotnet_build`, solution, Release, default `noIncremental=true`: **passed**.
- `run_test_by_filter`, `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`,
  Release, `noBuild=true`, `noRestore=true`: **123/123 passed**, 0 failed.
- `run_test_by_filter`, `Category!=AnalyzerLifecycle`, Release, `noBuild=true`,
  `noRestore=true`, timeout 300 seconds: **1314/1314 passed**, 0 failed.

Число passed совпадает с total во всех трёх test runs; skips не наблюдались,
zero-test runs отсутствуют. Structural limit inventory не менялся.
AnalyzerLifecycle **не запускался и дополнительно не требуется** для инертного
serialization/validation helper без изменений analyzer/shadow-copy/load/prepare/
publication, lifecycle host или test/build configuration.
Hydrate, new-PID semantic round-trip и benchmark — **not-run**, scope следующих задач.

`git diff --check` — **passed**. Общий `validate-artifacts.py` — **failed**, exit 1;
[raw результат](task-04-docs-validation.json) сохранён отдельно. Скрипт фиксирует
accepted metadata только для task-00/task-08 и требует от task-01–04 исторические
`planned / 2026-10-07` и пустые actual fields; кроме того, directory-ссылка
`task-01/` ошибочно проверяется через `is_file()`. Это ограничение существующего
docs validator, его успешный результат не заявляется. Сгенерированный им общий
исторический `validation.json` восстановлен; script и прежний отчёт не менялись.
Локальные ссылки и статусы файлов текущего ревью проверены отдельно.

## Дополнительный executable control

[Исходник пробы](task-04-review-probe.cs.txt) и [raw output](task-04-review-probe.txt)
сохранены. Расширение `.cs.txt` исключает probe из production compilation.
Harness берёт private `PortableSnapshot` из существующей test assembly, вызывает
actual production `Encode`/`Decode` через delegates и независимо формирует JSON
envelope с SHA256 точных payload bytes. Source/test assembly не переписывается.

Первые две попытки `dotnet run/build` временного project reference harness завершились
exit code 1 до получения результатов; build сообщил 0 warnings / 0 errors без причины.
Они **не считаются pass**. Успешная проба собрана локальным SDK compiler `csc.dll`
10.0.300 с reference pack 10.0.8 и actual Release assemblies. Выполнена через:

```powershell
dotnet exec --runtimeconfig RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.runtimeconfig.json --depsfile RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.deps.json RoslynMcpServer.Tests/bin/Release/net10.0/CodecReviewProbe.dll
```

Для повторной сборки сохранить probe source во временный `Program.cs`; `csc.dll`
вызывается с `-target:exe -nullable:enable -langversion:preview`, references ко всем
DLL из `.NETCore.App.Ref/10.0.8/ref/net10.0` и к `RoslynMcpServer.dll`,
`RoslynMcpServer.Tests.dll`, `Microsoft.CodeAnalysis.dll`, `Microsoft.CodeAnalysis.CSharp.dll`
из указанного Release bin; output — `CodecReviewProbe.dll` в том же bin.

Дополнительные probes invalid preamble и null required name оставлены в raw output
как исследовательские наблюдения, отдельных findings из них не выведено:
для preamble нужна отдельно установленная representability граница encoding,
null name уже получает отказ, хотя encode использует `NullReferenceException`.

Штатные 16 green tests и real-control byte round-trip подтверждают реализованные
positive/checksum/bounds проверки, но не закрывают пять подтверждённых negative
сценариев T04-R1-001/002. После исправлений нужны affected tests, final Release
build/main/SourceStructure и повторное независимое ревью; acceptance сейчас отсутствует.
