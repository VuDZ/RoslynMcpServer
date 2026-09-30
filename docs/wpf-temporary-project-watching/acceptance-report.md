# Проверка WPF-фильтра временных проектов

Дата: 2026-10-01. Реализована Epoch 1; версия исходников остаётся `1.5.0`.
Это отчёт реализации и проверок, не свидетельство выпуска.

## Воспроизведение до изменения production-кода

[Полный baseline](baseline-evidence.txt) снят новым тестовым процессом с прежней
реализацией `SolutionManager`. Windows, `dotnet --version`: **10.0.300**.
WPF task assembly:
`C:\Program Files\dotnet\sdk\10.0.300\Sdks\Microsoft.NET.Sdk.WindowsDesktop\tools\net10.0\PresentationBuildTasks.dll`,
FileVersion `10.0.826.23019`, ProductVersion
`10.0.8-servicing.26229.119+94ea82652cdd4e0f8046b5bd5becbd11461482ca`.
Локальные `Microsoft.WinFX.targets` используют современную ветку
`IncludePackageReferencesDuringMarkupCompilation != false`, где относительное
temporary-project name формируется из parent name, random component и `wpftmp`;
fixture не переключает legacy-ветку.
Fixture — настоящая `net10.0-windows` WPF Solution (`.slnx`), XAML использует
`LocalWidget` из той же сборки, что вызывает temporary-assembly pass. Сначала
выполнена прогревающая сборка и restore, затем `LoadAsync`, затем сборка под
работающими watchers. Тест запущен через Roslyn MCP `run_specific_test`;
внутри интеграции fixture запускает настоящую команду `dotnet build`.

Команда:

```text
dotnet build C:\Users\VuDZ\AppData\Local\Temp\RoslynWpfWatching-2c5736517bcd407e8821a075c0db0a4a\WpfProbe.slnx --no-incremental --nologo
```

CWD команды и watcher root:
`C:\Users\VuDZ\AppData\Local\Temp\RoslynWpfWatching-2c5736517bcd407e8821a075c0db0a4a`.
MSBuildStartupDirectory совпал с этим CWD; MSBuildProjectDirectory — его `App`.
Рабочий каталог самой WPF-задачи напрямую не измерен; указанные свойства не
выдаются за её CWD.

Временный проект:
`C:\Users\VuDZ\AppData\Local\Temp\RoslynWpfWatching-2c5736517bcd407e8821a075c0db0a4a\App\WpfProbe_s1jqos0b_wpftmp.csproj`.
Независимый FSW зарегистрировал **Created, Changed, Deleted** этого файла.
Information-лог менеджера трижды содержит `Project graph file changed on disk`
с этим полным путём. Logger принимает все уровни и не бросает исключения.
Outputs внутренней сборки (`.cs`, DLL, NuGet props/targets) находятся под `obj`/
`bin`; их события видны в raw evidence и не являются причиной graph-file stale.

До сборки: stale=false, refresh-all=false, dirty=0. После доставки событий и
после semantic flush: graph-file stale=true, composition-причины нет,
refresh-all=false, dirty=0. Первый `LoadAsync` с тем же ключом переоткрывает
граф; второй попадает в кеш. Backing `.csproj` и Solution не менялись.
Таким образом, baseline подтверждает именно заявленную гипотезу.

## Изменение

- [WorkspaceDiskPathFilter](../../Services/WorkspaceDiskPathFilter.cs) содержит
  отдельный `IsWpfTemporaryProject`: `.csproj` и окончание basename `_wpftmp`,
  без учёта регистра. `IsProjectGraphFile` сохраняет прежнюю классификацию.
- [WorkspaceInputSession](../../Services/Models/WorkspaceInputSession.cs) — один
  владелец generation и нормализованного immutable `FrozenSet` загруженных
  `Project.FilePath`. Та же материализация используется для корней watchers.
  Это минимальная модель общего input-state contract; покрытие imports/revisions
  этим изменением не объявляется реализованным.
- [SolutionManager](../../Services/SolutionManager.cs) захватывает сессию в каждом
  callback до включения событий. Только незагруженный кандидат временного имени
  отбрасывается перед graph-file stale. Deleted/обе стороны Rename используют
  снимок независимо от существования файла. Закрытие сессии и все callback
  mutations сериализованы отдельным коротким gate, без `_workspaceLock`.
  Позднее событие закрытой сессии ничего не меняет. Logger вызывается вне gate;
  исключения provider не прерывают callback.

## После изменения

[Полное post-fix evidence](post-fix-evidence.txt) получено из тестового процесса,
который использует заново собранные исходники. Running binary MCP-процесса
используется как runner и не выдаётся за обновлённый сервер.

