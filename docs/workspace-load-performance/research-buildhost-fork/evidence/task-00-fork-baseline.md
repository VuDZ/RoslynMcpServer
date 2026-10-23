# BuildHost/task-00 — воспроизводимый форк: локальная часть

Дата: **2026-10-24**. Решение владельца 2026-10-23: вариант A — папка `forks/roslyn-buildhost`
в research-ветке; стиль кода форка — Roslyn, не `docs/code-style.md` (записано в `AGENTS.md`
и `docs/code-style.md`). Production сервера не менялся, кроме исключения `forks\**` из его сборки.

## Upstream

- `dotnet/roslyn` `35d9211b841e7613c1d2f8f5af6d628ace696c4c` (`release/insiders`, 2026-07-07) —
  ревизия из nuspec пакета `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0.
- Дословно скопированы 109 файлов, с хешами в `forks/roslyn-buildhost/UPSTREAM.md`:
  - `BuildHost` и `Contracts`;
  - общий проект `src/Dependencies/Contracts`;
  - 16 файлов компилятора, которые подключает проект BuildHost;
  - `License.txt`, `THIRD-PARTY-NOTICES.txt` и `.editorconfig` Roslyn.

  Лицензия MIT.
- Минимальная граница форка — только процесс BuildHost-netcore.
  - Клиент (`Microsoft.CodeAnalysis.Workspaces.MSBuild.dll`) берёт BuildHost из папки
    `BuildHost-netcore` рядом с собой (`BuildHostProcessManager.GetBuildHostPath`, декомпиляция)
    и общается с ним JSON по именованному каналу.
  - Поэтому подмена — это копирование сборки форка в эту папку. Сборка Contracts форка
    используется только процессом BuildHost.
- Не форкаются: клиент, `BuildHost-net472` (проекты .NET Framework на Windows; в офисном решении
  такие варианты есть, это ограничение до task-02) и Mono.

## Сборка без Arcade

`forks/roslyn-buildhost/build/`:

- `Contracts.Fork.csproj` и `BuildHost.Fork.csproj` — обычный .NET SDK.
- Параметры как у пакета: net8.0, `rollForward: Major`, `MSBUILDWORKSPACE_BUILDHOST`.
- Ссылки на reference-сборки MSBuild 17.11.48 через `PackageDownload`, как upstream
  `PackageDownloadAndReference`; Microsoft.Build.Locator 1.10.2.
- Ресурсы генерирует SDK вместо Arcade `GenerateSource`.

Результат:

- сборка без ошибок;
- состав выходной папки совпадает с `BuildHost-netcore` пакета: BuildHost, Contracts,
  Locator, `deps.json`, `runtimeconfig.json`;
- версия 5.9.0, информационная — `5.9.0-fork+35d9211b…`;
- локализованные ресурсы (xlf) не собираются, сообщения форка только на английском.

## Сравнение оригинал / форк

`spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1`:

- spike собирается один раз, а его копия `bin-fork` получает форк в `BuildHost-netcore`;
- раунды чередуют порядок сторон, раунд 0 — прогрев;
- `observe` снимает `SolutionObservation` из `spikes/ParallelLoad` (поля проектов, документы,
  ссылки, опции, пути вывода), диагностики workspace, первый поиск и опрос дочерних процессов
  `dotnet` каждые 100 мс;
- `compare` переносит логику `compare.py`: естественный разброс по запускам оригинала,
  остальное — unexplained.

Локально, Linux 2 vCPU, `RoslynMcpServer.sln` (5 проектов), поиск `SolutionManager`, прогрев
и 2 раунда:

| метрика (медиана) | оригинал | форк | отношение |
|---|---|---|---|
| загрузка | 7 117 мс | 7 437 мс | 1,045 |
| первый поиск | 6 501 мс | 6 955 мс | 1,070 |
| CPU клиента | 13 740 мс | 14 395 мс | 1,048 |
| процессов BuildHost | 2 | 2 | 1 |
| CPU BuildHost (нижняя граница) | 5 050 мс | 5 175 мс | 1,025 |
| пик памяти BuildHost | 162 МБ | 162 МБ | 1,001 |

Эквивалентность: **identical** — все поля проектов, диагностики и ответ поиска совпадают, полей
с естественным разбросом нет. Разница времени в пределах разброса этой машины: в первом прогоне
того же сценария загрузка форка была ×1,022, поиск ×0,959.

## Для task-02

- `IBuildHost.ConfigureGlobalState` получает глобальные свойства клиента. Из свойств решения в
  строках клиента есть только `SolutionDir`, пути к файлу решения нет. Предварительной evaluation
  нужно найти решение в этой папке либо вычислять наперёд `ProjectReference` уже запрошенных
  проектов.
- Там же передаётся `maxNodeCount`.

## Не выполнено по task-00

- Roslyn.slnx и офисное решение: прогрев + не меньше трёх замеров на сторону на Windows.
- Отказы:
  - отсутствующий или неподдерживаемый проект;
  - несколько TFM;
  - отмена или ошибка;
  - завершение дочерних процессов.
- Проверка, что при сбое форка нет незаметного перехода на оригинал. В этой схеме перехода нет
  вообще: в папке лежит только один BuildHost.
- Независимое ревью.
