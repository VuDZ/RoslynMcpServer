# Эпоха 4. Шаблон

Статус: сделана.

## Цель

В дереве появляется `samples/RoslynMcpPlugin` — тот проект, который копируют
коллеги и который следующие эпохи грузят. Эвристики нет. Хост этот проект при
старте ещё не подхватывает.

Текст классов совпадает с [authoring.md](authoring.md), плюс
`using RoslynMcpServer.Plugins`. Пространство имён шаблона — `RoslynMcpPlugin`.
Поля `plugin.json` кроме `minHostVersion` совпадают с руководством.
В исходном манифесте `minHostVersion` — `1.0`: это значение в выход не попадает.
Тул `sample_loaded_workspace` возвращает `SolutionManager.GetLoadedWorkspacePath()`
или строку `No workspace is loaded.`, если путь пустой.

Шаблон вызывает код частного managed-пакета, которого нет в publish хоста.
Сборка копирует эту зависимость в выход и не копирует сборки контракта хоста.
Какое свойство SDK включает копирование, эпоха не нормирует.

## Проект

Class library `net10.0`, не self-contained. Ссылка на `RoslynMcpServer.csproj`:
`Private=false`, `ExcludeAssets=runtime`. Пакет `ModelContextProtocol` той же
`Major.Minor`, что у хоста (`1.3.0` на этих исходниках): `PrivateAssets=all`,
`ExcludeAssets=runtime`. `plugin.json` попадает в выход рядом с entry.

`minHostVersion` в выходной копии — `AssemblyVersion` разрешённой ссылки на
`RoslynMcpServer`. Число из исходного JSON, включая `1.0`, в выход не
доживает. Как цель MSBuild читает версию ссылки и подставляет её, эпоха не
нормирует. Остальные поля исходного манифеста копируются как есть: `id` —
`sample`, `entry` — `RoslynMcpPlugin.dll`, `pluginType` —
`RoslynMcpPlugin.SamplePlugin`, `toolPrefix` — `sample_`.

Проект добавляется в `RoslynMcpServer.sln`, конфигурации Debug и Release,
Any CPU. В хостовый csproj ссылкой не входит и в publish сервера не копируется.

Хостовый csproj исключает `samples\**` из `Compile` и `None`, включая generated
`.cs` под `obj` шаблона. Эпоха 4 правит хостовый csproj только этим исключением.
Переносить шаблон в другой каталог нельзя. Эпоха 7 исключение не снимает.

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
- `RoslynMcpServer.csproj` — только исключение `samples\**` из `Compile` и `None`
- `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj` — ссылка без загрузки сборки
- `RoslynMcpServer.Tests/SamplePluginShapeTests.cs` — манифест и наличие выхода
  после сборки решения. Сборку в тесте отдельным процессом `dotnet build` не
  запускать: её уже даёт ссылка проекта.

Версию хоста не менять. `authoring.md` не переписывать: недостающий `using`
допишет эпоха 7. В таблице эпох — только строка эпохи 4.

## Тесты

- В выходе шаблона рядом лежат `RoslynMcpPlugin.dll`, `RoslynMcpPlugin.deps.json`
  и `plugin.json`.
- В выходе есть DLL частного managed-пакета, которого нет в publish хоста.
  Код этого пакета шаблон вызывает. `RoslynMcpServer.dll` и
  `ModelContextProtocol.dll` рядом отсутствуют.
- `plugin.json` из выхода читается правилами эпохи 2 и даёт план с id `sample`,
  префиксом `sample_` и типом `RoslynMcpPlugin.SamplePlugin`.
  `minHostVersion` в этом файле равна `AssemblyVersion` ссылки на
  `RoslynMcpServer` и не равна `1.0` из исходника.
- После чистой сборки и повторной сборки типа шаблона нет в сборке хоста.
  `McpToolCatalog` не падает и не содержит `sample_loaded_workspace`.

`Assembly.Location` частной DLL в каталоге теневой копии проверяет эпоха 5,
когда эта фикстура загружается. Без загрузки эпохе 4 достаточно файла в выходе.

## Вне эпохи

`AssemblyLoadContext`, вызов `Register` на загруженной копии, правка `Program.cs`,
раздел Plugins в продуктовом README.