SDK/fixture/команда те же; новый CWD и root:
`C:\Users\VuDZ\AppData\Local\Temp\RoslynWpfWatching-1417169e44554ddfac07571399b3a09c`.
Полные новые случайные имена записаны в evidence. Тест ждёт **настоящий Deleted
callback менеджера** с bounded timeout 10 секунд; независимый FSW отдельно
проверяет Created/Changed/Deleted временного `.csproj` и отсутствие error.
Synthetic событие для подтверждения WPF delivery не используется.

После delivery, после semantic flush и после двух последовательных `LoadAsync`:
graph-file=false, composition=false, refresh-all=false, dirty=0. Обе загрузки
попадают в кеш, generation и membership snapshot сохраняются. Information-лог
доступен, строк с временным проектом как изменением графа нет. Backing `.csproj`
и Solution сравниваются с исходным содержимым и совпадают.

Отдельная реальная интеграция в Created callback временного проекта вносит
внешнюю правку настоящего backing `.csproj` во время temporary-compilation.
Сборка проходит; настоящий путь присутствует в graph-change Information-логе,
graph-file stale=true, composition=false. События временного проекта по-прежнему
не взводят stale. Подавления watchers на время всей сборки нет.

## Проверки жизненного цикла и границ

`run_specific_test(className="WpfTemporaryProject")`: **40 passed, 0 failed**
(38 функциональных callback/race cases и 2 WPF integration tests).

Независимая родительская `run_dotnet_build` успешна. Затем отдельные
`run_specific_test` прошли: WpfTemporaryProject — 40, WorkspaceDiskPathFilterTests — 15,
SolutionManagerDiskWatcherCallbackTests — 2, SolutionManagerWatchRootsTests — 6,
WorkspaceDocumentDiskSyncTests — 6; **69 passed, 0 failed**. Сборка сообщила
три прежних nullable warning и новый xUnit1031 в проверке ожидания closure.
xUnit1031 сохранён: независимое ревью приняло ограниченный по времени race-тест;
дефекта production-кода он не показывает.

`WpfTemporaryProjectCallbackTests` проверяет:

- Created/Changed/Deleted незагруженного кандидата и загруженного отсутствующего
  проекта; membership нормализован, immutable и сравнивается по правилам ОС.
- Rename обычного/временного имени в обоих направлениях, пару временных имён
  с загруженной старой или новой стороной, пару двух незагруженных кандидатов.
- `.csproj`, `.sln`, `.slnx`, поддерживаемые props/targets/global.json,
  существующие ignored trees, очередь `.cs` и self-write suppression.
- Пауза callback перед admission → reset → новая сессия → возобновление старого
  callback: graph-file/source/file Rename/directory Rename/error ничего не
  меняют в новой сессии. Новый snapshot может содержать тот же временный путь.
- Те же пять видов callback завершаются при уже захваченном workspace semaphore.
- Close ждёт допущенную mutation; после Close новый update отвергается.
- После настоящего overflow-driven reload в WPF fixture старые graph/error
  callbacks не меняют reopened-сессию.

Дополнительно расширен `WorkspaceDiskPathFilterTests` (положительные имена,
регистр, середина имени, другое расширение, каталог и пустые значения).
`SolutionManagerDiskWatcherCallbackTests` сохраняет проверки throw-provider для
error/directory Rename в явно созданной тестовой сессии.

Overflow проверен отдельно: error callback оставляет graph-file=false и
composition=false, выставляет refresh-all=true; первый cached load выполняет
flush и выставляет **composition=true, graph-file=false** без текстовых изменений;
следующий load переоткрывает граф. Это прежнее поведение, не WPF-filter failure.
После refresh-all MSBuildWorkspace может породить source-write echo в dirty set;
оно видно в evidence и очищается при reopen. Изолированная WPF-проверка таких
source writes не выполняет и отложенных dirty/stale после cache hits не имеет.

## Ограничения

Результат подтверждён на Windows с SDK 10.0.300 и созданном минимальном fixture,
не на Solution коллег. Прямой CWD WPF-задачи недоступен. Watcher overflow в тесте
вводится через настоящий handler с `InternalBufferOverflowException`, а не
принудительным переполнением буфера ОС. Политики imports, coverage, generated
publication, автоматического reload и долговременного кеша не расширяются.

Родительская сборка и 69 перечисленных проверок прошли. Новый независимый
reviewer вернул **ACCEPT** без обязательных замечаний; отдельный сабагент
приёмки спецификации вернул **ACCEPTANCE PASSED**. Раунды исправлений не
потребовались. Реализация и приёмка завершены 2026-10-01; статусы серии и эпохи
обновлены. Выпуск не выполнен, версия и product README/ARCHITECTURE не изменены.
