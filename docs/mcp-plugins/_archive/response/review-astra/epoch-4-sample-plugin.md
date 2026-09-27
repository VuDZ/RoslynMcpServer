# Response: review-astra/epoch-4-sample-plugin.md

## review-astra/E4-01

### Reviewer claim

`samples/RoslynMcpPlugin/*.cs` попадут в компиляцию хоста: SDK глобит `**/*.cs`, csproj исключает только Tests и LifecycleTestHost. Каталог тулов упадёт на `sample_loaded_workspace`. Эпоха 4 запрещает править хостовый csproj.

### Related findings

Duplicate of `review-ds/E4-01`. У ds дополнительно: после сборки шаблона в `Compile` хоста попадают ещё и generated `.cs` из `obj` шаблона (CS0579).

### Problem assessment

`ACCEPT`

### Problem rationale

`RoslynMcpServer.csproj` исключает только `RoslynMcpServer.Tests\**` и `RoslynMcpServer.LifecycleTestHost\**`. `EnableDefaultCompileItems` для SDK-проекта включает исходники под корнем проекта. Каталог `samples/` лежит внутри этого корня. `McpToolCatalog.DiscoverAttributedTools` сканирует сборку хоста и `Validate` бросает, если атрибут не в каталоге. Эпоха 4 не разрешает менять хостовый csproj. Эпоха 6 разрешает в нём только три свойства версии. Эпоха 7 запрещает менять csproj. Серия в этих границах не собирается.

### Correction assessment

`ACCEPT`

### Correction rationale

Эпоха 4 получает право добавить `Compile Remove` и `None Remove` для `samples\**` рядом с уже существующими исключениями. Шаблон в `McpToolCatalog` не добавлять. Приёмка: `SampleTools` нет в сборке хоста, `Validate` проходит на чистой и повторной сборке (повторная ловит generated-файлы из `obj`, это часть сценария ds). Эпоха 7 больше не говорит «csproj не менять» без изъятия этого исключения.

Вынести шаблон за пределы каталога хостового csproj тоже закрыло бы глоб. Оставляем `samples/` под репозиторием и явное исключение: так путь совпадает с уже написанным руководством.

### Evidence

`RoslynMcpServer.csproj`, ItemGroup `Compile Remove`. `Hosting/McpToolCatalog.cs`, `DiscoverAttributedTools` и `Validate`. Эпохи 4, 6, 7, списки файлов. README серии: файлы между эпохами не общие, кроме `plugin.json` и строки таблицы. Проба ds: `dotnet msbuild -getItem:Compile` увидел `samples\...\Probe.cs`.

### Classification

`ARCHITECTURE DEFECT`

### Severity

Blocker у обоих ревьюеров. Совпадает.

### Required outcome

Исходники шаблона не входят в сборку хоста. Каталог встроенных тулов на них не смотрит. Это разрешено эпохе 4, а не отложено.

### Proposed specification change

Эпоха 4: пункт файлов — `RoslynMcpServer.csproj`, только `Remove` для `samples\**`. Тест или проверка приёмки на отсутствие типа в сборке хоста. README серии: третий общий файл — хостовый csproj, эпоха 4 только эти `Remove`. Эпоха 7: запрет не покрывает уже внесённое исключение.

### Downstream consequences

Сборка хоста и publish не содержат шаблон. Тестовый `ProjectReference` с `ReferenceOutputAssembly=false` остаётся.
