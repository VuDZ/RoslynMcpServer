# Ревью BuildHost/task-00

- Модель: Grok 4.7.
- Дата: 2026-10-24.
- Проверенный commit: `972b7a84ebb55b73b47bc8acaa85920bf53cf081` (HEAD `research/workspace-load-performance`).
  Запрос ревью писался при `0d6cece`. От него до HEAD изменились только этот запрос и ссылка на него
  в отчёте. Код форка и spike — коммиты `e2e56a9`…`0d6cece`.
- ОС этой проверки: Windows 11 (10.0.26200), x64, .NET SDK 10.0.402, PowerShell 7.6.8.
  Скрипты `Invoke-BuildHostForkBaseline.ps1` и `Invoke-BuildHostForkScenarios.ps1` я не запускал.

## Вердикт

Task-00 в заявленном объёме принимать нельзя.

Форк как копия исходников и как подмена `BuildHost-netcore` собран аккуратно: 109 файлов побайтно
совпадают с `dotnet/roslyn@35d9211b`, клиент 5.9.0 берёт netcore-host только из папки рядом с собой,
отдельного пути «вернуться к dll из пакета» нет. Этого недостаточно для приёмки.

Блокирует сравнение. `ObservationComparer` слабее `spikes/ParallelLoad/compare.py`, хотя отчёт пишет,
что переносит его логику. Вердикт `identical` и «полей с естественным разбросом нет» означают только
то, что каждый прогон форка совпал с прогревом оригинала (round 0). Измеренные прогоны оригинала
при этом могут отличаться и в отчёт не попадут. Фраза «совпадают во всех 8 загрузках» из этого
вердикта не следует. Сырых JSON в репозитории нет, перепроверить опубликованные прогоны нельзя.

Пока comparer не приведён к compare.py и сводка не пересчитана по сохранённым JSON (или по новым
прогонам), эквивалентность task-00 не доказана. Отдельно не закрыты фикстуры Smoke и PowerToys и
явный список полей, которые observation не снимает.

## Находки

### Blocker. Comparer считает форк identical, если он совпал с прогревом

`spikes/BuildHostFork/ObservationComparer.cs:23`, `:24-35`, `:45-63`, `:107-111`.
`spikes/ParallelLoad/compare.py:20-36`, `:73-86`.
Отчёт: `evidence/task-00-fork-baseline.md:54-55`, `:114-116`, `:142-143`.
Задача: `task-00-buildhost-fork-baseline-hi.md:41` («Comparator из E0 не ослаблять»).

Эталон — `originals[0]`. Файлы грузятся по номеру раунда, round 0 — прогрев, его пишет
`Invoke-ObserveRounds`. Значит эталон — прогрев, а не первый измеренный запуск.

Набор «уже виденных» значений собирается по всем раундам оригинала, включая прогрев. Если значение
форка совпало с эталоном, ветка natural даже не смотрится. Отличия измеренных раундов оригинала
друг от друга нигде не печатаются.

Сценарий, в котором реальное отличие пропадает:

- прогрев оригинала: поле A;
- измеренные раунды оригинала: поле B;
- все раунды форка: поле A.

compare.py сравнивает измеренные раунды с baseline[1] (первый измеренный, прогрев в сравнение не
входит). Форк со значением A против эталона B — unexplained. `ObservationComparer` видит A == A и
пишет `identical`, natural = 0. Сводка при этом говорит «полей с естественным разбросом нет», хотя
оригинал после прогрева стабильно другой.

Дальше compare.py ослаблен ещё в двух местах.

Естественный разброс. compare.py кладёт в natural только два значения из различия baseline[1] и
baseline[2] (`compare.py:75-76`). Значение, которое было только в раунде 3 или только в прогреве,
кандидата не оправдывает. Здесь оправдывает любое значение любого раунда оригинала (`:56-60`).
Форк может систематически выдавать редкий выброс одного прогона оригинала, и это будет natural.

