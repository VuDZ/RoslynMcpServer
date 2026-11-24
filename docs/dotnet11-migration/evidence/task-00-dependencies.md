# Этап 0. Пакеты, профили MSBuild и протокол BuildHost

Дата проверки — 2026-11-18. Checkout продукта — `28cdca18701e58b344826aadea5d124aab2d7760`.
Эта часть отчёта покрывает пункты 2–4 и проверку источников из пункта 7.
[Сырые данные](task-00-dependencies-raw.json) содержат SHA-256 архивов, группы зависимостей
nuspec, полный граф изолированных restore, аудит, состав host и инвентарь upstream diff.
Пакеты и zip payload исследованы в `artifacts/dotnet11-task00/candidate-feed` и
`candidate-closure`. Исходники обоих commit и патчи загружены в `candidate-source`.
Restore и аудит выполнены с SDK 10.0.402. Динамические прогоны клиента и host учитываются
отдельно в отчёте этапа.

## Roslyn 5.12.0

С официального NuGet flat container скачаны восемь стабильных пакетов. Пять прямых
пакетов — `Microsoft.CodeAnalysis.CSharp`, `CSharp.Workspaces`, `CSharp.Features`, `Features`
и `Workspaces.MSBuild` с общим префиксом `Microsoft.CodeAnalysis.`. Roslyn closure добавляет
`Common`, `Workspaces.Common` и `Scripting.Common`. Все восемь имеют версию `5.12.0`,
один source commit `1324e1d34a0490454837a22370e75bbaeecffb87` на `release/insiders` и
точные зависимости `[5.12.0, 5.12.0]` между компонентами Roslyn. Отдельный build analyzer
`Microsoft.CodeAnalysis.Analyzers` разрешён как `5.9.0`. SHA-256
`Microsoft.CodeAnalysis.Workspaces.MSBuild.5.12.0.nupkg` —
`8525f9f7a17da7ca537bcfb3cb92ebfa4c617f85e0a88ba3d1a3894325c40849`.
[Пакет на NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.12.0) ·
[Source commit](https://github.com/dotnet/roslyn/commit/1324e1d34a0490454837a22370e75bbaeecffb87)

Основной пакет содержит клиентские `lib/net10.0` и `lib/net472`. Обе группы nuspec
требуют `Microsoft.Build.Framework >= 18.7.1` и `Workspaces.Common = 5.12.0`. В группе
`net10.0` также есть `Microsoft.VisualStudio.SolutionPersistence >= 1.0.52`, `Humanizer.Core >= 2.14.1`,
`System.Composition >= 10.0.10` и пять пакетов `Microsoft.Extensions.*` с нижней границей
`10.0.10`. Остальные семь пакетов Roslyn содержат `net10.0` и `netstandard2.0`.
Отдельные TFM host подтверждены по содержимому пакета.

Изолированный restore повторял прямые `PackageReference` сервера из checkout с теми же
метаданными assets. Модель `net10.0` с прежними зависимостями 5.9.0 и модель кандидата
разрешили по 75 пакетов. Кандидат оставил `Microsoft.Build.Locator 1.11.2` и
`System.Security.Cryptography.Pkcs 10.0.13`. Версии, транзитивные пути и хеши обеих
closure находятся в [сырых данных](task-00-dependencies-raw.json). Restore модели проверяет
разрешение пакетов. Сборка сервера и runtime-сценарии требуют собственной проверки.

NuGet показывает дату публикации 2026-11-13. Пакет `Workspaces.MSBuild` не содержит
`releaseNotes` в nuspec, GitHub API для release tag `v5.12.0` вернул HTTP 404.
Сверены [заметки .NET 11 GA](https://github.com/dotnet/core/blob/main/release-notes/11.0/11.0.0/11.0.0.md),
[C# 15 в Roslyn](https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/docs/Language%20Feature%20Status.md),
[compiler breaking changes](https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/docs/compilers/CSharp/Compiler%20Breaking%20Changes%20-%20DotNet%2011.md)
и [API breaking changes](https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/docs/Breaking%20API%20Changes.md)
точной ревизии пакета. В том числе изменились safe-context коллекций Span/ReadOnlySpan,
разбор `with` и контекстные `closed`/`union`. В 5.11 поменялись флаги
`default(Microsoft.CodeAnalysis.CSharp.Conversion)`. Прямых употреблений
`default(Conversion)` и `ClassifyConversion` в C# этого checkout вне форка не найдено.
Сдвиги парсинга, диагностики и привязки проверяются сценариями C# 14/15 по матрице.

## Три профиля MSBuild

- **Compile-time сервера, тестов и клиента.** Сервер и Tests явно закрепляют `18.6.3` для
  `Microsoft.Build`, `Microsoft.Build.Framework`, `Microsoft.Build.Tasks.Core`,
  `Microsoft.Build.Utilities.Core` и `Microsoft.NET.StringTools`. Изолированный restore
  со старым набором и Roslyn 5.12.0 завершился кодом 1 с `NU1605` для Framework.
  Согласованный набор всех пяти `18.7.1` и клиентского Locator `1.11.2` восстановился
  с кодом 0. Пять архивов `18.7.1` имеют nuspec source commit
  `024f5b0379cf99b95bc8939cd92a57912528b55c`. Framework ссылается на StringTools
  `18.7.1` прямо. `project.assets.json` содержит пять `ref/net10.0/*.dll` с
  runtime placeholder `lib/net10.0/_._` и клиентский Locator `lib/net8.0` с runtime DLL.
  Рекомендация Locator о верхней границе major/minor compile-time пакета относится к
  этому профилю, где [MsBuildBootstrapper](../../../Diagnostics/MsBuildBootstrapper.cs)
  регистрирует установленный MSBuild. Пара `18.7` выше MSBuild `18.0` и `18.6` ранних SDK 10.
  Обе версии клиента имеют CLR AssemblyRef `Microsoft.Build.Framework, Version=15.1.0.0,
  PublicKeyToken=b03f5f7f11d50a3a`; из CLR identity и ошибки NuGet отдельных runtime
  результатов не следует. [Руководство Microsoft по Locator](https://learn.microsoft.com/en-us/visualstudio/msbuild/find-and-use-msbuild-versions)
- **Reference-профиль форка.** Upstream 5.12 сохраняет `_MsbuildVersion=17.11.48` и
  `PackageDownloadAndReference` для четырёх MSBuild DLL. У локального
  [проекта форка](../../../forks/roslyn-buildhost/build/BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj)
  самостоятельный `MsbuildReferenceVersion=17.11.48`, один `PackageDownload` четырёх
  пакетов и четыре прямых `Reference` с `Private=false` —
  `ref\net8.0\Microsoft.Build.dll`, `ref\net8.0\Microsoft.Build.Framework.dll`,
  `ref\net8.0\Microsoft.Build.Tasks.Core.dll` и `ref\net8.0\Microsoft.Build.Utilities.Core.dll`.
  В каждом архиве 17.11.48
  подтверждён точный путь DLL; source commit архивов — `02bf66295b64ab368d12933041f7281aad186a2d`.
  В проверенных четырёх архивах 18.7.1 этих путей нет. Новый минимум клиента
  не меняет `PackageDownload` форка. Правило compile-time пакетов Locator относится к
  18.7.1-профилю клиента. [Проект BuildHost в upstream](https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj)
- **Runtime MSBuild процесса host.** В сохранённом `NetCoreBuildHost.FindMSBuild`
  запрос Locator привязан к каталогу проекта/решения, `AbstractBuildHost` регистрирует
  обнаруженный `MSBuildPath`. Клиент 5.12 во время переключения на другой dotnet
  выставляет `DOTNET_ROOT` и `DOTNET_HOST_PATH` для выбранного SDK. Фактический MSBuild
  берётся из найденной установки SDK и фиксируется при запуске на каждой строке.
  [Выбор SDK в host](https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/BuildHost/NetCoreBuildHost.cs) ·
  [Запуск host клиентом](https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Core/MSBuild/BuildHostProcessManager.cs)

Оба скачанных архива Workspaces.MSBuild, 5.9.0 и 5.12.0, содержат
`contentFiles/any/any/BuildHost-netcore` и `BuildHost-net472`. Netcore —
framework-dependent `net8.0`, `Microsoft.NETCore.App 8.0.0`, `rollForward=Major`.
Граф `.deps.json` кандидата содержит сам host и Contracts
`5.12.0-1.26502.2`, bundled Locator `1.10.2`, без MSBuild DLL. Кандидатный
Contracts имеет assembly version `5.12.0.0`; в deps у Locator добавлен
`localPath=Microsoft.Build.Locator.dll`. Net472 — отдельный exe и config. Сами
манифесты дают основание сохранить порядок этапов до получения динамического результата,
требующего иного порядка.

### Аудит зависимостей

Вариант кандидата без прямого pin Pkcs разрешил `10.0.4` из
`Microsoft.Build.Tasks.Core 18.7.1`, с `NU1902`. С `10.0.13` это замечание
отсутствует, поэтому pin `10.0.13`, `ExcludeAssets=runtime` и `PrivateAssets=all`
сохраняются в рассматриваемом наборе. Базовая модель и модель кандидата со штатным
SQLite показали один и тот же
набор 11 High advisory ID для `System.Security.Cryptography.Xml`, версия
`10.0.3` у baseline и `10.0.4` у кандидата. В обеих моделях Xml имеет compile asset
и runtime placeholder `_._`. Корневой `Directory.Build.props` подавляет `NU1903`
в restore, поэтому для closure сохранён отдельный `dotnet list package --vulnerable
--include-transitive --format json --no-restore` с официальным audit source NuGet.
[Аудит обеих моделей и отрицательных контролей](task-00-dependencies-raw.json) ·
[CVE-2026-70162](https://github.com/advisories/GHSA-2fmv-rv2c-59x7)

### SQLite в Workspaces

PEReader показал расхождение со штатной связкой 5.9.0. Workspaces 5.12.0 имеет
AssemblyRef `SQLitePCLRaw.batteries_v2 2.1.11.2622 / 8226ea5df37bcae9` и
`SQLitePCLRaw.core 2.1.11.2622 / 1488e028ca7ab535`. Штатный пакет
`SQLite3MC.PCLRaw.bundle 2.3.5` предоставляет batteries `2.3.5.0 / df579d6b11135638`
и тянет core `3.0.2.2801 / 1488e028ca7ab535`. Кандидат с неизменённым SQLite pin
успешно восстанавливает граф. Его собранный Workspaces и данные assembly identity
показывают, что текущий pin несовместим с проверкой
`Shipped_sqlite_is_the_build_Roslyn_references`.
[Тесты identity и сохраняемого индекса](../../../RoslynMcpServer.Tests/Workspace/RoslynIndexStorageTests.cs)

**Кандидат для этапа 1** — `SQLitePCLRaw.bundle_e_sqlite3 [2.1.11]` и прямой native
pin `SQLitePCLRaw.lib.e_sqlite3 [3.53.3]`. Restore сохранил точные batteries/core
identities, provider 2.1.11 и один native пакет с `e_sqlite3.dll` для `win-x64`,
`libe_sqlite3.so` для `linux-x64`, `libe_sqlite3.dylib` для `osx-arm64`.
Полный NuGet audit оставил только тот же набор advisory для compile-only Xml.
На Windows x64 проверены 10 native exports. Управляемая smoke-проба вызвала
`Batteries_V2.Init`, загрузила SQLite 3.53.3, записала SQLite-БД в режиме WAL и
вернула значение 73 после повторного открытия файла на Windows x64 и Linux x64
под WSL с .NET 10.0.15. На Linux пакетный payload скопирован в локальный `/tmp`
перед запуском, поскольку первый запуск с `/mnt/e` не завершился. Для приёмки продукта остаются end-to-end
`RoslynIndexStorageTests`, cold/warm сохраняемого индекса и нативный macOS smoke.
[Граф и runtime evidence SQLite](task-00-sqlite-raw.json) ·
[Managed bundle 2.1.11](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.11) ·
[Native pin 3.53.3](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/3.53.3)

При проверке альтернатив выявлены следующие ограничения. `bundle_e_sqlite3 2.1.11` без
native pin и `bundle_green 2.1.11` тянут один и тот же native 2.1.11 с High
`CVE-2025-6965`. Добавление `SourceGear.sqlite3 3.50.4.5` поверх старого bundle
оставляет обе копии одноимённых native assets в графе и сохраняет это замечание.
`bundle_e_sqlcipher 2.1.11` поставляет три RID и совпадающие managed identities,
но Windows DLL сообщает SQLite 3.39.2. Отсутствие advisory для SQLCipher package ID
само по себе не подтверждает наличие SQLite-исправления. `bundle_sqlite3` и
`bundle_winsqlite3` версии 2.1.11 используют системные библиотеки без поставляемых
native assets. `bundle_e_sqlite3 2.1.12` имеет batteries/core `2.1.12.3116`,
поэтому не удовлетворяет текущему тесту точного совпадения.
[Аудит исходной модели](task-00-dependencies-raw.json) ·
[GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) ·
[История SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw/releases)

## RPC, Contracts и linked files

Сравнены исходники ревизий 5.9.0 и 5.12.0 и состав локального форка.
`Request`, `Response`, `JsonSettings`, `DocumentFileInfo` и `ProjectFileInfo` побайтно
совпадают между upstream ревизиями. Изменения RPC затрагивают контракты и время жизни объектов.

- `IBuildHost.LoadProject` раньше принимал `projectFilePath, projectContent, languageName`.
  В 5.12 клиент отправляет `projectFilePath, physicalFilePath, projectContent,
  languageName, globalProperties`. Добавлен `LoadProjectInstance` для in-memory
  экземпляров, в том числе file-based programs.
- `IProjectFile` и новый `IProjectInstance` поддерживают `Dispose`. Новый RPC target
  предоставляет `GetDiagnosticLogItems`, `GetItemMetadataValues`, `GetPropertyValue`,
  `ExpandString` и освобождение ресурса. `RpcServer` отслеживает target в обе стороны
  под reader/writer lock, поддерживает `RemoveTarget` и соответствующую очистку.
- Даже обычный `.csproj` проходит через `await using` `RemoteProjectFile` в
  `BuildHostProjectFileInfoProvider`, а его `DisposeAsync` вызывает `IProjectFile.Dispose`
  по RPC. Из старой формы DTO и неизменной формы пакета совместимость с 5.9 host
  не следует. Для 5.12-клиента нужна согласованная пара host/Contracts. Guard 5.9
  текущего продукта остаётся обязательным.
- В BuildHost добавлены ссылки на
  `src/Compilers/Core/Portable/InternalUtilities/ReaderWriterLockSlimExtensions.cs`
  и `src/Workspaces/Core/Portable/Utilities/StandardHandleInheritance.cs`.
  Второй файл используется при старте host для отключения наследования стандартных
  дескрипторов. В client build добавлен linked
  `src/Compilers/Core/Portable/InternalUtilities/EnvironmentExtensions.cs`.
  В общем `src/Dependencies/Contracts` изменены `FailFast.cs` и `FatalError.cs`.
  Подробный список файлов — в сырых данных.

[Upstream Contracts](https://github.com/dotnet/roslyn/tree/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Contracts) ·
[Upstream server RPC](https://github.com/dotnet/roslyn/tree/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/BuildHost/Rpc) ·
[Upstream client RPC](https://github.com/dotnet/roslyn/tree/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Core/Rpc) ·
[Локальный guard](../../../forks/roslyn-buildhost/build/BuildHostFork.targets)

## Ранняя отмена

Скачан патч `2624f5c156a7c2edeed0e0330b6528b32e37e6a5`. GitHub Compare от него
к commit пакета 5.12.0 вернул `ahead=2301`, `behind=0`, merge base — сам патч.
В точном клиентском источнике 5.12.0 в `NoLock_GetBuildHostAsync` исключение из
`ConfigureGlobalStateAsync` или `FindBestMSBuildAsync` попадает в catch, который
вызывает `DisposeAsync` и пробрасывает исходное исключение. Это происходит до
регистрации в `_processes`. Dispose посылает Shutdown с `CancellationToken.None`
и при необходимости завершает дочерний процесс. Соседние патчи `cc080c5038`
и `3e70a65557` также зафиксированы в сырых данных.
[Патч исправления](https://github.com/dotnet/roslyn/commit/2624f5c156a7c2edeed0e0330b6528b32e37e6a5) ·
[Исходник клиента 5.12.0](https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Core/MSBuild/BuildHostProcessManager.cs)

Проверка исходников подтверждает историческую часть пункта 7. Состояние старой
[task-00b](../../workspace-load-performance/research-buildhost-fork/task-00b-early-cancel-buildhost-leak-low.md)
определяется отдельными фактическими прогонами на Windows и Linux.
