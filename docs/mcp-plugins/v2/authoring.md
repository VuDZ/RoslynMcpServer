# Как собрать плагин

Дата: 2026-09-27. Статус: **post-arbitration specification v2**. Контракт хоста —
[README.md](README.md). Загрузчика в runtime ещё нет, поэтому проекта
`samples/RoslynMcpPlugin` в дереве нет: без `IRoslynMcpPlugin` он не собирается.
Этот текст — то, что копируют коллеги, когда загрузчик появится. Продуктовый
README сюда не переносится.

Шаблон один. Его же тесты хоста грузят через `ROSLYN_MCP_PLUGINS` и проверяют
обнаружение, префикс, DI и теневую копию. Своей эвристики в шаблоне нет.

## Что получается

Class library `net10.0`. Хост (self-contained publish, не single-file) грузит
её при старте, до `host.RunAsync()`. Первый `tools/list` уже видит тулы плагина.
Свой `MSBuildWorkspace` плагин не поднимает: тот же процесс, тот же
`SolutionManager`, тот же загруженный `Solution`.

Плагин зовёт публичные сервисы хоста и Roslyn. Методы встроенных тулов
(`RunDotNetTest`, `FindSymbolReferences` и остальные) не зовёт: они возвращают
текст для агента. Классы тулов в DI контрактом не являются.

## Дерево шаблона

```
samples/RoslynMcpPlugin/
  RoslynMcpPlugin.csproj
  plugin.json
  SamplePlugin.cs
  SampleTools.cs
```

После `dotnet build` в `bin/Debug/net10.0/` лежат `RoslynMcpPlugin.dll`,
`RoslynMcpPlugin.pdb`, `RoslynMcpPlugin.deps.json` и `plugin.json`. Частные
managed-зависимости плагина лежат там же. Сборки контракта хоста туда не
копируются. Какое свойство SDK включает копирование частных зависимостей
(`CopyLocalLockFileAssemblies`, `EnableDynamicLoading` или эквивалент), текст
не нормирует: исход сборки выполняется, контракт хоста в выход не попадает.

## Проект

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\RoslynMcpServer.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <PackageReference Include="ModelContextProtocol" Version="1.3.0">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <None Include="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`Major.Minor` пакета MCP равен загруженной сборке хоста (`ModelContextProtocol`
1.3.0 на исходниках 1.4.15). Exact-равенство патча не требуется. `Private` /
`ExcludeAssets=runtime` оставляют эти сборки у процесса хоста. В рантайме
загрузчик отдаёт уже загруженные `RoslynMcpServer`, `ModelContextProtocol`,
`Microsoft.CodeAnalysis.*`, `Microsoft.Extensions.*` по имени. Тогда
`SolutionManager` в плагине и в хосте — один тип.

Без исходников хоста ссылка ведёт на `RoslynMcpServer.dll` из publish, не на
exe: exe self-contained publish — native apphost без метаданных CLR. Правило
то же (`Reference`, `Private=false`). Как автор получает соседние compile-time
ссылки, если одной DLL хоста компилятору мало, текст не нормирует и граф NuGet
не перечисляет.

Одна `Version` продукта не гарантирует типы MCP и Roslyn. Хост сверяет до
`Load` entry и `minHostVersion`, и версии ссылок. Носитель версий ссылок —
compile-запись в `deps.json`, в том числе когда runtime-ассеты контракта
исключены. Поля `plugin.json` под эти версии не добавляются.

Свой managed-`PackageReference` сборка копирует в выход. Эти DLL — его
зависимости, хост их из своего publish не подменяет. Если загрузка идёт из
теневой копии, `Assembly.Location` такой DLL — каталог копии. Пакет с native
этим не обещается: v1 не резолвит native и не добавляет `LoadUnmanagedDll`.
Файл в `runtimes/` P/Invoke не обещает.

## Манифест

`plugin.json` в корне проекта и, после сборки, рядом с entry DLL.

```json
{
  "id": "sample",
  "entry": "RoslynMcpPlugin.dll",
  "pluginType": "RoslynMcpPlugin.SamplePlugin",
  "toolPrefix": "sample_",
  "minHostVersion": "1.4.15"
}
```