Диагностики. compare.py берёт симметрическую разность с эталоном (`compare.py:20-21`) и пропускает
сообщение только если наличие этого сообщения уже разошлось между baseline[1] и baseline[2].
Здесь правило ever/always по всем раундам, включая прогрев (`ObservationComparer.cs:39-40`, `:66-68`):

- сообщение, которого не было ни в одном прогоне оригинала, — ошибка;
- сообщение, которое было в каждом прогоне оригинала, обязано быть у форка;
- сообщение, которое было хотя бы в одном прогоне и отсутствовало хотя бы в одном, форк может и
  иметь, и не иметь.

Диагностика только из прогрева разрешает форку печатать её во всех измеренных раундах. Вердикт
остаётся identical.

Что в сравнении сделано строже compare.py и дефектом не является: нет `explainedCase` для путей,
отличающихся только регистром, поэтому такой путь станет unexplained. Хеш первого поиска
сравнивается, compare.py его не сравнивает. Медианы берутся по раундам `> 0` (`:131-132`) — это
совпадает с требованием.

Исправление: эталон — original round 1; natural — только значения из различия original round 1 и
round 2, как `differences` + словарь в compare.py; диагностики по тому же правилу, а не ever/always;
round 0 не участвует в эквивалентности; в `summary.md` печатать различия оригинала с самим собой.
Пересчитать уже сохранённые JSON. Пока их нет в репозитории, без нового прогона или этих файлов
утверждение «8 загрузок совпали» не восстановить.

### Major. Поля из приёмки не снимаются и не названы ограничением

`spikes/ParallelLoad/SolutionObservation.cs:18-39`, `:49-62`.
Задача: `task-00-buildhost-fork-baseline-hi.md:39-41`.
Отчёт описывает, что снимается (`task-00-fork-baseline.md:50-53`), и не пишет, чего observation не
видит.

`Capture` пишет пути документов, additional files и analyzer config, пути и алиасы ссылок, пути
анализаторов (`FullPath`), подмножество опций и три выходных пути. В эквивалентность не попадает:

- текст документа и то, сгенерирован он или нет. Два разных `.g.cs` с одним путём совпадут.
  `CompilationOutputInfo.GeneratedFilesOutputDirectory` на проекте есть, в снимок берётся только
  `AssemblyPath` (`:20`);
- provenance анализатора сверх пути: имя, версия, хеш содержимого, shadow-копия. Расхождение
  бинарника по тому же пути observation не увидит;
- остальные опции компиляции и разбора (уровень предупреждений, deterministic, VB language version,
  документация ссылок и т. д.). Nullable, AllowUnsafe и LanguageVersion снимаются только с C#
  (`:57-59`).

Такое отличие не попадает ни в natural, ни в unexplained: поля нет в JSON. Приёмка как раз требует
вынести недостающие поля явным ограничением. В отчёте этого раздела нет. Ограничение net472 по
процессу sampler'а записано; ограничение снимка workspace — нет.

Исправление: в отчёте task-00 отдельным списком записать, что именно не сравнивается. Не расширять
снимок в этом task, если полное содержимое generated sources и provenance остаются на следующие
задачи, но тогда приёмка task-00 на эти поля не закрыта.

### Major. Smoke и PowerToys не прогонялись, и это не записано

Задача, пункт 4: `task-00-buildhost-fork-baseline-hi.md:30-33` — Smoke, PowerToys и полный Roslyn,
офис после публичной проверки.

Фактически: `RoslynMcpServer.sln` (локально, прогрев и 2 измеренных раунда, `:57-58`), Roslyn.slnx
(434 проекта, прогрев и 3 раунда) и офисное решение (142 проекта). В задаче, отчёте и README нет
фразы, что Smoke и PowerToys заменены или отложены. Раздел «Не выполнено» после вычёркивания
оставляет только независимое ревью (`task-00-fork-baseline.md:81-91`, `:215`).

