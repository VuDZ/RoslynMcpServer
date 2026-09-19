<a id="e2-01"></a>

ID: E2-01
Severity: Blocker
Category: Contract

Target:
[stage-2-symbol-identity.md](../proposal-v1/stage-2-symbol-identity.md), «Идентичность»,
строки 30–36; также `review-mapping.md`, R-§6.

Claim:
Не создавать собственный handle; использовать строку
`Microsoft.CodeAnalysis.SymbolKey`, создавать её из `ISymbol` и
разрешать через `SymbolKey.Resolve`.

Evidence:
[RoslynMcpServer.csproj](../../../../RoslynMcpServer.csproj), строки 76–80,
фиксирует Roslyn 5.9.0. Проверка именно установленной
`Microsoft.CodeAnalysis.Workspaces, Version=5.9.0.0` через отдельный
AssemblyLoadContext и независимый .NET probe дала
`SymbolKey.IsPublic=False`, `SymbolKey.IsVisible=False`.
Объявление также подтверждается [исходником Roslyn](https://source.dot.net/Microsoft.CodeAnalysis.CodeStyle/src/roslyn/src/Workspaces/SharedUtilitiesAndExtensions/Compiler/Core/SymbolKey/SymbolKey.cs.html):
`internal partial struct SymbolKey`. Public-методы внутри internal-типа
не делают его доступным для обычного C#-вызова из сервера.
Спецификация не выбирает adapter к internal API, vendoring или другую
реализуемую стратегию; одновременно запрещает собственный handle.

Failure scenario:
1. Исполнитель вводит DI-сервис и реализует прямой вызов предписанного
   `SymbolKey.Create`/`Resolve` с текущими PackageReference.
2. C#-код не может обращаться к internal-типу из сборки сервера.
3. Для реализации центрального результата стадии приходится менять
   архитектурное решение: вводить зависимость от internal API либо
   нарушать запрет собственного handle. Как обычный доступный API
   предписанный путь не реализуется.

Suggested change:
До реализации выбрать доступный механизм идентичности и проверить
compile-time spike на Roslyn 5.9.0. Если выбран internal adapter, явно
принять его как отдельную зависимость с контролем версии/формата и
ошибок совместимости. Если выбран собственный opaque handle на публичных
API, снять запрет. Ни одно из этих решений само по себе не закрывает
E2-03/E2-04.

Confidence:
High

---

<a id="e2-02"></a>

ID: E2-02
Severity: High
Category: Contract

Target:
[stage-2-symbol-identity.md](../proposal-v1/stage-2-symbol-identity.md), «Цель»,
строки 18–26; «Optional params», строки 74–79; ограничение строки 4.

Claim:
Добавление optional `symbolId` позволяет вызывать `find_usages(symbolId)`,
`rename_symbol(symbolId, newName)` и `get_call_graph(symbolId)` вместо
повторной передачи старых идентификаторов, сохраняя совместимость.

Evidence:
Текущие адаптеры имеют обязательные параметры без default:

- [NavigationTools.cs](../../../../Tools/NavigationTools.cs), `FindUsages`,
  строки 268–277: `symbolName` обязателен, пустое имя отклоняется.
- Там же `FindSymbolReferences`, строки 37–54: обязательны `filePath`
  и `symbolName`; `GetCallGraph`, строки 749–753: `filePath`, `className`,
  `methodName`.
- [UtilityTools.cs](../../../../Tools/UtilityTools.cs), `GetMethodBody`,
  строки 251–271: `filePath`, `className`, `methodName`;
  `RenameSymbol`, строки 927–944: `filePath`, `symbolName`, `newName`
  с явной проверкой всех трёх.
- [McpToolCatalogTests.cs](../../../../RoslynMcpServer.Tests/McpToolCatalogTests.cs),
  `Public_tool_schemas_preserve_required_default_and_type`, строки 332–370,
  проверяет присутствие обязательных аргументов в MCP JSON Schema.

Добавление ещё одного optional аргумента не удаляет старые из `required`
и не меняет раннюю валидацию. План перечисляет только добавления и не
определяет альтернативные наборы обязательных полей.

Failure scenario:
1. Исполнитель сохраняет прежние обязательные аргументы и добавляет
   `symbolId = null`, как описано в разделе compatible params.
2. Агент получает ID и вызывает `rename_symbol` только с `symbolId`
   и `newName` по примеру самой стадии.
3. Запрос не соответствует опубликованной схеме/валидации и не достигает
   resolver. Передача фиктивных старых аргументов формально обходит
   обязательность, но возвращает неоднозначное адресование вместо
   обещанного ID-only API.

Suggested change:
Явно разрешить ослабление required для прежних аргументов адресования,
сохранив все ранее валидные вызовы. Для каждого tool определить режимы:
ID, location, legacy name; обязательные поля каждого режима и реакцию на
конфликт одновременно переданных селекторов. `newName` остаётся
обязательным для rename. Добавить протокольные schema/call tests для
вызовов только с ID, только с location и для старых вызовов.

Confidence:
High

---

<a id="e2-03"></a>

ID: E2-03
Severity: High
Category: Cache identity

Target:
[stage-2-symbol-identity.md](../proposal-v1/stage-2-symbol-identity.md), «Идентичность»,
строки 30–36; «Новые tools», строка 51; «Тесты», строка 88.

Claim:
Строки `SymbolKey` достаточно, чтобы адресовать конкретный символ в
published solution, в том числе при одинаковых именах в двух проектах.

Evidence:
У реальной Roslyn 5.9.0 сигнатура разрешения —
`Resolve(Compilation, bool, CancellationToken)` / `ResolveString(string,
Compilation, bool, CancellationToken)`. Ключ разрешается в выбранной
компиляции, а не в `Solution`; ProjectId в строку не добавляется.
Контракт assembly identity в XML-документации установленного пакета
`microsoft.codeanalysis.workspaces.common/5.9.0`, раздел `T:Microsoft.CodeAnalysis.SymbolKey`,
сравнивает имя assembly, а не принадлежность проекту.

Независимый runtime probe создал два валидных метода `N.C.M` в разных
source trees и компиляциях с одинаковой assembly identity. Получены
одинаковые ключи; ключ из A успешно разрешился в B даже при
`ignoreAssemblyKey=false`. Это допустимая модель двух независимых проектов
solution с одинаковым `AssemblyName`.

Результат probe, также используемого для E2-01/E2-04:

```text
Assembly=Microsoft.CodeAnalysis.Workspaces, Version=5.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35; SymbolKey.IsVisible=False
Different projects, same assembly identity: equalKeys=True; AkeyInB=B.cs
Old local key after insertion: void L() { int newlyInserted = 1; }
Errors: 0/0/0/0
```

Воспроизведение: временный executable project `net10.0` с прямыми
references на `Microsoft.CodeAnalysis.dll`, `Microsoft.CodeAnalysis.CSharp.dll`
и `Microsoft.CodeAnalysis.Workspaces.dll` из пакетов версии **5.9.0**.
Probe не загружает и не меняет workspace сервера; reflection намеренно
обходит internal-доступность только для проверки семантики ключа.

```csharp
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var asm = Assembly.Load("Microsoft.CodeAnalysis.Workspaces");
var keyType = asm.GetType("Microsoft.CodeAnalysis.SymbolKey", true)!;
Console.WriteLine($"Assembly={asm.FullName}; SymbolKey.IsVisible={keyType.IsVisible}");
var create = keyType.GetMethod("CreateString",
    new[] { typeof(ISymbol), typeof(CancellationToken) })!;
var resolve = keyType.GetMethod("ResolveString",
    new[] { typeof(string), typeof(Compilation), typeof(bool), typeof(CancellationToken) })!;
string Key(ISymbol s) => (string)create.Invoke(null,
    new object?[] { s, default(CancellationToken) })!;
ISymbol? Resolve(string key, Compilation c)
{
    var resolution = resolve.Invoke(null,
        new object?[] { key, c, false, default(CancellationToken) })!;
    return (ISymbol?)resolution.GetType().GetProperty("Symbol")!.GetValue(resolution);
}
CSharpCompilation Compile(string text, string path) => CSharpCompilation.Create(
    "SharedAssembly", new[] { CSharpSyntaxTree.ParseText(text, path: path) },
    new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

var a = Compile("namespace N { public class C { public void M() {} } }", "A.cs");
var b = Compile("namespace N { public class C { public void M() { int differentBody = 1; } } }", "B.cs");
var sa = a.GetTypeByMetadataName("N.C")!.GetMembers("M").Single();
var sb = b.GetTypeByMetadataName("N.C")!.GetMembers("M").Single();
Console.WriteLine($"Different projects, same assembly identity: equalKeys={Key(sa) == Key(sb)}; AkeyInB={Resolve(Key(sa), b)!.Locations[0].SourceTree!.FilePath}");

var before = Compile("class C { void M() { { void L() {} L(); } } }", "Local.cs");
var after = Compile("class C { void M() { { void L() { int newlyInserted = 1; } L(); } { void L() {} L(); } } }", "Local.cs");
var tree = before.SyntaxTrees.Single();
var local = before.GetSemanticModel(tree).GetDeclaredSymbol(
    tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single())!;
var resolvedLocal = Resolve(Key(local), after)!;
Console.WriteLine($"Old local key after insertion: {resolvedLocal.DeclaringSyntaxReferences.Single().GetSyntax()}");
Console.WriteLine($"Errors: {a.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error)}/{b.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error)}/{before.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error)}/{after.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error)}");
```

Дополнительный путь к той же проблеме — linked source: существующий
[SolutionManager.FindDocumentAsync](../../../../Services/SolutionManager.cs),
строки 408–437, выбирает первый Document по пути. Поэтому предложенный
location-режим тоже не различает несколько project contexts одного файла,
если просто переиспользовать текущий helper.

Failure scenario:
1. В solution загружены два независимых проекта с одним `AssemblyName`,
   одинаковыми `N.C.M` и разными реализациями; агент выбирает символ из A.
2. `get_symbol_info` возвращает только предписанную строку ключа,
   неотличимую от ID символа B. Следующий ID-only вызов не содержит
   project context.
3. Resolver либо произвольно выбирает компиляцию и читает/переименовывает
   не тот символ, либо корректно возвращает ambiguity, но выбрать A
   повторной передачей того же ID невозможно. Обещанный точный workflow
   не работает в обоих вариантах.

Suggested change:
Определить идентичность как минимум в контексте проекта/компиляции
опубликованной сессии. Разрешить соответствующий envelope/handle либо
явный selector; определить правила его жизни при reload и смене TFM.
Для location-режима возвращать различимые project candidates при
нескольких memberships. Приёмка должна включать одинаковые assembly
identity и linked documents, с доказательством чтения/изменения именно
выбранного проекта, а не только сравнения display names.

Confidence:
High

---

<a id="e2-04"></a>

ID: E2-04
Severity: High
Category: Lifecycle

Target:
[stage-2-symbol-identity.md](../proposal-v1/stage-2-symbol-identity.md), «Цель»,
строки 12–22; «Новые tools», строки 60–63; «Тесты», строка 89.

Claim:
Агент резолвит символ один раз и повторно использует ID после disk sync;
поддерживаемые символы включают local function.

Evidence:
XML-документация Roslyn 5.9.0 в `T:Microsoft.CodeAnalysis.SymbolKey`
определяет разрешение interior-method symbols, включая local functions,
по индексу среди символов с одинаковыми именем и kind. Это устойчиво
к некоторым текстовым правкам, но не является постоянной идентичностью.

В runtime probe из E2-03 исходный метод содержит блок с `void L() {}`.
Перед ним добавляется другой блок с новой `void L()`; исходная функция
сохранена. Старый ключ успешно и однозначно возвращает новую функцию
с `int newlyInserted = 1`. Все компиляции валидны. Проверка только
`missing`/`ambiguous` этот случай не обнаруживает.

[SolutionManager.GetPublishedSolutionAfterDiskSyncAsync](../../../../Services/SolutionManager.cs),
строки 962–972, сначала применяет накопленные изменения, затем отдаёт
текущий `_solution`. Следующий вызов не обязан работать на snapshot,
из которого первоначально получен ID. Freshness gate записи защищает
выбранный write base, но не доказывает историческую тождественность
символа, ошибочно разрешённого уже в свежем snapshot.

Failure scenario:
1. Агент получает ID исходной локальной функции `L` для чтения или rename.
2. Между вызовами на диск сохраняется новый предшествующий блок с другой
   `L`; очередной semantic tool выполняет disk sync.
3. Старый ID разрешается в новую функцию без ошибки и ambiguity.
   `get_symbol_source` возвращает чужое тело, а rename с этим ID может
   переименовать чужую функцию на вполне свежем write base.

Suggested change:
Ограничить гарантию сохранения ID конкретными видами правок и символов.
Для body-level symbols выбрать проверку версии/привязки и fail-closed
инвалидацию при непроверяемом изменении либо другой механизм tracking,
который действительно доказывает сохранение цели. Не лечить случай
выбором первого результата: он уже единственный. Добавить acceptance
на вставку, удаление и перестановку одноимённых локальных функций;
требовать сохранения исходной цели или явного stale-ID, но не просто
успешного resolve после sync.

Confidence:
High