| Поле | Смысл |
|---|---|
| `id` | Имя плагина. `IRoslynMcpPlugin.Name` возвращает то же значение. В `get_mcp_server_info` показывается оно. |
| `entry` | Имя входной сборки в этом каталоге. Остальные DLL хост сам не открывает. |
| `pluginType` | Единственный тип, который хост берёт из entry (`Assembly.GetType`). Обхода всех типов нет. |
| `toolPrefix` | Каждое имя тула этого плагина начинается с него. |
| `minHostVersion` | Нижняя граница `Version` продукта. Тот же мажор и хост не старее — иначе каталог пропущен до `Load`. Ось MCP, `RoslynMcpServer` и `Microsoft.CodeAnalysis` это поле не закрывает. |

В drop-in имя подкаталога совпадает с `id`: `{BaseDirectory}/plugins/sample/`.
В dev-пути каталог называется `net10.0`, id оттуда не читается.

Нет файла, битый JSON, нет `entry` или `pluginType` — этот каталог пропускается,
в лог пишется причина, остальные плагины и встроенные тулы живы.

До `Load` entry хост дополнительно пропускает каталог, если не сошлась ось
ссылок (носитель — compile-запись `deps.json`):

- `Major.Minor` ссылки на `ModelContextProtocol` не равен загруженной сборке хоста;
- мажор ссылки на `RoslynMcpServer` не совпал или ссылка новее хоста;
- мажор `Microsoft.CodeAnalysis` не совпал;
- при том же мажоре версия ссылки `Microsoft.CodeAnalysis` новее загруженной
  сборки хоста (`Version` целиком: 5.9.2 на хосте 5.9.0 — пропуск; 5.8 на 5.9.0 —
  загрузка).

Гейт `Microsoft.Extensions.*` это руководство не задаёт.

## Регистрация

```csharp
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using RoslynMcpServer.Services;

namespace RoslynMcpPlugin;

public sealed class SamplePlugin : IRoslynMcpPlugin
{
    public string Name => "sample";

    public void Register(RoslynMcpPluginContext context)
    {
        context.AddToolsFrom<SampleTools>();
    }
}

public sealed class SampleTools
{
    private readonly SolutionManager _solutions;

    public SampleTools(SolutionManager solutions)
    {
        _solutions = solutions;
    }

    [McpServerTool(Name = "sample_loaded_workspace", Title = "Loaded workspace path")]
    [Description("Returns the loaded workspace path, or says that none is loaded.")]
    public string LoadedWorkspace()
    {
        var path = _solutions.GetLoadedWorkspacePath();
        return string.IsNullOrEmpty(path)
            ? "No workspace is loaded."
            : path;
    }
}
```

`Register` выполняется до `host.Build()`. Здесь регистрируют свои синглтоны
(`context.Services.AddSingleton<ImpactHeuristic>()`). Сам класс тула хост
создаёт на вызов так же, как встроенные: конструктор получает `SolutionManager`,
`ILogger<T>` и то, что плагин положил в этот `IServiceProvider`.

Имя тула без `sample_`, имя из каталога встроенных тулов или имя другого
плагина хост пропускает и пишет в лог. Профиль `lite`/`full` плагин не
фильтрует. В `list_tool_groups` его нет. `get_tool_help` берёт `[Description]`.

## Чем пользоваться

Общий `Solution` и публичные типы `RoslynMcpServer.Services`. Свой workspace
не создавать. Новый метод `SolutionManager` не нужен.

Анализ, который должен совпасть со встроенными semantic-тулами, вызывает
`GetSanitizedPublishedSolutionAsync`.

- `GetPublishedSolutionAsync` возвращает опубликованный снимок и очередь
  disk watcher не применяет.
- `GetPublishedSolutionAfterDiskSyncAsync` применяет очередь диска и возвращает
  сырой опубликованный снимок, без снятия заглушек `UnresolvedAnalyzerReference`.
- `GetSanitizedPublishedSolutionAsync` применяет очередь и возвращает снимок без
  этих заглушек. Опубликованный снимок не подменяется.

Остальные входы:

- `FindDocumentAsync`, `ResolvePathAgainstWorkspace`, `GetLoadedWorkspacePath`,
  `GetLoadedWorkspaceDirectory`. Относительный путь к файлу — только через
  `ResolvePathAgainstWorkspace`.
- `CallGraphHelper`, `TestDiscoveryHelper`, `GitChangedFilesHelper` — у них
  структурный результат, не текст тула.
- Дальше обычный Roslyn: `Document`, `SemanticModel`, `SymbolFinder`.

Запись на диск и правка документов идут через публичные методы
`SolutionManager` (`UpdateDocumentInMemoryAsync`, `ApplySolutionChangesToDiskAsync`).
`internal` write-boundary плагину не виден.

