# Эпоха 4. Шаблон

Статус: не начата.

## Цель

В дереве появляется `samples/RoslynMcpPlugin` — тот проект, который копируют
коллеги и который следующие эпохи грузят. Эвристики нет. Хост этот проект при
старте ещё не подхватывает.

Текст классов и `plugin.json` совпадает с [authoring.md](authoring.md), плюс
`using RoslynMcpServer.Plugins`. Пространство имён шаблона — `RoslynMcpPlugin`.
Тул `sample_loaded_workspace` возвращает `SolutionManager.GetLoadedWorkspacePath()`
или строку `No workspace is loaded.`, если путь пустой.

## Проект

Class library `net10.0`, не self-contained. Ссылка на `RoslynMcpServer.csproj`:
`Private=false`, `ExcludeAssets=runtime`. Пакет `ModelContextProtocol` той же
версии, что у хоста (`1.3.0` на этих исходниках): `PrivateAssets=all`,
`ExcludeAssets=runtime`. `plugin.json` копируется в выход
(`CopyToOutputDirectory=PreserveNewest`).

`minHostVersion` в манифесте шаблона — текущая `Version` хоста (`1.4.15`, пока
эпоха 6 её не сдвинула). `id` — `sample`, `entry` — `RoslynMcpPlugin.dll`,
`pluginType` — `RoslynMcpPlugin.SamplePlugin`, `toolPrefix` — `sample_`.

Проект добавляется в `RoslynMcpServer.sln`, конфигурации Debug и Release,
Any CPU. В хостовый csproj ссылкой не входит и в publish сервера не копируется.

Тестовый проект ссылается на шаблон так, чтобы `dotnet test` собирал шаблон, но
не загружал его сборку в контекст тестового процесса:

```xml
<ProjectReference Include="..\samples\RoslynMcpPlugin\RoslynMcpPlugin.csproj">
  <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
</ProjectReference>
```

Выход шаблона тесты эпохи 5 ищут сами: от `AppContext.BaseDirectory` тестовой
сборки каталог конфигурации — имя родителя (`bin/Debug/net10.0` → `Debug`),
корень репозитория — каталог вверх, в котором лежит `RoslynMcpServer.sln`,
дальше `samples/RoslynMcpPlugin/bin/{configuration}/net10.0/`. Свой `OutputPath`
шаблону не задавать.

## Файлы

- `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`
- `samples/RoslynMcpPlugin/plugin.json`
- `samples/RoslynMcpPlugin/SamplePlugin.cs`
- `samples/RoslynMcpPlugin/SampleTools.cs`
- `RoslynMcpServer.sln`
- `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj` — ссылка без загрузки сборки
- `RoslynMcpServer.Tests/SamplePluginShapeTests.cs` — манифест и наличие выхода
  после сборки решения. Сборку в тесте отдельным процессом `dotnet build` не
  запускать: её уже даёт ссылка проекта.

Версию хоста не менять. `authoring.md` не переписывать: недостающий `using`
допишет эпоха 7. В таблице эпох — только строка эпохи 4.

## Тесты

- В выходе шаблона рядом лежат `RoslynMcpPlugin.dll`, `RoslynMcpPlugin.deps.json`
  и `plugin.json`.
- `plugin.json` из выхода читается правилами эпохи 2 и даёт план с id `sample`,
  префиксом `sample_` и типом `RoslynMcpPlugin.SamplePlugin`.
- Рядом с DLL нет `RoslynMcpServer.dll` и нет `ModelContextProtocol.dll`.

## Вне эпохи

`AssemblyLoadContext`, вызов `Register` на загруженной копии, правка `Program.cs`,
раздел Plugins в продуктовом README.
