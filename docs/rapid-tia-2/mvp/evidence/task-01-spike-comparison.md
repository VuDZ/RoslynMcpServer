# MVP/task-01 — сверка со спайком, замер и проба блокировки файла

- Дата: **2026-11-16** (Europe/Moscow). Исполнитель: Claude Fable 5.2, сабагент.
- Машина: Linux x64 (ядро 7.0.0-42), 2 процессора, 3843 МБ, .NET SDK 10.0.114.
- Код: рабочая копия ветки `rapid-tia` на `ba00ef2` с незакоммиченными файлами task-01, Release-сборка
  solution (`dotnet build RoslynMcpServer.sln -c Release`). Основные разделы сняты на сборке до ревью; повтор
  на итоговой сборке — в разделе [«Повтор после раунда ревью 1»](#повтор-после-раунда-ревью-1).
- Спайк `spikes/TestImpactIlProbe` не менялся; он собран как есть через `ProjectReference`.

## Как сверялось

Спайк и продукт не видят внутренние типы друг друга, а спайк не входит в solution. Поэтому сверка сделана двумя
маленькими консольными программами вне репозитория, в `$HOME_DIR/tia-mvp/compare/`. Обе пишут один и тот же
вид файла: строка на пару «тестовый класс, достигнутый тип» —
`сборка теста<TAB>тестовый класс<TAB>сборка типа<TAB>тип`, строки отсортированы. Файлы сравниваются `cmp` и `comm`.

- `spike-dump` называется `TestImpactIlProbe.Tests` (этому имени спайк открывает внутренние типы) и вызывает
  код спайка без изменений: `GraphLoader.Load` и `TestClosures.Compute` для слоёв `References`, `Discovery`,
  `DiscoveryWithLoads`. Он же пишет список файлов, которые спайк прочитал (тем же отбором, что в
  `GraphLoader.Load`: сборки с исходниками на диске, один файл на MVID).
- `product-dump` называется `RoslynMcpServer.Tests` (этому имени сервер открывает внутренние типы), ссылается
  на собранный `bin/Release/net10.0/RoslynMcpServer.dll` и вызывает `IlNeighborMapBuilder.Build`,
  `IlDependencyGraphBuilder.BuildFromFiles` и `IlTestClosures.ReachedTypes` для трёх ступеней.

Исходники обеих программ — в конце файла.

## Сверка на одних и тех же сборках

Спайку даны выходные каталоги пяти проектов solution (Release, `net10.0`). Он нашёл в них 7 сборок с
исходниками на диске: пять сборок проектов и две сборки форка BuildHost из подкаталога `BuildHost-netcore`
(`Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost`, `…MSBuild.Contracts`). Продукту даны те же 7 сборок:
пять — по выходному пути своего проекта, две сборки форка — по пути, который выбрал спайк. Тестовым проектом
помечен один: `RoslynMcpServer.Tests`. Совпадение наборов проверено по паре «имя сборки, MVID».

```bash
R=REPO_ROOT
C=$HOME_DIR/tia-mvp/compare
BINS="$R/bin/Release/net10.0;$R/RoslynMcpServer.Tests/bin/Release/net10.0;$R/RoslynMcpServer.LifecycleTestHost/bin/Release/net10.0;$R/RoslynMcpServer.Tests/SourceStructure/DeadlockWitnessHost/bin/Release/net10.0;$R/samples/RoslynMcpPlugin/bin/Release/net10.0"

(cd $C/spike-dump && dotnet build -c Release) && (cd $C/product-dump && dotnet build -c Release)
dotnet $C/spike-dump/bin/Release/net10.0/TestImpactIlProbe.Tests.dll "$BINS" "$R" "$C/out"
dotnet $C/product-dump/bin/Release/net10.0/RoslynMcpServer.Tests.dll $C/inputs-same-as-spike.tsv $C/out

cd $C/out
diff <(cut -f1,2 spike-files.tsv | sort) <(cut -f1,2 product-files.tsv | sort)   # имя сборки и MVID
for l in references discovery discoveryWithLoads; do cmp spike-$l.tsv product-$l.tsv; done
diff spike-neighbors.tsv product-neighbors.tsv
```

`inputs-same-as-spike.tsv` (пути от корня репозитория, второй столбец — признак тестового проекта):

```text
bin/Release/net10.0/RoslynMcpServer.dll	false
RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.dll	true
RoslynMcpServer.LifecycleTestHost/bin/Release/net10.0/RoslynMcpServer.LifecycleTestHost.dll	false
RoslynMcpServer.Tests/SourceStructure/DeadlockWitnessHost/bin/Release/net10.0/DeadlockWitnessHost.dll	false
samples/RoslynMcpPlugin/bin/Release/net10.0/RoslynMcpPlugin.dll	false
bin/Release/net10.0/BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll	false
bin/Release/net10.0/BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.Contracts.dll	false
```

Вывод программ:

```text
spike: 7 assemblies, 826 types, 3166 edges, 173 test classes, 0 unreadable bodies
spike references: 14785 pairs of a test class and a reached type
spike discovery: 16112 pairs of a test class and a reached type
spike discoveryWithLoads: 47405 pairs of a test class and a reached type
product: 7 inputs, 0 unread, 7 assemblies, 826 types, 3166 edges, 173 test classes, 0 unreadable bodies
product references: 14785 pairs of a test class and a reached type
product discovery: 16112 pairs of a test class and a reached type
product discoveryWithLoads: 47405 pairs of a test class and a reached type
```

| Что сравнивалось | Спайк | Продукт | Результат |
| --- | --- | --- | --- |
| Набор сборок (имя, MVID) | 7 | 7 | совпадает |
| Типов, рёбер, тестовых классов | 826, 3166, 173 | 826, 3166, 173 | совпадает |
| Ступень 1 и слой `references`, пар | 14785 | 14785 | файлы побайтно равны |
| Ступень 2 и слой `discovery`, пар | 16112 | 16112 | файлы побайтно равны |
| Ступень 3 и слой `discoveryWithLoads`, пар | 47405 | 47405 | файлы побайтно равны |
| Сборки рядом с `RoslynMcpServer.Tests` | 5 | 5 | совпадает |

Расхождений ступени 3 на этом репозитории нет: `RoslynMcpServer.Tests.dll` лежит только в своём выходном
каталоге, чужого каталога с его копией нет.

Рядом с тестовой сборкой и у спайка, и у продукта: `DeadlockWitnessHost`, `RoslynMcpServer`,
`RoslynMcpServer.LifecycleTestHost`, сама `RoslynMcpServer.Tests` и `Microsoft.CodeAnalysis.Workspaces.MSBuild.Contracts`.
Последняя попала в соседи в этой сверке потому, что сборка форка передана продукту как вход, а файл с таким
именем (из пакета NuGet) лежит в каталоге тестовой сборки. Среди настоящих проектов solution её нет, и в замере
ниже её нет.

## Проверка объяснения «только чужие каталоги»

Чтобы проверить, что единственный источник расхождения ступени 3 — чужой каталог, он создан искусственно:
каталог `$C/foreign` с копиями `RoslynMcpServer.Tests.dll` и `RoslynMcpPlugin.dll` (и их PDB) добавлен спайку
ещё одним `--bin`. Продукту даны те же 7 сборок.

```bash
mkdir -p $C/foreign
cp -p $R/RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.{dll,pdb} \
      $R/samples/RoslynMcpPlugin/bin/Release/net10.0/RoslynMcpPlugin.{dll,pdb} $C/foreign/
dotnet $C/spike-dump/bin/Release/net10.0/TestImpactIlProbe.Tests.dll "$BINS;$C/foreign" "$R" "$C/out-foreign"
dotnet $C/product-dump/bin/Release/net10.0/RoslynMcpServer.Tests.dll $C/inputs-same-as-spike.tsv $C/out-foreign
cd $C/out-foreign
for l in references discovery discoveryWithLoads; do
  echo "$l: spike-only $(comm -23 spike-$l.tsv product-$l.tsv | wc -l), product-only $(comm -13 spike-$l.tsv product-$l.tsv | wc -l)"
done
comm -23 spike-discoveryWithLoads.tsv product-discoveryWithLoads.tsv | cut -f3 | sort | uniq -c
```

```text
references: spike-only 0, product-only 0
discovery: spike-only 0, product-only 0
discoveryWithLoads: spike-only 228, product-only 0
    228 RoslynMcpPlugin
```

Спайк добавил `RoslynMcpPlugin` в соседи тестовой сборки, продукт — нет. Все 228 лишних пар спайка — это
76 тестовых классов, доходящих до загрузки по имени, на 3 типа `RoslynMcpPlugin`. Пар, которые есть у продукта
и которых нет у спайка, нет ни в одной ступени. Это ровно разница, названная в задаче и в M-06.

## Замер на сборках этого репозитория

Вход — пять выходных сборок проектов solution (`inputs-solution.tsv`: первые пять строк списка выше), Release.
`RoslynMcpServer.dll` — 1,77 МБ, `RoslynMcpServer.Tests.dll` — 2,85 МБ. Каждый запуск — новый процесс, то есть
время включает JIT; `measure` строит карту соседства, читает сборки, строит граф и замыкания всех тестовых
классов для трёх ступеней и ничего не пишет на диск.

```bash
for i in 1 2 3; do
  /usr/bin/time -f "wall %e s, max RSS %M KB" \
    dotnet $C/product-dump/bin/Release/net10.0/RoslynMcpServer.Tests.dll $C/inputs-solution.tsv $C/out-solution measure
done
DOTNET_gcServer=1 /usr/bin/time -f "wall %e s, max RSS %M KB" \
  dotnet $C/product-dump/bin/Release/net10.0/RoslynMcpServer.Tests.dll $C/inputs-solution.tsv $C/out-solution measure
```

```text
product: 5 inputs, 0 unread, 5 assemblies, 750 types, 3031 edges, 173 test classes, 0 unreadable bodies
time: neighbor map 10 ms, read and graph 451 ms, closures of three stages 49 ms
memory: peak working set 72 MB, managed heap after full collection 0.8 MB (graph and closures alive)
wall 0.58 s, max RSS 73432 KB
time: neighbor map 11 ms, read and graph 510 ms, closures of three stages 47 ms
memory: peak working set 71 MB, managed heap after full collection 0.8 MB (graph and closures alive)
wall 0.63 s, max RSS 72824 KB
time: neighbor map 11 ms, read and graph 461 ms, closures of three stages 50 ms
memory: peak working set 72 MB, managed heap after full collection 0.8 MB (graph and closures alive)
wall 0.59 s, max RSS 73320 KB
--- server GC (DOTNET_gcServer=1)
time: neighbor map 10 ms, read and graph 468 ms, closures of three stages 29 ms
memory: peak working set 83 MB, managed heap after full collection 0.7 MB (graph and closures alive)
wall 0.58 s, max RSS 85264 KB
```

| Величина | Workstation GC, три запуска | Server GC, один запуск |
| --- | --- | --- |
| Карта соседства | 10–11 мс | 10 мс |
| Чтение пяти сборок и граф | 451–510 мс | 468 мс |
| Замыкания 173 тестовых классов, три ступени | 47–50 мс | 29 мс |
| Весь процесс, с запуском среды | 0,58–0,63 с | 0,58 с |
| Пик памяти процесса (max RSS) | 72,8–73,4 МБ | 85,3 МБ |
| Управляемая куча после полной сборки, граф и замыкания живы | 0,8 МБ | 0,7 МБ |

Порога нет. Для сравнения: спайк на этом репозитории дал 1,1 с и 81 МБ, но он ещё искал файлы, отбирал сборки
по PDB, читал PDB и считал статистику, так что числа не одного и того же объёма работы. Сервер работает с
Server GC, отдельный процесс сверки по умолчанию — с Workstation GC, поэтому замерены оба режима. Пик памяти
— это пик процесса из пяти сборок; что удерживает сессия, покажет task-03.

## Проба: что может другой писатель, пока файл открыт (Linux)

Проверка «чтение не держит файл» обязательна на Windows; этот прогон сделан на Linux и Windows не заменяет.
На Linux .NET изображает `FileShare` рекомендательной блокировкой `flock`: любое открытие с `FileShare`,
отличным от `None`, берёт разделяемую блокировку, а запрос `FileShare.None` — исключительную. От флагов
`ReadWrite | Delete` читателя это не зависит. Проба останавливает `IlAssemblyImageLoader.Load` в точке, где
файл открыт, и пробует из той же точки пять действий:

```bash
dotnet $C/product-dump/bin/Release/net10.0/RoslynMcpServer.Tests.dll lock \
  $R/samples/RoslynMcpPlugin/bin/Release/net10.0/RoslynMcpPlugin.dll $C/lock
```

```text
open for write, FileShare.Read (File.WriteAllBytes): ok
open for write, FileShare.None: IOException: The process cannot access the file '…/Probe.dll' because it is being used by another process.
File.Copy(overwrite: true): IOException: The process cannot access the file '…/Probe.dll' because it is being used by another process.
File.Move(overwrite: true) onto it: ok
File.Delete: ok
```

Следствие для Linux: пока идёт чтение (миллисекунды на сборку), писатель на .NET, который просит
исключительный доступ, получает отказ; `File.Copy` с перезаписью — в их числе. Это свойство .NET на Unix, от
режима доступа читателя не зависит (с `File.OpenRead` ожидается то же, но это не запускалось). Запись без исключительного доступа,
замена файла переименованием и удаление проходят. Вопрос вынесен в отчёт задачи.

## Повтор после раунда ревью 1

По ревью изменился загрузчик образа (параметры открытия собраны в `CreateOpenOptions`) и добавлены тесты,
поэтому сверка, замер и проба повторены на итоговой Release-сборке теми же командами (выход — `$C/out-r1`).
Тестовая сборка стала больше на один тестовый класс и два типа, отсюда другие числа.

```text
spike: 7 assemblies, 828 types, 3176 edges, 174 test classes, 0 unreadable bodies
spike references: 14799 pairs of a test class and a reached type
spike discovery: 16126 pairs of a test class and a reached type
spike discoveryWithLoads: 47495 pairs of a test class and a reached type
product: 7 inputs, 0 unread, 7 assemblies, 828 types, 3176 edges, 174 test classes, 0 unreadable bodies
product references: 14799 pairs of a test class and a reached type
product discovery: 16126 pairs of a test class and a reached type
product discoveryWithLoads: 47495 pairs of a test class and a reached type
```

- Набор сборок по имени и MVID совпадает; файлы трёх ступеней побайтно равны (`cmp`); соседи совпадают.
- Замер на пяти сборках solution, три запуска, Workstation GC: чтение и граф 446–496 мс (752 типа, 3041
  ребро), замыкания 174 тестовых классов 43–48 мс, весь процесс 0,57–0,62 с, пик памяти 73,6–73,7 МБ. Режим
  Server GC и проверка с искусственным чужим каталогом не повторялись.
- Проба блокировки на Linux дала тот же результат, что выше.

## Исходники программ сверки

`spike-dump/SpikeDump.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- The spike shows its internals to an assembly with this name only. -->
    <AssemblyName>TestImpactIlProbe.Tests</AssemblyName>
    <RootNamespace>SpikeDump</RootNamespace>
    <ServerGarbageCollection>false</ServerGarbageCollection>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="REPO_ROOT/spikes/TestImpactIlProbe/TestImpactIlProbe.csproj" />
  </ItemGroup>
</Project>
```

`spike-dump/Dump.cs`:

```csharp
using System.Numerics;
using TestImpactIlProbe;

namespace SpikeDump;

internal static class Dump
{
    // args: <bin directories separated by ';'> <source root> <output directory>
    private static void Main(string[] args)
    {
        var bins = args[0].Split(';', StringSplitOptions.RemoveEmptyEntries);
        var root = args[1];
        var output = args[2];
        Directory.CreateDirectory(output);

        // The same selection GraphLoader.Load makes: one file per MVID among assemblies with sources on disk.
        var paths = new DocumentPaths([root]);
        var files = AssemblyFiles.Find(bins);
        var read = files.Distinct
            .Select(file => AssemblyReader.Identify(file, paths))
            .Where(identity => identity is not null && identity.Origin == AssemblyOrigin.Sources)
            .Select(identity => identity!)
            .GroupBy(identity => identity.Mvid)
            .Select(group => group.OrderBy(identity => identity.Path, StringComparer.Ordinal).First())
            .OrderBy(identity => identity.Name, StringComparer.Ordinal)
            .ToList();
        File.WriteAllLines(Path.Combine(output, "spike-files.tsv"), read.Select(identity => $"{identity.Name}\t{identity.Mvid}\t{identity.Path}"));

        var graph = GraphLoader.Load(bins, [root]).Graph;
        File.WriteAllLines(
            Path.Combine(output, "spike-neighbors.tsv"),
            Enumerable.Range(0, graph.Assemblies.Length)
                .Where(assembly => graph.TestAssembly[assembly])
                .Select(assembly => graph.Assemblies[assembly] + "\t" + string.Join(";", graph.Deployed[assembly].Select(other => graph.Assemblies[other]).Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal));
        Console.WriteLine($"spike: {graph.Assemblies.Length} assemblies, {graph.Names.Length} types, {graph.Edges} edges, {graph.TestClasses.Length} test classes, {graph.UnreadableBodies} unreadable bodies");
        foreach (var (layer, name) in new[] { (Layer.References, "references"), (Layer.Discovery, "discovery"), (Layer.DiscoveryWithLoads, "discoveryWithLoads") })
        {
            var rows = TestClosures.Compute(graph, layer);
            var lines = new List<string>();
            for (var index = 0; index < rows.Length; index++)
            {
                var test = graph.TestClasses[index];
                for (var word = 0; word < rows[index].Length; word++)
                {
                    for (var bits = rows[index][word]; bits != 0; bits &= bits - 1)
                    {
                        var node = (word << 6) + BitOperations.TrailingZeroCount(bits);
                        lines.Add($"{graph.Assemblies[graph.AssemblyOf[test]]}\t{graph.Names[test]}\t{graph.Assemblies[graph.AssemblyOf[node]]}\t{graph.Names[node]}");
                    }
                }
            }

            lines.Sort(StringComparer.Ordinal);
            File.WriteAllLines(Path.Combine(output, $"spike-{name}.tsv"), lines);
            Console.WriteLine($"spike {name}: {lines.Count} pairs of a test class and a reached type");
        }
    }
}
```

`product-dump/ProductDump.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- The server shows its internals to an assembly with this name. -->
    <AssemblyName>RoslynMcpServer.Tests</AssemblyName>
    <RootNamespace>ProductDump</RootNamespace>
    <ServerGarbageCollection>false</ServerGarbageCollection>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="RoslynMcpServer">
      <HintPath>SDK_ROOT/RoslynMcpServer.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

`product-dump/Dump.cs`:

```csharp
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace ProductDump;

internal static class Dump
{
    // args: <inputs.tsv: "path<TAB>true|false" per line> <output directory> [measure]
    private static void Main(string[] args)
    {
        if (args[0] == "lock")
        {
            LockProbe.Run(args[1], args[2]);
            return;
        }

        IlAssemblyInput[] inputs = [.. File.ReadAllLines(args[0])
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .Select(parts => new IlAssemblyInput(parts[0], bool.Parse(parts[1])))];
        var output = args[1];
        var measureOnly = args.Length > 2 && args[2] == "measure";

        var watch = Stopwatch.StartNew();
        var neighbors = IlNeighborMapBuilder.Build(inputs);
        var mapTime = watch.Elapsed;
        watch.Restart();
        var result = IlDependencyGraphBuilder.BuildFromFiles(inputs, neighbors);
        var graphTime = watch.Elapsed;
        watch.Restart();
        var pairs = Enum.GetValues<IlDependencyStage>()
            .Select(stage => result.Closures.TestClasses.Sum(test => (long)result.Closures.ReachedTypes(test, stage).Count))
            .ToArray();
        var closureTime = watch.Elapsed;
        var graph = result.Graph;
        Console.WriteLine($"product: {inputs.Length} inputs, {result.Unread.Count} unread, {graph.Assemblies.Length} assemblies, {graph.Types.Length} types, "
            + $"{graph.Dependencies.Sum(dependencies => dependencies.Length)} edges, {graph.TestClasses.Length} test classes, {graph.UnreadableBodies} unreadable bodies");
        Console.WriteLine($"product pairs by stage: {string.Join(", ", pairs)}");
        Console.WriteLine($"time: neighbor map {mapTime.TotalMilliseconds:F0} ms, read and graph {graphTime.TotalMilliseconds:F0} ms, closures of three stages {closureTime.TotalMilliseconds:F0} ms");
        using var process = Process.GetCurrentProcess();
        Console.WriteLine($"memory: peak working set {process.PeakWorkingSet64 / (1024.0 * 1024):F0} MB, managed heap after full collection {GC.GetTotalMemory(forceFullCollection: true) / (1024.0 * 1024):F1} MB (graph and closures alive)");
        GC.KeepAlive(result);
        if (measureOnly)
        {
            return;
        }

        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "product-files.tsv"), inputs.Select(input => Identity(input.OutputFilePath)).Order(StringComparer.Ordinal));
        File.WriteAllLines(Path.Combine(output, "product-unread.tsv"), result.Unread.Select(unread => $"{unread.Path}\t{unread.Reason}\t{unread.Message}"));
        File.WriteAllLines(
            Path.Combine(output, "product-neighbors.tsv"),
            Enumerable.Range(0, graph.Assemblies.Length)
                .Where(assembly => graph.TestClasses.Any(test => graph.AssemblyOf[test] == assembly))
                .Select(assembly => graph.Assemblies[assembly] + "\t" + string.Join(";", graph.Neighbors[assembly].Select(other => graph.Assemblies[other]).Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal));
        foreach (var (stage, name) in new[] { (IlDependencyStage.References, "references"), (IlDependencyStage.Discovery, "discovery"), (IlDependencyStage.DiscoveryWithLoads, "discoveryWithLoads") })
        {
            var lines = result.Closures.TestClasses
                .SelectMany(test => result.Closures.ReachedTypes(test, stage).Select(type => $"{test.Assembly}\t{test.Name}\t{type.Assembly}\t{type.Name}"))
                .Order(StringComparer.Ordinal)
                .ToList();
            File.WriteAllLines(Path.Combine(output, $"product-{name}.tsv"), lines);
            Console.WriteLine($"product {name}: {lines.Count} pairs of a test class and a reached type");
        }
    }

    private static string Identity(string path)
    {
        using var image = new PEReader(File.OpenRead(path));
        var metadata = image.GetMetadataReader();
        return $"{metadata.GetString(metadata.GetAssemblyDefinition().Name)}\t{metadata.GetGuid(metadata.GetModuleDefinition().Mvid)}\t{path}";
    }
}
```

`product-dump/LockProbe.cs`:

```csharp
using RoslynMcpServer.Services.Testing.Il;

namespace ProductDump;

/// <summary>What another writer can do with the file while the product reader holds it open.</summary>
internal static class LockProbe
{
    public static void Run(string source, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Probe.dll");
        File.Copy(source, path, overwrite: true);
        IlAssemblyImageLoader.Load(path, () =>
        {
            Try("open for write, FileShare.Read (File.WriteAllBytes)", () => File.WriteAllBytes(path, File.ReadAllBytes(source)));
            Try("open for write, FileShare.None", () => { using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None); });
            Try("File.Copy(overwrite: true)", () => File.Copy(source, path, overwrite: true));
            Try("File.Move(overwrite: true) onto it", () => { var other = path + ".new"; File.Copy(source, other, true); File.Move(other, path, overwrite: true); });
            Try("File.Delete", () => File.Delete(path));
        });
    }

    private static void Try(string what, Action action)
    {
        try
        {
            action();
            Console.WriteLine($"{what}: ok");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{what}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
```