Процесс, если плагин сам запускает CLI:

- `DotNetCliRunner` — процесс, таймаут, раздельные stdout/stderr.
- `DotNetBuildArguments`, `DotNetTestArguments`, `DotNetBuildProbe`,
  `VstestOutputParser` — сборка командной строки и разбор вывода.

Оркестрация `run_dotnet_test` (сначала `dotnet build`, потом
`dotnet test --no-build`, фильтр без `()`, Configuration с `load_workspace`)
внутри `TestTools` и в этот список не входит. Пока нет отдельного структурного
раннера, плагин либо возвращает агенту список, либо собирает команду сам из
публичных аргументов и `DotNetCliRunner`. Повторно вызывать `TestTools` нельзя.

`internal` (`SymbolSelectionHelper`, `NavigationListingHelper`, overlay и
публикация анализаторов) не открывается через `InternalsVisibleTo`. Нужный
кусок выводят в public под конкретный сценарий.

## Куда класть сборку

Хост не сканирует cwd и профиль пользователя.

Drop-in, рядом с опубликованным exe:

```
{BaseDirectory}/plugins/sample/
  plugin.json
  RoslynMcpPlugin.dll
  RoslynMcpPlugin.pdb
  RoslynMcpPlugin.deps.json
```

Каталог копируют целиком из выхода сборки. Имя подкаталога — `sample`, как `id`.
Грузится этот путь, без теневой копии.

Dev, когда DLL лежит в `bin` и копировать её в publish не надо. Один из двух
способов, оба читают один и тот же `plugin.json`:

- переменная `ROSLYN_MCP_PLUGINS` — каталог `bin/Debug/net10.0` или путь к entry DLL;
- ключ `plugins` в `RoslynMcp.jsonc` рядом с exe или в cwd процесса — массив
  таких же путей.

Путь к DLL значит «вот entry». Соседние файлы хост не перебирает. Путь к
каталогу значит «прочитай `plugin.json` внутри и загрузи названный entry».

Перед загрузкой dev-пути хост копирует каталог (DLL, PDB, `plugin.json`,
`deps.json`, зависимости плагина) во временный каталог этого запуска и грузит
копию. Сборки хоста в копию не входят. Исходный `bin` можно перезаписать, пока
старый процесс ещё держит свою копию. Каталог копии свой у каждого запуска.
Копирование `runtimes/` не резолвит native.

## Цикл правки

1. Меняете код плагина и собираете его. Сборка проходит, даже если MCP ещё
   держит предыдущий запуск: заблокирована копия, не `bin`.
2. Reload MCP. Новый процесс копирует уже новый `bin` и грузит его.
3. Снова `load_workspace`. Перезапуск хоста сбрасывает загруженный `Solution`.
   Подхватить новую сборку без рестарта нельзя.

Cursor может оставить `RoslynMcpServer.exe` после отключения MCP. Этот процесс
держит свою теневую копию. На `bin` плагина он не опирается, следующий старт
ему не мешает.

## Отладка

Точка входа — хост с символами, не проект плагина. На время отладки команда MCP
в клиенте запускает сборку сервера из исходников (`dotnet run --project
RoslynMcpServer.csproj`) либо его `bin` с PDB. Рядом выставляется
`ROSLYN_MCP_PLUGINS` на `bin/Debug/net10.0` шаблона.

Версия этого хоста и `ProjectReference` плагина — одна сборка. Иначе
`SolutionManager` в отладчике и в процессе — разные типы, DI не сходится.

PDB копируется вместе с DLL. В переносимом PDB записаны пути исходников
плагина, точки останова встают на копию. Цепляться к процессу `RoslynMcpServer`.
Правка на лету для уже загруженной сборки не работает: тот же цикл, что у
самого сервера — пересобрать, Reload MCP, заново `load_workspace`.

Опубликованный self-contained exe без PDB для этой отладки не используют.
После сессии конфиг клиента возвращают на обычный publish.

## Чего шаблон не делает

Не содержит эвристику test-impact и не тащит её в этот репозиторий. Коллеги
копируют проект, меняют `id`, `toolPrefix` и `pluginType`, регистрируют свой
синглтон и пишут тул поверх `GetSanitizedPublishedSolutionAsync`, если анализ
должен совпасть со встроенными semantic-тулами.

Не показывает выгрузку сборки, hot-reload, песочницу, загрузку плагина из
NuGet или по сети и резолв native.
