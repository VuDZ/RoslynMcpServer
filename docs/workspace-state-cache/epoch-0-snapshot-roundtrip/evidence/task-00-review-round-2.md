# E0/task-00 — recheck, round 2

Дата: 2026-10-07 (Europe/Moscow). Итог: **accepted**.
Принятый scope: docs-only inventory текущих seams и выбор неизменённого
RoslynMcpPlugin как реального кандидата для bounded S-001.
Автор исправлений: Grok 4.7, по ответу в [round 1](task-00-review-round-1.md).
Рецензент: Codex (GPT-6; точный runtime model ID недоступен), `/root`.
Раунд: 2. Подтверждены 2 исправления; открытых findings: 0; новых: 0.

## Версия и проверка исправлений

HEAD остаётся `7ab1d8eb7e1811d9c1602845fa53acd945d720e0`.
Проверена исправленная незакоммиченная версия [inventory](task-00-controls.md)
и ответ автора; SHA256 inventory до обновления статуса:
`B0199B246F4AEF32BD86A5EC1928F9A63310271FED87F6227098B14EB716251B`.
Состав исходного scope и проверенные неизменённые seams — в round 1.

- **T00-R1-001 — closed / fixed.** Объявления PackageReference отделены от
  наблюдаемого Project.MetadataReferences. Указаны метод open, SDK, project
  path, число references, три actual DLL paths/identities и reference properties.
  Отдельный процесс автора честно отличён от MCP workspace 1.5.4.0.
  Рецензент независимо повторил ordinary open и получил те же результаты.
- **T00-R1-002 — closed / fixed.** В таблице восстановлены repository-relative
  каталоги LifecycleTestHost и Tests; оба пути существуют и соответствуют solution.

Три перечисленных DLL — ограниченный inventory, а не разрешение отбросить
остальные 223 references при capture. Запрет удаления references сохранён.
Representability всего обязательного состояния выбранного контроля и его
project dependency остаётся за task-08; здесь supported verdict не присвоен.

## Независимое ordinary наблюдение

Проверка выполнена из отдельного PowerShell 7.6.5 / .NET 10.0.11 процесса.
Roslyn assemblies взяты из существующего
`E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish` и загружены
в отдельный AssemblyLoadContext для устранения конфликта со встроенным Roslyn
PowerShell. Через публичные API зарегистрирован SDK
`C:\Program Files\dotnet\sdk\10.0.300`, создан MEF host из Workspaces и
CSharp.Workspaces, затем MSBuildWorkspace без global properties dictionary.
Вызван OpenSolutionAsync для исходного RoslynMcpServer.sln.
Private provider state не читалось; C# файлы/harness не создавались.

Наблюдаемый результат:

- ровно один проект RoslynMcpPlugin, путь и AssemblyName совпадают с inventory;
- MetadataReferences.Count = **226**, ProjectReferences = **RoslynMcpServer**;
- workspace Diagnostics = **[]**;
- ModelContextProtocol и ModelContextProtocol.Core: version **2.2.0.0**, token
  **cc7b13ffcd2ddd51**, пути из packages 2.2.0 / lib/net10.0 совпадают;
- Newtonsoft.Json: version **13.0.0.0**, token **30ad4fe6b2a6aeed**,
  путь из package 13.0.4 / lib/net6.0 совпадает;
- все три runtime reference types — Microsoft.CodeAnalysis.MetadataImageReference,
  Kind = Assembly, Aliases = [], EmbedInteropTypes = false.

Предварительные попытки не засчитаны как pass: прямой LoadFrom конфликтовал
с Roslyn PowerShell; подготовка изолированного host потребовала исключить
framework assemblies и выбрать точный overload RegisterMSBuildPath.
Затем sandbox-run дошёл до OpenSolutionAsync, но BuildHost не подключился
к локальному pipe (exit -1). Тот же готовый probe вне sandbox завершился
успешно, exit 0, и дал перечисленные результаты. Это проверка inventory,
не build/test suite, capture/hydrate или выполнение S-001.

## Validation и учёт

Проверены file links изменённых документов и diff/whitespace. Build, main suite,
SourceStructure и AnalyzerLifecycle **not-run / not required** для этого docs-only
scope. Чужие dirty C# файлы не изменялись и не принимаются этим ревью.

Статусы task-00, evidence, README эпохи/серии, execution summaries в spec и
task-08 синхронизированы. Счётчики: **2 раунда, 2 подтверждённых исправления**;
считаются отдельные findings, а не одна авторская попытка исправления.
Task-00 accepted не означает принятие плана/реализации E0, успешный S-001,
capture-ready или разрешение public activation. Остальные задачи planned.
