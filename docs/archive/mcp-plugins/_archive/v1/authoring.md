# Как собрать плагин

Дата: 2026-09-27. Статус: **концепция**. Контракт хоста — [README.md](README.md).
Загрузчика в runtime ещё нет, поэтому проекта `samples/RoslynMcpPlugin` в дереве
нет: без `IRoslynMcpPlugin` он не собирается. Этот текст — то, что копируют
коллеги, когда загрузчик появится. Продуктовый README сюда не переносится.

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
`RoslynMcpPlugin.pdb`, `RoslynMcpPlugin.deps.json` и `plugin.json`. Чужие
пакеты плагина, если они появятся, попадают туда же. Сборки хоста туда не
копируются.

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

`Version` пакета MCP — тот же мажор, что у хоста (`ModelContextProtocol` 1.3.0
на исходниках 1.4.15). `Private` / `ExcludeAssets=runtime` оставляют эти сборки
у процесса хоста. В рантайме загрузчик отдаёт уже загруженные
`RoslynMcpServer`, `ModelContextProtocol`, `Microsoft.CodeAnalysis.*`,
`Microsoft.Extensions.*` по имени. Тогда `SolutionManager` в плагине и в хосте —
один тип.

Ссылка на опубликованный exe вместо проекта допустима тем же правилом
(`Reference` с `Private=false`), если коллеги не держат исходники сервера.
Версия сборки, против которой компилируют, совпадает с запущенным хостом.
`minHostVersion` хост читает до загрузки и при другом мажоре каталог пропускает.

Свой `PackageReference` плагин копирует в выход как обычно. Эти DLL — его
зависимости, хост их из своего publish не подменяет.

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
| `minHostVersion` | Минимальная `Version` хоста. Другой мажор — каталог пропущен до `Load`. |

В drop-in имя подкаталога совпадает с `id`: `{BaseDirectory}/plugins/sample/`.
В dev-пути каталог называется `net10.0`, id оттуда не читается.

Нет файла, битый JSON, нет `entry` или `pluginType` — этот каталог пропускается,
в лог пишется причина, остальные плагины и встроенные тулы живы.

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
не создавать.

Анализ на уже загруженном решении:

- `SolutionManager.GetPublishedSolutionAsync`, `FindDocumentAsync`,
  `ResolvePathAgainstWorkspace`, `GetLoadedWorkspacePath`,
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
синглтон и пишут тул поверх `GetPublishedSolutionAsync`.

Не показывает выгрузку сборки, hot-reload, песочницу и загрузку плагина из
NuGet или по сети.