Roslyn.slnx закрывает «полный Roslyn». Два измеренных раунда локального `.sln` меньше минимума
«прогрев + три» (`task-00-buildhost-fork-baseline-hi.md:42-43`); для Roslyn.slnx и офиса в отчёте
заявлены три, сырых файлов нет.

Исправление: либо прогнать Smoke и PowerToys тем же скриптом, либо явно сузить пункт 4 и не считать
его закрытым молча.

### Minor. Офисный net472-host не наблюдался, а fallback на netcore в снимок не попадает

`BuildHostProcessManager.GetBuildHostWithFallbackAsync` в `dotnet/roslyn@35d9211b`,
`src/Workspaces/MSBuild/Core/MSBuild/BuildHostProcessManager.cs:80-90`: если проект классифицирован
как NetFramework и `HasUsableMSBuildAsync` ложно, клиент переходит на NetCore. Фактический kind
вызывающий код выбрасывает:

```csharp
var (buildHost, _) = await buildHostProcessManager.GetBuildHostWithFallbackAsync(...)
```

`BuildHostProjectFileInfoProvider.cs:26-27` того же коммита.

Отчёт (`task-00-fork-baseline.md:147-150`) пишет, что варианты net472 в обоих офисных прогонах грузил
один и тот же не форкнутый host. Это верно только когда Visual Studio MSBuild для host'а пригоден.
Офисный прогон шёл sampler'ом, который видел только процессы `dotnet` (сам отчёт это говорит),
поэтому процесс `BuildHost-net472.exe` в таблицу не мог попасть. Если проверка MSBuild не прошла,
обе стороны ушли бы на свой netcore-host, форк участвовал бы в загрузке этих проектов, а observation
этого не покажет.

Исправление: для офиса либо повторить прогон текущим sampler'ом и привести `hosts`, либо
сформулировать ограничение как «net472 не форкнут; каким процессом грузились эти варианты, прогон
не записал».

### Minor. «Только английский» и «состав папки совпадает» сильнее, чем отличие от пакета 5.9.0

