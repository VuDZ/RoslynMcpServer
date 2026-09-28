ID: E4-01
Severity: Blocker
Category: Contract

Target:
[epoch-4-sample-plugin.md](../v1/epoch-4-sample-plugin.md), строки 7–14, 28–29, 46–59; ограничения общих файлов [README](../v1/README.md):12–15.

Claim:
Добавление samples/RoslynMcpPlugin не включает его в хост: ProjectReference из host csproj отсутствует, каталог встроенных тулов не меняется, хост ещё не подхватывает шаблон.

Evidence:
Корневой [RoslynMcpServer.csproj](../../../../../RoslynMcpServer.csproj):52–57 исключает из Compile только Tests и LifecycleTestHost. MSBuild evaluation подтверждает `EnableDefaultCompileItems=true` и отсутствие исключения samples; SDK включает `**/*.cs`. Поэтому SampleTools.cs окажется в RoslynMcpServer.dll независимо от ProjectReference. [McpToolCatalog.cs](../../../../../Hosting/McpToolCatalog.cs):229, 365–389, 400–425 проверяет все атрибутированные методы сборки хоста и отвергает имя, которого нет в каталоге. При этом эпоха 4 не разрешает изменение корневого csproj, а эпоха 6 разрешает в нём только три свойства версии.

Failure scenario:
1. Исполнитель создаёт два .cs-файла шаблона в samples по плану, не меняя host csproj.
2. Сборка хоста включает SampleTools и его `[McpServerTool(Name="sample_loaded_workspace")]`; последующие build также рискуют захватить generated .cs из вложенного obj.
3. Инициализация McpToolCatalog обнаруживает лишний атрибутированный тул и выбрасывает `Tool catalog is missing attributed tools: sample_loaded_workspace`. Серия не достигает зелёного host startup, даже когда plugins вообще не подключены.

Suggested change:
Разрешить эпохе 4 исключить дерево samples из host Compile и из нежелательных publish items, согласовав это с перечнем общих файлов. Приёмка должна доказать отсутствие SampleTools в сборке хоста и успешную проверку каталога после чистой и повторной сборки решения. Добавлять sample в McpToolCatalog нельзя: это нарушило бы основную границу плагина.

Confidence:
High
