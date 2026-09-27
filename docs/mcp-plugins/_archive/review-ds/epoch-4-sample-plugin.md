ID: E4-01
Severity: Blocker
Category: Contract

Target:
epoch-4-sample-plugin.md / разделы «Проект» (строки 16–40) и «Файлы» (строки 46–58)

Claim:
`samples/RoslynMcpPlugin/` появляется как отдельный проект в `RoslynMcpServer.sln`, из хостового проекта меняется только `RoslynMcpServer.Tests.csproj` (ссылка без загрузки сборки); сам `RoslynMcpServer.csproj` в этой эпохе не правится.

Evidence:
`RoslynMcpServer.csproj` — SDK-style в корне репозитория и исключает вложенные проекты **явно**: `<Compile Remove="RoslynMcpServer.Tests\**" />`, `<Compile Remove="RoslynMcpServer.LifecycleTestHost\**" />` (строки 52–57). Исключения для `samples\**` нет, а эпоха 7 прямо запрещает менять csproj («Код загрузчика, шаблон … и csproj не менять»). Глоб SDK — `Compile Include="**/*.cs" Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)"` (`Microsoft.NET.Sdk.DefaultItems.props`, SDK 10.0.204), и `$(DefaultItemExcludesInProjectFolder)` не содержит шаблонов `**/bin/**`/`**/obj/**`. Проверено на этом дереве: `dotnet msbuild RoslynMcpServer.csproj -getItem:Compile` с временным `samples/RoslynMcpPlugin/Probe.cs` вернул `"Identity": "samples\\RoslynMcpPlugin\\Probe.cs"`; с `samples/RoslynMcpPlugin/obj/Gen.cs` и `samples/RoslynMcpPlugin/bin/Out.cs` — те же два пути в `@(Compile)`. Дальше в рантайме: `McpToolCatalog.DiscoverAttributedTools()` сканирует `typeof(WorkspaceTools).Assembly` и вернёт `sample_loaded_workspace`, а `McpToolCatalog.ValidateCatalogConsistency` бросает `InvalidOperationException("Tool catalog is missing attributed tools: sample_loaded_workspace")` из статического инициализатора `McpToolCatalog.All`.

Failure scenario:
1. Исполнитель создаёт `samples/RoslynMcpPlugin/SamplePlugin.cs` и `SampleTools.cs`, добавляет проект в `.sln` и правит только `RoslynMcpServer.Tests.csproj` — ровно по списку файлов эпохи 4.
2. `dotnet build RoslynMcpServer.sln` компилирует эти два файла и в `RoslynMcpPlugin.dll`, и в `RoslynMcpServer.dll`; на повторной сборке в `@(Compile)` хоста добавляются ещё и `samples/RoslynMcpPlugin/obj/Debug/net10.0/RoslynMcpPlugin.AssemblyInfo.cs` / `GlobalUsings.g.cs` → CS0579 (duplicate assembly attribute).
3. Даже если сборка прошла, первый же доступ к `McpToolCatalog.All` (любой `AddRoslynMcpServerTools`, любая приёмка) падает `TypeInitializationException`, то есть эпохи 5–7 не могут стать зелёными.

Suggested change:
В эпоху 4 добавить в `RoslynMcpServer.csproj` `<Compile Remove="samples\**" />` и `<None Remove="samples\**" />` рядом с существующими Remove (либо разместить шаблон вне каталога хостового проекта); в эпохе 7 снять запрет «csproj не менять» или назвать это исключение явно.

Confidence:
High