Проверено по nupkg `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0 и по локальной сборке форка.

- В обеих сборках один и тот же ресурс
  `Microsoft.CodeAnalysis.WorkspaceMSBuildBuildHostResources.resources` с двумя строками:
  `Parameter_cannot_be_null_empty_or_contain_whitespace` и `Project_does_not_contain_0_target`,
  текст совпадает. Класс генератора SDK — `Microsoft.CodeAnalysis.WorkspaceMSBuildBuildHostResources`
  (`BuildHost.Fork.csproj:46-52` и сгенерированный designer).
- В папке `BuildHost-netcore` пакета нет satellite dll. `deps.json` пакета перечисляет 13 культур,
  файлов культур в nupkg нет. Сравнение оригинала и форка шло на английских строках с обеих сторон.
  Отсутствие xlf — отличие сборки, не наблюдавшееся отличие сообщений.
- Состав папки не тот. Пакет: host dll 112464 байт, Contracts pdb, без pdb host'а. Форк: host dll
  100864 байт (разница 11600 байт, те самые ~11 КБ), pdb есть и у host, и у Contracts. `deps.json`
  форка добавляет `Microsoft.CodeAnalysis.Workspaces.MSBuild.Contracts.Reference` и пишет
  fileVersion Contracts `0.0.0.0`, хотя у самого dll FileVersion `5.9.0.0`. У пакета FileVersion
  `5.900.26.35703`, версия библиотеки в deps — `5.9.0-1.26357.3`.
- `Directory.Build.props` форка не задаёт `<Features>strict</Features>`. У upstream это общее
  свойство `eng/targets/Settings.props:30`. На IL я не смотрел.

Strong name и Authenticode на подмену не влияют, см. раздел «Проверено». Размер и подпись сами по
себе поведение загрузки не меняют. Расхождение `deps.json` стоит поправить или явно принять: это
единственное отличие файлов запуска, которое я не исполнял.

### Minor. Непрочитанная командная строка делает любой новый `dotnet` видом BuildHost

`spikes/BuildHostFork/BuildHostSampler.cs:138-141`, `:100-101`. `FailureScenario.cs:73-76`.

`TryRead == null` даёт хост `"(unreadable command line)"`. Фильтр `StartTime < _since` такой процесс
не отсекает. Первый такой процесс становится `FirstHost`, и `kill-host` убивает его, а настоящий
BuildHost остаётся. Тот же процесс попадает в count и в нижнюю границу CPU.

На x64 этой машины `NtQueryInformationProcess` (класс 60) для текущего процесса вернул командную
строку: `UNICODE_STRING` 16 байт, `Buffer` по смещению 8, статус 0, длина в байтах, текст читается.
Это не проверка чужого процесса с отказом в доступе. Опубликованные Windows-сценарии как раз
показывают путь из своей папки, то есть в тех прогонах чтение удалось. Дыра остаётся для прогона,
где чтение не удалось.

Исправление: непрочитанный `dotnet` писать отдельно и не считать ни хостом в инвентаре, ни целью
`kill-host`. Иначе отказ чтения выглядит как лишний BuildHost.

### Minor. CPU клиента и инвентарь хоста не то, чем их считает сводка

- `Program.cs:54-66`: `WallMs` останавливается после загрузки, `ClientCpuMs` и peak working set
  читаются после поиска и после ожидания выхода хостов (до 10 с). В таблицах это «CPU клиента»
  рядом с загрузкой. Отношение ×1,057 в офисе может включать поиск. Отчёт это число оговаривает
  (`task-00-fork-baseline.md:144-146`); строки статуса задачи и README говорят только про загрузку
  ×1,001.
- `ObservationComparer.cs:84-90` в вердикт не берёт `BuildHostProcesses.hosts` и
  `RemainingBuildHosts`. Утечка и запуск не из `bin-fork` не делают вердикт `different`. Каталог
  на диске проверяется до прогона (`BuildHostForkCommon.ps1:23-24`, ProductVersion `*fork*`),
  это проверка файла, не процесса.
- Опрос 100 мс (`BuildHostSampler.cs:20`, `:91-127`). Процесс, который стартовал и завершился между
  тиками, в инвентарь не попадает. CPU — последнее снятое значение, это нижняя граница, отчёт так и
  пишет. Для kill-host отдельный перезапуск маловероятен: загрузчик идёт по проектам последовательно
  и исключение из `LoadProjectFileInfosAsync` из цикла не перехватывает
  (`MSBuildProjectLoader.Worker.cs:96-118`, `:144` на `35d9211b`). «Перезапуска нет» опирается на это
  и на текст исключения, не только на count = 1.

Исправление: CPU и память клиента снимать в той же точке, что и `WallMs`, поиск учитывать отдельно.
`hosts` и `RemainingBuildHosts` включить в вердикт.

### Minor. Скрипт сценариев не падает, когда стороны разошлись. PS 5.1 закрыт не весь

`Invoke-BuildHostForkScenarios.ps1:117-163` всегда пишет `scenarios.md` и завершается. Равенство
исходов, кроме строки про диагностики фикстуры, никто не проверяет. Опубликованная таблица —
пересказ этой сводки, самого `scenarios.md` в репозитории нет.

`FailureScenario.cs:85-98`: в отчёт попадает первая строка первого `InnerException`. Отличие в
следующих исключениях или в хвосте сообщения в `scenarios.md` не видно.

`MatrixCommon.ps1:3-6`: `Invoke-Native` при `ErrorActionPreference = Stop` по-прежнему не прячет
stderr. `Get-GitOutput` (`:11-20`) и `Invoke-Spike` (`BuildHostForkCommon.ps1:45-56`) прячут.
На Windows PowerShell 5.1 stderr `dotnet build` / `dotnet restore` всё ещё может оборвать успешную
команду. Офисный прогон на 5.1 дошёл до сводки, значит в том запуске это не сработало. Дыра в коде
осталась.

Фикстура: пустые `Directory.Build.props` и `Directory.Build.targets` в корне фикстуры
(`Invoke-BuildHostForkScenarios.ps1:69-73`). Ближайший props выигрывает у props репозитория, я это
не прогонял через MSBuild. `global.json` репозитория на фикстуру по-прежнему действует: изоляция
заявлена только для Directory.Build.

Вырезание путей (`:127-131`) покрывает каталог фикстуры, каталог spike, каталог решения и `$HOME`.
Сравнение строк ординальное: другой регистр того же пути в `scenarios.md` останется.

Исправление: завершать скрипт ненулевым кодом, если исход, диагностики, каталог хоста или
`RemainingBuildHosts` сторон различаются. `Invoke-Native` обернуть так же, как `Invoke-Spike`.

### Minor. Шапка индекса противоречит строке task-00

`research-buildhost-fork/README.md:3-5`: статус «planned; реализация форка не начата, независимое
ревью не проводилось». Строка task-00 (`:29`) и абзац ниже (`:33-35`, «новые измерения и реализация
не запускались») описывают уже идущую работу. Файл задачи и отчёт согласованы между собой: in
progress, 2026-10-24, ревью ещё не было. Принятым task не помечен. Поправить шапку и абзац «сейчас
оформлен план».

### Nit

- `spikes/BuildHostFork/ProjectMergerKey.cs:6`: тип называется `ProjectMerger`, файл — нет.
  `docs/code-style.md` требует совпадения имени. Сама формула ключа совпадает с
  `spikes/ParallelLoad/ProjectMerger.cs:7`.
- `ObservationComparer.Run` около 90 строк. Для code-style это повод разделить сбор отчёта и
  сравнение, не дефект поведения.
- В публичной сводке офиса есть имя символа поиска `MainViewModel`
  (`task-00-fork-baseline.md:127`). Путей, имён проектов и имени машины офиса нет. По одному
  распространённому имени решение не опознать. Агрегатом это всё же не является.
- Подпись «natural-variance fields» считает пары (раунд, поле), не число полей
  (`ObservationComparer.cs:54-60`, `:80`). При нуле это не меняет текст.

## Проверено и в порядке

### Дословность и ревизия

- SHA-256 из `forks/roslyn-buildhost/UPSTREAM.md`: 109 файлов. Хеш рабочей копии на этой машине не
  совпадает из-за `core.autocrlf=true` (в `Program.cs` на диске только CRLF). После замены CRLF на
  LF совпали все 109. Git-blob тех же путей совпал с деревом GitHub
  `dotnet/roslyn@35d9211b841e7613c1d2f8f5af6d628ace696c4c` (дерево не обрезано, 32290 blob):
  109/109, лишних файлов в `upstream/` нет.
- Каталоги полные: `src/Workspaces/MSBuild/BuildHost/` 45 файлов, `Contracts/` 18,
  `src/Dependencies/Contracts/` 27, плюс 16 файлов компилятора и `.editorconfig`, `License.txt`,
  `THIRD-PARTY-NOTICES.txt`. В истории `upstream/` только коммит `e2e56a9`.
- 16 `Compile Include` в upstream-csproj и в `BuildHost.Fork.csproj:27-42` — один и тот же список.
  `Microsoft.CodeAnalysis.Contracts.projitems` подключает `**/*.cs` своего каталога, каталог скопирован
  целиком.
- Nupkg 5.9.0 скачан с nuget.org. В nuspec `repository commit` и текст description —
  `35d9211b841e7613c1d2f8f5af6d628ace696c4c`, ветка `release/insiders`.

### Сборка

- TFM `net8.0`, `RollForward` Major, `UseAppHost` false, `SelfContained` false, `PlatformTarget`
  AnyCPU, `AllowUnsafeBlocks`, `DefineConstants` включает `MSBUILDWORKSPACE_BUILDHOST`.
  LangVersion у upstream для C# — `preview` (`eng/targets/Settings.props:208`), у форка тоже
  (`Directory.Build.props:8`). Nullable enable с обеих сторон.
- MSBuild 17.11.48: upstream `PackageDownloadAndReference` на `ref/$(TargetFramework)`, форк —
  `PackageDownload` и `Reference` на `ref/net8.0` с `Private=false` (`BuildHost.Fork.csproj:54-64`).
  Microsoft.Build.Locator 1.10.2. Скопированный в выход Locator побайтно совпал с пакетом
  (SHA-256 `FCC85336217BE226F90A5DDFA55151DC84FCB2A76FE242F5274535C6B878AA30`).
- AssemblyVersion обеих сборок 5.9.0.0. InformationalVersion форка
  `5.9.0-fork+35d9211b841e7613c1d2f8f5af6d628ace696c4c`.
- `runtimeconfig.json`: обе стороны `net8.0`, rollForward Major, те же два `configProperties`.
  Порядок ключей разный, байты разные. Клиент всё равно передаёт `--roll-forward LatestMajor`
  (`BuildHostProcessManager.cs:249-252`), так что значение в json при штатном запуске перекрывается
  одинаково.
- InternalsVisibleTo форка на Contracts — только BuildHost (`Contracts.Fork.csproj:14-16`). У
  upstream ещё LanguageServer, Workspaces.MSBuild, тесты и Moq. Host — отдельный процесс, клиент
  его сборку не загружает. На протокол это не влияет.
- Подпись. Пакет: strong name `31bf3856ad364e35`, Authenticode Valid, CN=.NET, Microsoft.
  Форк: токена нет, Authenticode NotSigned. `GetBuildHostPath` проверяет только `File.Exists`
  (`BuildHostProcessManager.cs:300-324`). Канал JSON между процессами, Contracts host грузит из
  своей папки. Подпись в эту подмену не входит.
- Исключение форка из сервера: `RoslynMcpServer.csproj:71-73`, `SourceSetScope.cs:121-123`,
  абзац в `AGENTS.md` и в `docs/code-style.md`. Ближайший `Directory.Build.props` форка перекрывает
  props репозитория. Отдельного `Directory.Build.targets` в репозитории нет.

### Подмена host

Читал клиент `35d9211b`, не декомпиляцию.

- Netcore-dll только `GetNetCoreBuildHostPath` → `BuildHost-netcore/<dll>` рядом со сборкой клиента,
  либо `contentFiles/any/any/...`, если на два уровня выше лежит nuspec
  (`BuildHostProcessManager.cs:263-266`, `:300-324`). Переменной окружения с другим путём нет.
  `MSBUILD_EXE_PATH` из процесса host'а удаляется (`:332-334`).
- Повторный запуск ради SDK (`:149-176`) меняет только `dotnet.exe`. Путь к dll тот же
  `GetNetCoreBuildHostPath`. Это не возврат к пакету.
- Кэш `_processes` — один процесс на kind внутри одного `BuildHostProcessManager`. Spike поднимает
  новый процесс на раунд. Между раундами host не переиспользуется.
- `BuildHost-net472` и Mono в форк не входят. Скрипт подменяет только `BuildHost-netcore`
  (`BuildHostForkCommon.ps1:17-22`). На стороне форка net472 остаётся сборкой пакета.
- Не-SDK проект на Windows получает NetFramework (`GetKindForProject`, `:365-420`). Сломанный XML на
  NET получает NetCore (`:386-392`). vcxproj фикстуры отсекается по расширению до выбора host
  (`BuildHostProjectFileInfoProvider.cs:21-24`).
- `bin-fork-broken` удаляет dll форка (`Invoke-BuildHostForkScenarios.ps1:59-62`). Клиент при
  отсутствии файла бросает исключение и другого каталога не ищет (`:321-322`). Обходного пути в
  этом коммите нет.
- Раскладка spike: сборка клиента лежит в `bin-fork`, nuspec на два уровня выше не появляется.
  Ветка `IsLoadedFromNuGetPackage` для этого прогона ложна.

### Измерения, которые дефектом не оказались

- Чередование сторон: round 0 original затем fork, нечётные раунды наоборот
  (`BuildHostForkCommon.ps1:31-33`).
- Проверка `*fork*` смотрит ProductVersion dll в `bin-fork`. У собранного dll он
  `5.9.0-fork+35d9211b…`, проверка такая dll пропускает.
- Путь к dll в командной строке: индексная арифметика `Identify` (`BuildHostSampler.cs:143-151`)
  на примерах без кавычек, с пробелами, с кавычками и у `dotnet.exe`, и у dll, даёт каталог host'а.
  Это воспроизведение той же арифметики, не вызов метода. Сам `NtQuery` на x64 выполнен.
- `WaitForExitAsync` (`BuildHostSampler.cs:44-73`) возвращает id, которые были живы после таймаута,
  и только потом вызывает `Kill`. В JSON попадает это число (`FailureScenario.cs:29`, `:40`).
  Утечка в отчёте есть до того, как её число потерялось бы из-за добивания.
- Лимит 5 минут: `FailureScenario.cs:26`, `:110`. По таймауту исход пишется как «no result after
  300 s», процесс скрипта на этом завершается. Токен загрузки по этому таймауту не отменяется.
- `canceled` — `Task.IsCanceled`, иначе первая строка первого исключения (`:85-98`). Обе стороны
  идут через один `SolutionLoader`.
- `SolutionLoader.cs` в диапазоне task-00 получил необязательный список диагностик под lock и
  `CancellationToken` последним аргументом. К стилю изменённые строки претензий нет.
- Порядок членов в новых типах spike: свойства, конструктор, методы, вложенный `UnicodeString`,
  поля в конце. Текст в коде на английском. Файлы `forks/` после `e2e56a9` не менялись и идут
  стилем Roslyn; новые csproj форка с отступом в два пробела, как upstream-csproj.

### Отчёт и приватность офиса

- Отношения в таблицах совместимы с округлением медиан до целых: comparer делит не округлённые
  медианы (`ObservationComparer.cs:131-133`), а в таблицу печатает `Math.Round`. Противоречия
  между соседними числами одной таблицы я не нашёл. Сами медианы по сырым JSON не проверял.
- Офисный коммит `5692fef` меняет только три markdown-файла. Путей и имён проектов офиса в них нет.
  Единственное внутреннее имя — `MainViewModel`, см. nit.

## Не проверено

- Скрипты baseline и scenarios не запускались. Не подтверждены ни локальные числа, ни Windows-прогоны
  Roslyn.slnx и офиса, ни таблица отказов. Их JSON в репозиторий не входят (`results/` в
  `spikes/BuildHostFork/.gitignore`).
- x86 и ARM64 для `NtQueryInformationProcess` не исполнялись. На x64 раскладка совпала с нативной
  `UNICODE_STRING` (padding до указателя). Для x86 и ARM64 вывод только по правилам маршалера:
  указатель выравнивается на свой размер, это та же раскладка, что у структуры Win32. Это не прогон.
- `/proc/<pid>/cmdline` не читался: проверка на Windows.
- IL форка и пакета не сравнивался. `<Features>strict</Features>` и отсутствие Authenticode на
  codegen не проверялись.
- Фикстура не собиралась, поэтому перекрытие `Directory.Build.props` репозитория не наблюдалось
  в MSBuild, только по правилу ближайшего файла.
- Офисный компьютер и его процессы мне недоступны. Утверждение «net472 грузил пакетный exe» остаётся
  выводом из кода, не из инвентаря того прогона.
