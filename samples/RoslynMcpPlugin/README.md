# Roslyn MCP plugin development and setup

<a id="english-version"></a>

[🇷🇺 Читать на русском](#russian-version)

A plugin extends RoslynMcpServer with its own MCP tools. It runs inside the
server process, receives the shared `SolutionManager`, and uses the Roslyn
solution that is already loaded. It does not need a separate `MSBuildWorkspace`.
Plugin tools are available in the first `tools/list` response after startup.

This guide matches server **1.4.18** (`AssemblyVersion`: `1.4.18.0`).
The working sample is in this directory: its `sample_loaded_workspace` tool
returns the loaded workspace path or `No workspace is loaded.`.
This is the main guide for plugin users and authors. It lives beside the sample,
independently of the archived development plans.

## Quick start

You need .NET SDK 10 and the RoslynMcpServer source tree to build the sample.
Run this command from the repository root:

```powershell
dotnet build samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj -c Debug
```

In your MCP server launch settings, set the `ROSLYN_MCP_PLUGINS` environment
variable to the absolute path of
`samples/RoslynMcpPlugin/bin/Debug/net10.0`. The variable must be available
to the server process itself. For example, this MCP client configuration fragment:

```json
"env": {
  "ROSLYN_MCP_PLUGINS": "C:/src/RoslynMcpServer/samples/RoslynMcpPlugin/bin/Debug/net10.0"
}
```

Restart the MCP server. Call `get_mcp_server_info`: the response should list
the `sample` plugin and its `sample_loaded_workspace` tool. After
`load_workspace`, calling that tool returns the workspace path.
If the plugin is missing, see [Diagnostics](#diagnostics).

## Sample files and build output

```text
samples/RoslynMcpPlugin/
  README.md
  RoslynMcpPlugin.csproj
  plugin.json
  SamplePlugin.cs
  SampleTools.cs
```

A plugin is a `net10.0` class library without its own runtime.
The server is published self-contained, as separate files rather than
a single-file executable.

After a build, `bin/Debug/net10.0/` contains `RoslynMcpPlugin.dll`,
`RoslynMcpPlugin.pdb`, `RoslynMcpPlugin.deps.json`, the generated `plugin.json`,
and private managed dependencies, including `Newtonsoft.Json.dll`.
Distribute the entire build output directory. Host contract assemblies
are excluded from it.

## Project settings

The complete [RoslynMcpPlugin.csproj](RoslynMcpPlugin.csproj) follows,
including its original comments.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- A plugin is a class library the host loads from a directory of its own, so its private
         managed dependencies have to lie next to the entry assembly. The SDK default for a class
         library does not copy the assemblies a PackageReference resolves: without this property the
         private dependency stays in the NuGet cache and the loaded plugin cannot resolve it. -->
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <!-- The host checks the contract versions a plugin was compiled against, and the carrier of those
         versions is the compile record of deps.json: the record of a library is the compile asset whose
         file name is the assembly. The contract assemblies are compile-only references here, so neither
         part comes from the runtime graph of the project. Without the compilation context the file lists
         no reference for RoslynMcpServer at all; with it, the trimming of libraries that have no runtime
         asset empties every contract record of its assets and leaves only the library key behind. Both
         are off for that reason. The references themselves are not to be shipped, so the refs folder the
         compilation context would otherwise fill stays off. -->
    <PreserveCompilationContext>true</PreserveCompilationContext>
    <PreserveCompilationReferences>false</PreserveCompilationReferences>
    <TrimDepsJsonLibrariesWithoutAssets>false</TrimDepsJsonLibrariesWithoutAssets>
  </PropertyGroup>

  <ItemGroup>
    <!-- Compile against the host and the MCP SDK, never carry either into the output: the running
         host shares its already loaded assemblies with the plugin, so SolutionManager is one type
         in both. Major.Minor of the SDK must equal the loaded host one. -->
    <ProjectReference Include="..\..\RoslynMcpServer.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <PackageReference Include="ModelContextProtocol" Version="1.3.0">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <!-- Private managed dependency of the plugin: no host assembly backs it, so it travels in the
         plugin directory and the plugin resolves it from there. -->
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <!-- The source plugin.json and the file next to the entry assembly differ: the delivered one must
       state the host version the plugin was actually compiled against, so it cannot drift from the
       resolved reference. The version is read from the reference rather than written by hand here,
       and the two escapes below stand for a quote and a line break, which an attribute cannot hold. -->
  <Target Name="WritePluginManifest" AfterTargets="CopyFilesToOutputDirectory">
    <GetAssemblyIdentity AssemblyFiles="@(ReferencePath)" Condition="'%(Filename)' == 'RoslynMcpServer'">
      <Output TaskParameter="Assemblies" ItemName="HostAssemblyIdentity" />
    </GetAssemblyIdentity>

    <!-- Without the host reference there is no version to state, and a manifest that carries none
         would be discovered as broken only at host startup. -->
    <Error Condition="'@(HostAssemblyIdentity)' == ''"
           Text="No resolved reference to RoslynMcpServer: plugin.json cannot state minHostVersion." />

    <PropertyGroup>
      <PluginManifestText>{ %22id%22: %22sample%22,%0A  %22entry%22: %22RoslynMcpPlugin.dll%22,%0A  %22pluginType%22: %22RoslynMcpPlugin.SamplePlugin%22,%0A  %22toolPrefix%22: %22sample_%22,%0A  %22minHostVersion%22: %22%(HostAssemblyIdentity.Version)%22%0A}</PluginManifestText>
    </PropertyGroup>

    <WriteLinesToFile File="$(TargetDir)plugin.json"
                      Lines="$(PluginManifestText)"
                      Overwrite="true"
                      WriteOnlyWhenDifferent="true" />
  </Target>

  <ItemGroup>
    <!-- Never copy the source file over the manifest the target writes. PreserveNewest would put the
         source minHostVersion back whenever that file is newer than the output. -->
    <None Update="plugin.json" CopyToOutputDirectory="Never" />
  </ItemGroup>
</Project>
```

These settings ensure that the output directory contains the required files:

- `CopyLocalLockFileAssemblies=true` copies the plugin's private managed
  dependencies from NuGet packages. The sample uses `Newtonsoft.Json` `13.0.4`.
- `PreserveCompilationContext=true` keeps compile reference records in
  `deps.json`. The server needs them to check compatibility, including
  assemblies excluded from runtime dependencies.
- `PreserveCompilationReferences=false` prevents reference assemblies
  from being copied to a `refs` directory.
- `TrimDepsJsonLibrariesWithoutAssets=false` keeps library records that
  have no runtime assets. Without the required `compile` records, the server
  cannot check versions and skips the plugin.
- `Private=false` and `ExcludeAssets=runtime` prevent host contract assemblies
  from being copied. At runtime, the plugin uses the server's assemblies.
- The `WritePluginManifest` target generates the output `plugin.json`, taking
  `minHostVersion` from the `AssemblyVersion` of the resolved reference to
  `RoslynMcpServer`. You do not need to update this version manually.

If you develop a plugin without the server source tree, replace
`ProjectReference` with a `Reference` whose `HintPath` points to
**`RoslynMcpServer.dll` from the publish directory**, and set `Private=false`.
The `RoslynMcpServer.exe` in a self-contained publication is a native apphost;
it cannot serve as a compiler reference.
Add the MCP and Roslyn references your code needs, following the compatibility
rules below and excluding their runtime files from the plugin distribution.
Keep the `deps.json` settings and the manifest generation target.

## Manifest and compatibility

The server reads `plugin.json` beside the plugin's entry DLL. It must be plain
UTF-8 JSON: comments and trailing commas are not allowed. All five fields
are required and must contain nonempty strings.

For server 1.4.18, the generated manifest looks like this
(the build writes the full `AssemblyVersion`, `1.4.18.0`):

```json
{
  "id": "sample",
  "entry": "RoslynMcpPlugin.dll",
  "pluginType": "RoslynMcpPlugin.SamplePlugin",
  "toolPrefix": "sample_",
  "minHostVersion": "1.4.18.0"
}
```

- `id` identifies the plugin. `IRoslynMcpPlugin.Name` must match it.
  This identifier appears in `get_mcp_server_info`.
- `entry` is the entry DLL file name, without a directory path. The server
  does not scan neighboring DLLs for another plugin.
- `pluginType` is the fully qualified name of the class implementing
  `IRoslynMcpPlugin`. The server requests this specific type through
  `Assembly.GetType`. It must have a public parameterless constructor.
- `toolPrefix` is the required prefix for every tool name in the plugin.
- `minHostVersion` is the minimum server version: the `Major` version must
  match, and the server must not be older than the specified version.

The source [plugin.json](plugin.json) contains the placeholder version `1.0`.
It is not copied to the output directory: `WritePluginManifest` generates
the manifest instead. When creating your own plugin, update `id`, `entry`,
`pluginType`, and `toolPrefix` consistently in both the source manifest and
the project file's `PluginManifestText`, along with the class, assembly,
and tool names in the code. Editing only the source `plugin.json`
does not change the generated file.

Compatibility is checked **along two independent axes before the entry DLL
is loaded**. Besides `minHostVersion`, the server checks compile reference
versions recorded in the adjacent `RoslynMcpPlugin.deps.json`.
There are no separate manifest fields for these versions.

- `ModelContextProtocol` must have the same `Major.Minor` as the server's
  assembly. Matching `Patch` versions are not required. The current sample
  uses package `1.3.0`.
- The `RoslynMcpServer` reference must have the same `Major` and must not
  be newer than the running server.
- `Microsoft.CodeAnalysis` must have the same `Major`, and its full reference
  version must not be newer than the server's assembly. For example, references
  to `5.10` and `5.9.2` are incompatible with a server using `5.9.0`, while `5.8`
  passes this check. A different major version is rejected in either direction.

The product version alone therefore cannot establish MCP and Roslyn type
compatibility. There is no separate version check for `Microsoft.Extensions.*`;
incompatibility in that family can surface at runtime.

## Registering tools

[SamplePlugin.cs](SamplePlugin.cs) registers the tool class:

```csharp
using RoslynMcpServer.Plugins;

namespace RoslynMcpPlugin;

public sealed class SamplePlugin : IRoslynMcpPlugin
{
    public string Name => "sample";

    public void Register(RoslynMcpPluginContext context)
    {
        context.AddToolsFrom<SampleTools>();
    }
}
```

[SampleTools.cs](SampleTools.cs) receives the shared `SolutionManager` through
its constructor. When returning a nonempty path, it calls `Newtonsoft.Json`
to demonstrate a private dependency:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RoslynMcpServer.Services;

namespace RoslynMcpPlugin;

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

        // Newtonsoft.Json is a private managed dependency of the plugin: the host shares only the
        // assemblies of its own contract, so this call is served by the Newtonsoft.Json.dll that lies
        // next to the entry assembly. The call is made for its side of the dependency contract, not for
        // its result, so the tool keeps returning the path itself; a JSON tree of a string property
        // round-trips to that same string.
        return string.IsNullOrEmpty(path)
            ? "No workspace is loaded."
            : JToken.FromObject(path).ToString();
    }
}
```

`Register` runs before `host.Build()`. You can add your own services with
`context.Services.AddSingleton<YourService>()` before calling
`context.AddToolsFrom<YourTools>()`; this requires
`using Microsoft.Extensions.DependencyInjection`.
The server creates a tool class instance for each invocation, resolving
`SolutionManager`, `ILogger<T>`, and plugin services through its shared DI
container. The constructor is also called during registration validation:
keep long-running operations and external side effects out of it.

A name without the required prefix, a built-in tool name, or an already
registered name is rejected individually; other valid tools still register.
An exception from `Register` or failure to construct a tool class rejects
the entire plugin and rolls back the registrations it added.
Other plugins and built-in tools continue to work.

The `lite` and `full` profiles do not filter plugin tools. These tools are
not included in `McpToolCatalog` or `list_tool_groups`. `get_tool_help` uses
the registered method's `[Description]` and parameters.

## Working with the solution and server services

For semantic analysis consistent with the built-in tools, use
**`SolutionManager.GetSanitizedPublishedSolutionAsync`**. It applies queued
file changes and returns a solution snapshot without
`UnresolvedAnalyzerReference` placeholders, leaving the published snapshot
unchanged. The result may be `null` when no solution is loaded.

Two other methods serve different purposes:

- `GetPublishedSolutionAsync` returns the published snapshot without applying
  the file watcher's pending changes.
- `GetPublishedSolutionAfterDiskSyncAsync` applies pending changes but returns
  the raw published snapshot, retaining analyzer placeholders.

Use `FindDocumentAsync`, `GetLoadedWorkspacePath`, and
`GetLoadedWorkspaceDirectory` to locate documents and workspace paths.
Resolve relative paths through `ResolvePathAgainstWorkspace`.
Then use regular Roslyn APIs such as `Document`, `SemanticModel`, and
`SymbolFinder`. The `CallGraphHelper`, `TestDiscoveryHelper`, and
`GitChangedFilesHelper` helpers return structured results.

Edit and save documents through public `SolutionManager` methods, including
`UpdateDocumentInMemoryAsync` and `ApplySolutionChangesToDiskAsync`.
Internal write mechanisms, analyzer publication, and helper types marked
`internal` are not part of the plugin API. `InternalsVisibleTo` access
is not provided for arbitrary plugins.

Built-in tool methods such as `TestTools.RunDotNetTest` and
`NavigationTools.FindSymbolReferences` are intended to respond to the agent,
not to be called by plugins. Use public services and Roslyn directly.
For CLI execution, the available APIs include `DotNetCliRunner` (timeouts
and separate stdout/stderr), `DotNetBuildArguments`, `DotNetTestArguments`,
`DotNetBuildProbe`, and `VstestOutputParser`.
The `run_dotnet_test` workflow lives inside `TestTools`; there is currently
no separate service returning a structured result for the entire test run.
A plugin can return a test list to the agent or build its own command
and execute it through `DotNetCliRunner`.

## Dependency resolution

The loader uses three stages:

1. Host contract assemblies — `RoslynMcpServer`, `ModelContextProtocol`
   and its family, `Microsoft.CodeAnalysis.*`, and `Microsoft.Extensions.*` —
   come from the server's context. Already loaded instances are reused;
   files with the same names in the plugin directory are not opened for them.
2. Private managed dependencies are resolved by `AssemblyDependencyResolver`
   using the plugin's `deps.json` and loaded from its files.
3. .NET framework assemblies are resolved through the default load context.
   A resolver miss at the second stage for such an assembly does not by itself
   reject the plugin.

If a private managed dependency is unavailable, the plugin is skipped:
a server assembly with the same name does not replace a missing DLL from
the plugin distribution. The loader checks the entry DLL's references
before registration. When using a shadow copy, `Assembly.Location` for
a private dependency points to that copy's directory.

Native dependency resolution is unsupported in the first implementation (v1):
the loader does not override `LoadUnmanagedDll`. Having files under `runtimes/`
and copying them does not guarantee that P/Invoke will work.

## Connecting a built plugin

To install beside a published server, copy the entire build output
to a `plugins/<id>/` subdirectory:

```text
{BaseDirectory}/plugins/sample/
  plugin.json
  RoslynMcpPlugin.dll
  RoslynMcpPlugin.pdb
  RoslynMcpPlugin.deps.json
  Newtonsoft.Json.dll
```

This is a drop-in installation; it needs no `plugins` setting.
The immediate subdirectory name must exactly match the manifest's `id`.
The plugin loads directly from that location, without a shadow copy.
Stop the server process using these files before replacing them, then restart it.

During development, it is more convenient to connect the build output directly:

- Use `ROSLYN_MCP_PLUGINS` with one or more paths separated by `;` on Windows
  or `:` on Linux/macOS.
- Alternatively, use the `plugins` array in `RoslynMcp.jsonc`:

```json
{
  "plugins": [
    "C:/src/RoslynMcpServer/samples/RoslynMcpPlugin/bin/Debug/net10.0"
  ]
}
```

Settings are read beside the server and from the process working directory;
the working directory's value takes precedence for the same key.
See [RoslynMcp.jsonc.sample](../../RoslynMcp.jsonc.sample).
You can specify a directory containing the manifest or the entry DLL path.
For a DLL path, the file name must match `entry` in the adjacent manifest.
A development directory name such as `net10.0` does not have to match `id`.
Use absolute paths: relative paths are resolved against the process working
directory, which may differ from the settings file's directory.

Sources are processed in this order: drop-in directories, the `plugins` array,
then the environment variable. The first source that passes validation claims
the `id`; a repeated identifier is skipped with a reason.
The server does not search for plugins by scanning arbitrary locations
in the working directory or user profile.

For paths from settings or the environment variable, the server creates
a shadow copy under `{Temp}/RoslynMcpServer/plugins/<unique directory>/`
and loads the DLL from there. It copies PDBs, the manifest, `deps.json`,
private dependencies, and other files; host contract assemblies and their
PDBs are excluded. Symbolic links and other reparse points cause the plugin
to be rejected. Automatic cleanup of old copies is not implemented.

## Updating and debugging

When developing with a shadow copy, use this cycle:

1. Edit and rebuild the plugin. The running server holds its own copy,
   leaving the original `bin` directory available for overwriting.
2. Restart the MCP server in the client. The new process creates a fresh
   copy and loads the updated DLL.
3. Call `load_workspace` again: the solution state belonged to the previous
   process.

Disabling MCP in the client does not always terminate the old process.
When connected through a development path, that process continues using
its own copy and does not block a new build in `bin`. Replacing the loaded
DLL without restarting the server is unsupported.

To debug, launch the **server**, for example with
`dotnet run --project RoslynMcpServer.csproj`, or from its build directory
with PDBs. Point `ROSLYN_MCP_PLUGINS` at the plugin's output directory
and attach the debugger to the process running RoslynMcpServer
(account for how the process is launched when using `dotnet`).
Use a compatible server build, preferably the same one the plugin references.

The plugin's PDB is copied alongside its DLL. The debugger uses symbols
to map the loaded copy to source files. After making changes, repeat the
build, MCP restart, and `load_workspace` cycle. This debugging setup needs
server symbols; a published executable without PDBs is insufficient.
When finished, restore the MCP client's command to the regular publication.

## Diagnostics

`get_mcp_server_info` shows each plugin's identifier, the DLL file actually
opened, and its accepted tool names. For a development path, this is
**the shadow copy path**. Server 1.4.18 uses these line formats:

```text
- **Plugins:** (none)
- **Plugin `{id}`:** `{EntryPath}` — tools: `sample_loaded_workspace`
- **Plugin skipped `{id or path}`:** {reason}
```

The `(none)` line appears only when there are neither loaded plugins nor
skipped sources. The other lines describe the corresponding load results;
braces here indicate substituted values. `get_tool_help` for a plugin tool
includes these lines:

```text
- Kind: plugin
- Group: `plugin`
```

Manifest errors, incompatible versions, missing dependencies, copy failures,
or registration failures cause the plugin to be skipped. The `PluginStartup`
startup step writes `[RoslynMcp] plugin skipped (<id or path>): <reason>`
to stderr. After `host.Build()`, the same line is written to `logs/mcp-*.log`
beside the server. Before `Register`, the loader itself returns the reason;
the startup step is responsible for writing it to stderr.
For registration failures, stderr also contains the registrar's
`plugin '<id>' was not loaded` message.
The registrar reports an individual tool name rejection on stderr as
`[RoslynMcp] plugin '<id>' skipped '<name>' (<method>): <reason>`;
this is not a rejection of the entire plugin and does not create a separate
`Plugin skipped` line in server info.

If you call the server info formatting API directly, older `BuildInfoMarkdown`
overloads without a plugin report do not add this section.
The `get_mcp_server_info` tool always passes a report; when no report service
is available, it passes an empty report and displays `(none)`.

## Limitations

A plugin runs with the server process's permissions; there is no sandbox.
Connect only trusted assemblies. Unloading the assembly load context,
updating code without a restart, installing plugins from NuGet or the network,
and resolving native dependencies are unsupported.
The sample demonstrates server integration; authors add their own analysis
rules and application logic in their projects.

---

<a id="russian-version"></a>

## 🇷🇺 Разработка и подключение плагинов Roslyn MCP

[🇬🇧 Back to English](#english-version)

Плагин расширяет RoslynMcpServer собственными MCP-инструментами. Он работает
в процессе сервера, получает общий `SolutionManager` и использует уже
загруженное решение Roslyn. Создавать отдельный `MSBuildWorkspace` не нужно.
Инструменты плагина доступны при первом запросе `tools/list` после запуска.

Руководство соответствует серверу **1.4.18** (`AssemblyVersion` — `1.4.18.0`).
Готовый пример находится в этом каталоге: инструмент `sample_loaded_workspace`
возвращает путь загруженного решения или сообщение `No workspace is loaded.`.
Это основное руководство для пользователей и авторов плагинов; оно хранится
рядом с примером независимо от архива планов разработки.

## Быстрый старт

Для сборки нужны .NET SDK 10 и исходники RoslynMcpServer. Из корня репозитория
выполните:

```powershell
dotnet build samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj -c Debug
```

В настройках запуска MCP-сервера укажите переменную окружения
`ROSLYN_MCP_PLUGINS` с абсолютным путём к
`samples/RoslynMcpPlugin/bin/Debug/net10.0`. Переменная должна попасть именно
в окружение процесса сервера. Например, фрагмент конфигурации MCP-клиента:

```json
"env": {
  "ROSLYN_MCP_PLUGINS": "C:/src/RoslynMcpServer/samples/RoslynMcpPlugin/bin/Debug/net10.0"
}
```

Перезапустите MCP-сервер. Вызовите `get_mcp_server_info`: в ответе должны
появиться плагин `sample` и инструмент `sample_loaded_workspace`. После
`load_workspace` вызов этого инструмента возвращает путь решения.
Если плагин отсутствует, проверьте раздел «Диагностика» ниже.

## Файлы примера и результат сборки

```text
samples/RoslynMcpPlugin/
  README.md
  RoslynMcpPlugin.csproj
  plugin.json
  SamplePlugin.cs
  SampleTools.cs
```

Плагин — библиотека классов для `net10.0`, без собственной среды выполнения.
Сервер публикуется со средой выполнения (self-contained), отдельными файлами,
а не одним исполняемым файлом (single-file).

После сборки каталог `bin/Debug/net10.0/` содержит `RoslynMcpPlugin.dll`,
`RoslynMcpPlugin.pdb`, `RoslynMcpPlugin.deps.json`, сформированный `plugin.json`
и частные управляемые зависимости, включая `Newtonsoft.Json.dll`.
Распространяйте весь каталог сборки. Сборки контракта сервера в него не входят.

## Настройки проекта

Ниже приведён полный файл [RoslynMcpPlugin.csproj](RoslynMcpPlugin.csproj).
Комментарии в исходном файле сохранены на английском языке.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- A plugin is a class library the host loads from a directory of its own, so its private
         managed dependencies have to lie next to the entry assembly. The SDK default for a class
         library does not copy the assemblies a PackageReference resolves: without this property the
         private dependency stays in the NuGet cache and the loaded plugin cannot resolve it. -->
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <!-- The host checks the contract versions a plugin was compiled against, and the carrier of those
         versions is the compile record of deps.json: the record of a library is the compile asset whose
         file name is the assembly. The contract assemblies are compile-only references here, so neither
         part comes from the runtime graph of the project. Without the compilation context the file lists
         no reference for RoslynMcpServer at all; with it, the trimming of libraries that have no runtime
         asset empties every contract record of its assets and leaves only the library key behind. Both
         are off for that reason. The references themselves are not to be shipped, so the refs folder the
         compilation context would otherwise fill stays off. -->
    <PreserveCompilationContext>true</PreserveCompilationContext>
    <PreserveCompilationReferences>false</PreserveCompilationReferences>
    <TrimDepsJsonLibrariesWithoutAssets>false</TrimDepsJsonLibrariesWithoutAssets>
  </PropertyGroup>

  <ItemGroup>
    <!-- Compile against the host and the MCP SDK, never carry either into the output: the running
         host shares its already loaded assemblies with the plugin, so SolutionManager is one type
         in both. Major.Minor of the SDK must equal the loaded host one. -->
    <ProjectReference Include="..\..\RoslynMcpServer.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <PackageReference Include="ModelContextProtocol" Version="1.3.0">
      <PrivateAssets>all</PrivateAssets>
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <!-- Private managed dependency of the plugin: no host assembly backs it, so it travels in the
         plugin directory and the plugin resolves it from there. -->
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <!-- The source plugin.json and the file next to the entry assembly differ: the delivered one must
       state the host version the plugin was actually compiled against, so it cannot drift from the
       resolved reference. The version is read from the reference rather than written by hand here,
       and the two escapes below stand for a quote and a line break, which an attribute cannot hold. -->
  <Target Name="WritePluginManifest" AfterTargets="CopyFilesToOutputDirectory">
    <GetAssemblyIdentity AssemblyFiles="@(ReferencePath)" Condition="'%(Filename)' == 'RoslynMcpServer'">
      <Output TaskParameter="Assemblies" ItemName="HostAssemblyIdentity" />
    </GetAssemblyIdentity>

    <!-- Without the host reference there is no version to state, and a manifest that carries none
         would be discovered as broken only at host startup. -->
    <Error Condition="'@(HostAssemblyIdentity)' == ''"
           Text="No resolved reference to RoslynMcpServer: plugin.json cannot state minHostVersion." />

    <PropertyGroup>
      <PluginManifestText>{ %22id%22: %22sample%22,%0A  %22entry%22: %22RoslynMcpPlugin.dll%22,%0A  %22pluginType%22: %22RoslynMcpPlugin.SamplePlugin%22,%0A  %22toolPrefix%22: %22sample_%22,%0A  %22minHostVersion%22: %22%(HostAssemblyIdentity.Version)%22%0A}</PluginManifestText>
    </PropertyGroup>

    <WriteLinesToFile File="$(TargetDir)plugin.json"
                      Lines="$(PluginManifestText)"
                      Overwrite="true"
                      WriteOnlyWhenDifferent="true" />
  </Target>

  <ItemGroup>
    <!-- Never copy the source file over the manifest the target writes. PreserveNewest would put the
         source minHostVersion back whenever that file is newer than the output. -->
    <None Update="plugin.json" CopyToOutputDirectory="Never" />
  </ItemGroup>
</Project>
```

Свойства проекта обеспечивают необходимый состав выходного каталога:

- `CopyLocalLockFileAssemblies=true` копирует частные управляемые зависимости
  плагина из пакетов NuGet. В примере это `Newtonsoft.Json` версии `13.0.4`.
- `PreserveCompilationContext=true` сохраняет сведения о ссылках компиляции
  в `deps.json`. Они нужны серверу для проверки совместимости, в том числе
  для сборок, исключённых из зависимостей времени выполнения.
- `PreserveCompilationReferences=false` отключает копирование сборок ссылок
  в каталог `refs`.
- `TrimDepsJsonLibrariesWithoutAssets=false` сохраняет записи библиотек,
  у которых нет файлов для времени выполнения. Без нужных записей `compile`
  сервер не сможет проверить версии и пропустит плагин.
- `Private=false` и `ExcludeAssets=runtime` исключают копирование сборок
  контракта. При выполнении плагин использует сборки самого сервера.
- Цель `WritePluginManifest` формирует выходной `plugin.json`, получая
  `minHostVersion` из `AssemblyVersion` фактически разрешённой ссылки на
  `RoslynMcpServer`. Вручную обновлять эту версию не нужно.

Если вы разрабатываете плагин без исходников сервера, замените
`ProjectReference` ссылкой `Reference` с `HintPath` на **`RoslynMcpServer.dll`
из каталога публикации** и `Private=false`. Исполняемый файл
`RoslynMcpServer.exe` из self-contained-публикации — нативный загрузчик
приложения; он не подходит в качестве ссылки компилятора.
Добавьте необходимые вашему коду ссылки MCP и Roslyn, соблюдая правила
совместимости ниже и исключая их файлы времени выполнения из поставки.
Сохраните свойства для `deps.json` и цель формирования манифеста.

## Манифест и совместимость

Сервер читает `plugin.json` рядом с основной DLL плагина. Это обычный JSON
в UTF-8: комментарии и завершающие запятые не допускаются. Все пять полей
обязательны и должны содержать непустые строки.

Для версии сервера 1.4.18 содержимое выходного манифеста выглядит так
(сборка записывает полную `AssemblyVersion` — `1.4.18.0`):

```json
{
  "id": "sample",
  "entry": "RoslynMcpPlugin.dll",
  "pluginType": "RoslynMcpPlugin.SamplePlugin",
  "toolPrefix": "sample_",
  "minHostVersion": "1.4.18.0"
}
```

- `id` — идентификатор плагина. Значение `IRoslynMcpPlugin.Name` должно
  совпадать с ним. Этот идентификатор отображается в `get_mcp_server_info`.
- `entry` — имя основной DLL без пути к каталогу. Сервер не перебирает
  соседние DLL в поисках другого плагина.
- `pluginType` — полное имя класса, реализующего `IRoslynMcpPlugin`.
  Сервер запрашивает именно этот тип через `Assembly.GetType`.
  Класс должен иметь открытый конструктор без параметров.
- `toolPrefix` — обязательный префикс имени каждого инструмента плагина.
- `minHostVersion` — минимальная версия сервера: старшая часть версии
  (`Major`) должна совпадать, а сервер не должен быть старее указанной версии.

Исходный [plugin.json](plugin.json) содержит демонстрационное значение `1.0`.
Он не копируется в выходной каталог: цель `WritePluginManifest` создаёт
манифест заново. При создании своего плагина согласованно измените `id`,
`entry`, `pluginType` и `toolPrefix` в исходном манифесте и в `PluginManifestText`
файла проекта, а также имена классов, сборки и инструментов в коде.
Ручная правка одного лишь исходного `plugin.json` не изменит выходной файл.

Совместимость проверяется **по двум независимым осям до загрузки основной DLL**.
Помимо `minHostVersion`, сервер проверяет версии ссылок компиляции из соседнего
`RoslynMcpPlugin.deps.json`. Отдельных полей манифеста для этих версий нет.

- У `ModelContextProtocol` должны совпадать `Major.Minor` со сборкой сервера.
  Совпадение исправляющей версии (`Patch`) не требуется. В текущем шаблоне
  используется пакет `1.3.0`.
- У ссылки на `RoslynMcpServer` должен совпадать `Major`; ссылка не должна
  быть новее работающего сервера.
- У `Microsoft.CodeAnalysis` должен совпадать `Major`; полная версия ссылки
  не должна быть новее сборки сервера. Например, ссылки `5.10` и `5.9.2`
  несовместимы с сервером на `5.9.0`, а `5.8` проходит эту проверку.
  Другая старшая версия отклоняется независимо от направления различия.

Поэтому одной версии продукта недостаточно, чтобы подтвердить совместимость
типов MCP и Roslyn. Отдельная проверка версий `Microsoft.Extensions.*`
не реализована; несовместимость этих сборок может проявиться при выполнении.

## Регистрация инструментов

[SamplePlugin.cs](SamplePlugin.cs) регистрирует класс инструментов:

```csharp
using RoslynMcpServer.Plugins;

namespace RoslynMcpPlugin;

public sealed class SamplePlugin : IRoslynMcpPlugin
{
    public string Name => "sample";

    public void Register(RoslynMcpPluginContext context)
    {
        context.AddToolsFrom<SampleTools>();
    }
}
```

[SampleTools.cs](SampleTools.cs) получает общий `SolutionManager` через
конструктор. При возврате непустого пути он вызывает `Newtonsoft.Json`,
демонстрируя использование частной зависимости:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RoslynMcpServer.Services;

namespace RoslynMcpPlugin;

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

        // Newtonsoft.Json is a private managed dependency of the plugin: the host shares only the
        // assemblies of its own contract, so this call is served by the Newtonsoft.Json.dll that lies
        // next to the entry assembly. The call is made for its side of the dependency contract, not for
        // its result, so the tool keeps returning the path itself; a JSON tree of a string property
        // round-trips to that same string.
        return string.IsNullOrEmpty(path)
            ? "No workspace is loaded."
            : JToken.FromObject(path).ToString();
    }
}
```

Метод `Register` вызывается до `host.Build()`. Собственные сервисы можно
добавить через `context.Services.AddSingleton<YourService>()` перед
`context.AddToolsFrom<YourTools>()`; для этого потребуется
`using Microsoft.Extensions.DependencyInjection`.
Сервер создаёт экземпляр класса инструментов для каждого вызова, разрешая
`SolutionManager`, `ILogger<T>` и сервисы плагина через общий контейнер DI.
Конструктор также вызывается при проверке регистрации: не помещайте в него
длительные операции или действия с внешними побочными эффектами.

Имя без нужного префикса, имя встроенного инструмента или уже занятое имя
отклоняется отдельно; остальные корректные инструменты регистрируются.
Исключение из `Register` или ошибка создания класса инструментов приводит
к отказу в регистрации всего плагина и откату добавленных им регистраций.
Другие плагины и встроенные инструменты продолжают работать.

Профили `lite` и `full` не фильтруют инструменты плагина. Они не включаются
в `McpToolCatalog` и `list_tool_groups`. Справка `get_tool_help` строится
по `[Description]` и параметрам зарегистрированного метода.

## Работа с решением и сервисами сервера

Для семантического анализа, согласованного со встроенными инструментами,
используйте **`SolutionManager.GetSanitizedPublishedSolutionAsync`**.
Он применяет накопленные изменения файлов и возвращает снимок решения без
заглушек `UnresolvedAnalyzerReference`, не заменяя опубликованный снимок.
Учитывайте, что при отсутствии загруженного решения результат может быть `null`.

Два других метода имеют иное назначение:

- `GetPublishedSolutionAsync` возвращает опубликованный снимок без применения
  очереди изменений от наблюдателя файловой системы.
- `GetPublishedSolutionAfterDiskSyncAsync` применяет очередь изменений, но
  возвращает исходный опубликованный снимок, сохраняя заглушки анализаторов.

Для поиска документов и определения путей доступны `FindDocumentAsync`,
`GetLoadedWorkspacePath` и `GetLoadedWorkspaceDirectory`.
Относительные пути разрешайте через `ResolvePathAgainstWorkspace`.
Далее работайте с обычными API Roslyn: `Document`, `SemanticModel`,
`SymbolFinder`. Вспомогательные классы `CallGraphHelper`, `TestDiscoveryHelper`
и `GitChangedFilesHelper` возвращают структурированные результаты.

Изменяйте документы и сохраняйте их через публичные методы `SolutionManager`,
в частности `UpdateDocumentInMemoryAsync` и `ApplySolutionChangesToDiskAsync`.
Внутренние механизмы записи, публикации анализаторов и вспомогательные типы
с модификатором `internal` не являются API плагина. Доступ через
`InternalsVisibleTo` для произвольных плагинов не предоставляется.

Методы встроенных инструментов, например `TestTools.RunDotNetTest` и
`NavigationTools.FindSymbolReferences`, предназначены для ответов агенту,
а не для вызова из плагина. Используйте публичные сервисы и Roslyn напрямую.
Если плагину нужно запускать CLI, доступны `DotNetCliRunner` (таймаут,
раздельные stdout/stderr), `DotNetBuildArguments`, `DotNetTestArguments`,
`DotNetBuildProbe` и `VstestOutputParser`.
Последовательность действий `run_dotnet_test` реализована внутри `TestTools`;
отдельного сервиса, возвращающего структурированный результат всего запуска
тестов, пока нет. Плагин может вернуть агенту список тестов либо самостоятельно
сформировать команду и выполнить её через `DotNetCliRunner`.

## Как разрешаются зависимости

Загрузчик использует три стадии:

1. Сборки контракта — `RoslynMcpServer`, `ModelContextProtocol` и его семейство,
   `Microsoft.CodeAnalysis.*`, `Microsoft.Extensions.*` — берутся из контекста
   сервера. Уже загруженные экземпляры используются повторно; одноимённые
   файлы в каталоге плагина для них не открываются.
2. Частные управляемые зависимости разрешаются через `AssemblyDependencyResolver`
   по `deps.json` плагина и загружаются из его файлов.
3. Сборки среды .NET разрешаются через стандартный контекст загрузки.
   Отсутствие результата на второй стадии для такой сборки само по себе
   не означает отказ в загрузке плагина.

Если частная управляемая зависимость недоступна, плагин пропускается:
одноимённая сборка сервера не заменяет отсутствующую DLL из поставки плагина.
Перед регистрацией загрузчик проверяет ссылки основной DLL.
При загрузке из теневой копии `Assembly.Location` частной зависимости
указывает на каталог этой копии.

Разрешение нативных зависимостей в первой реализации (v1) не поддерживается:
загрузчик не переопределяет `LoadUnmanagedDll`. Наличие файлов в `runtimes/`
и их копирование не гарантируют работу P/Invoke.

## Подключение готового плагина

Для установки рядом с опубликованным сервером скопируйте весь результат
сборки в подкаталог `plugins/<id>/`:

```text
{BaseDirectory}/plugins/sample/
  plugin.json
  RoslynMcpPlugin.dll
  RoslynMcpPlugin.pdb
  RoslynMcpPlugin.deps.json
  Newtonsoft.Json.dll
```

Это способ подключения drop-in: настройка `plugins` для него не требуется.
Имя непосредственного подкаталога должно точно совпадать с `id` манифеста.
Плагин загружается с указанного места без теневой копии. Перед заменой файлов
остановите использующий их процесс сервера, затем запустите его заново.

Для разработки удобнее подключать выходной каталог сборки напрямую:

- через `ROSLYN_MCP_PLUGINS` — один или несколько путей, разделённых `;`
  в Windows или `:` в Linux/macOS;
- через массив `plugins` в `RoslynMcp.jsonc`:

```json
{
  "plugins": [
    "C:/src/RoslynMcpServer/samples/RoslynMcpPlugin/bin/Debug/net10.0"
  ]
}
```

Файл настроек читается рядом с сервером и из рабочего каталога процесса;
значение ключа из рабочего каталога имеет приоритет. Пример конфигурации —
[RoslynMcp.jsonc.sample](../../RoslynMcp.jsonc.sample).
Можно указывать каталог с манифестом или путь к основной DLL. Во втором случае
имя файла должно совпадать с `entry` соседнего манифеста.
Имя каталога разработки, например `net10.0`, не обязано совпадать с `id`.
Используйте абсолютные пути: относительные разрешаются от рабочего каталога
процесса, который может отличаться от каталога настроек.

Источники обрабатываются в порядке: drop-in, массив `plugins`, переменная
окружения. Первый прошедший проверки источник занимает `id`; повторное
объявление того же идентификатора пропускается с указанием причины.
Сервер не ищет плагины произвольным обходом рабочего каталога или профиля
пользователя.

Для путей из настроек и переменной окружения сервер создаёт теневую копию
каталога под `{Temp}/RoslynMcpServer/plugins/<уникальный каталог>/` и загружает
DLL оттуда. Копируются PDB, манифест, `deps.json`, частные зависимости и
остальные файлы; сборки контракта сервера и их PDB исключаются.
Символические ссылки и другие точки повторной обработки (reparse points)
отклоняются вместе с плагином. Автоматическая очистка старых копий не реализована.

## Обновление и отладка

При разработке с теневой копией используйте следующий цикл:

1. Измените код и пересоберите плагин. Работающий сервер держит свою копию,
   поэтому исходный каталог `bin` остаётся доступным для перезаписи.
2. Перезапустите MCP-сервер в клиенте. Новый процесс создаст новую копию
   и загрузит обновлённую DLL.
3. Повторно вызовите `load_workspace`: состояние решения принадлежало
   предыдущему процессу.

Отключение MCP в клиенте не всегда завершает старый процесс. При подключении
через путь разработки он продолжает использовать собственную копию и
не блокирует новую сборку в `bin`. Подмена загруженной DLL без перезапуска
сервера не поддерживается.

Для отладки запускайте **сервер**, например командой
`dotnet run --project RoslynMcpServer.csproj`, либо из его каталога сборки
с PDB. Укажите `ROSLYN_MCP_PLUGINS` на выходной каталог плагина и подключите
отладчик к процессу, выполняющему RoslynMcpServer (при запуске через `dotnet`
учитывайте способ запуска процесса). Используйте совместимую сборку сервера,
желательно ту же, на которую ссылается проект плагина.

PDB плагина копируется вместе с DLL. Отладчик сопоставляет загруженную копию
с исходными файлами по символам. После изменений повторите цикл сборки,
перезапуска MCP и `load_workspace`. Для описанного сценария отладки нужны
символы сервера; опубликованного исполняемого файла без PDB недостаточно.
По окончании верните команду MCP-клиента на обычную публикацию сервера.

## Диагностика

`get_mcp_server_info` показывает идентификатор, фактически открытый файл DLL
и принятые имена инструментов. Для пути разработки это **путь теневой копии**.
Формат строк сервера 1.4.18:

```text
- **Plugins:** (none)
- **Plugin `{id}`:** `{EntryPath}` — tools: `sample_loaded_workspace`
- **Plugin skipped `{id или путь}`:** {причина}
```

Строка `(none)` появляется, только когда нет ни загруженных плагинов,
ни пропусков. Остальные строки выводятся для соответствующих результатов
загрузки; фигурные скобки здесь обозначают подставляемые значения.
`get_tool_help` для инструмента плагина содержит строки:

```text
- Kind: plugin
- Group: `plugin`
```

Ошибки манифеста, несовместимые версии, отсутствие зависимостей, ошибки
копирования или регистрации приводят к пропуску плагина. Этап запуска
`PluginStartup` записывает в stderr строку
`[RoslynMcp] plugin skipped (<id или путь>): <причина>`.
После `host.Build()` та же строка попадает в `logs/mcp-*.log` рядом с сервером.
Сам загрузчик до вызова `Register` возвращает причину; в stderr её выводит
именно этап запуска. При отказе регистрации stderr также содержит
сообщение `plugin '<id>' was not loaded` от регистратора.
Отказ отдельного имени инструмента регистратор сообщает в stderr строкой
`[RoslynMcp] plugin '<id>' skipped '<имя>' (<метод>): <причина>`;
это не отказ всего плагина и не отдельная строка `Plugin skipped` в info.

Если вы используете API форматирования сведений о сервере напрямую, учтите:
старые перегрузки `BuildInfoMarkdown` без отчёта плагинов не добавляют этот
раздел. Инструмент `get_mcp_server_info` всегда передаёт отчёт; если сервис
отчёта отсутствует, передаётся пустой отчёт и выводится `(none)`.

## Ограничения

Плагин выполняется с правами процесса сервера; изоляция в песочнице
не предусмотрена. Подключайте только доверенные сборки.
Не поддерживаются выгрузка контекста загрузки, обновление кода без перезапуска,
установка плагинов из NuGet или по сети и разрешение нативных зависимостей.
Пример показывает интеграцию с сервером; собственные правила анализа
и прикладную логику автор добавляет в своём проекте.
