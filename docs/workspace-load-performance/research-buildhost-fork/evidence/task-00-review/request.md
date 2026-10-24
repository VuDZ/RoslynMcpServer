# Независимое ревью: BuildHost/task-00 — воспроизводимый форк BuildHost без изменения поведения

Ты проводишь независимое ревью. Не меняй файлы репозитория, кроме файла с отчётом.

## Куда сохранить результат

Отчёт сохрани рядом с этим запросом:

```
docs/workspace-load-performance/research-buildhost-fork/evidence/task-00-review/review-<model_name>.md
```

`<model_name>` — короткое имя модели в нижнем регистре через дефис, например `review-gpt-5.md` или
`review-grok-4.md`. Если файл с таким именем уже есть, добавь номер раунда: `review-gpt-5-round-2.md`.
В начале отчёта укажи модель, дату и проверенный commit (HEAD ветки на момент ревью).

Если писать файлы ты не можешь, выведи весь отчёт одним markdown-документом. Владелец сохранит его
под этим именем.

## Где лежит

Репозиторий: https://github.com/VuDZ/RoslynMcpServer
Ветка: `research/workspace-load-performance`. Запрос написан при HEAD `0d6cece`.
Коммиты задачи:
- `e2e56a9` — дословная копия upstream;
- `ae5d093` — сборка без Arcade и сравнение;
- `d493831` — baseline на Roslyn.slnx;
- `227ae5a` — запуск для решений вне Git;
- `5692fef` — офисный baseline;
- `cf87b73` — сценарии сбоев;
- `0d6cece` — сценарии сбоев на Windows.

Upstream: `dotnet/roslyn`, commit `35d9211b841e7613c1d2f8f5af6d628ace696c4c` (ветка `release/insiders`).
Из этого коммита собран NuGet-пакет `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0.

Что проверять:
- Постановка и критерии приёмки: `docs/workspace-load-performance/research-buildhost-fork/task-00-buildhost-fork-baseline-hi.md`.
- Отчёт: `docs/workspace-load-performance/research-buildhost-fork/evidence/task-00-fork-baseline.md`.
- Индекс исследования: `docs/workspace-load-performance/research-buildhost-fork/README.md`.
- Форк: `forks/roslyn-buildhost/`:
  - `upstream/` — дословная копия;
  - `build/Contracts/Contracts.Fork.csproj`, `build/BuildHost/BuildHost.Fork.csproj`;
  - `Directory.Build.props`, `UPSTREAM.md` (список файлов и SHA-256), `README.md`.
- Исключение форка из сервера:
  - `RoslynMcpServer.csproj` (`forks\**`);
  - `RoslynMcpServer.Tests/SourceStructure/SourceSetScope.cs`;
  - `AGENTS.md` и `docs/code-style.md` (исключение по стилю для `forks/`).
- Измерительный код (spike):
  - `spikes/BuildHostFork/`: `Program.cs`, `BuildHostSampler.cs`, `ProcessCommandLine.cs`, `FailureScenario.cs`, `ObservationComparer.cs`, `ProjectMergerKey.cs`, `BuildHostFork.csproj`;
  - скрипты `Invoke-BuildHostForkBaseline.ps1`, `Invoke-BuildHostForkScenarios.ps1`, `BuildHostForkCommon.ps1`.
- Общий код с другими spike:
  - `spikes/ParallelSearch/SolutionLoader.cs`, `MsBuildRegistration.cs`, `SearchMeasurement.cs`, `MatrixCommon.ps1`;
  - `spikes/ParallelLoad/SolutionObservation.cs` и `compare.py` (эталон логики сравнения).

Сырых результатов прогонов в репозитории нет, папки `results/` исключены через .gitignore. Офисные данные
намеренно не публикуются. Доступны только сводки в отчёте.

## Что проверить

1. **Дословность копии и ревизия.**
   - Файлы в `forks/roslyn-buildhost/upstream/` совпадают с `dotnet/roslyn@35d9211b` побайтно, SHA-256
     совпадают с `UPSTREAM.md`.
   - Набор файлов полный: всё, что компилирует upstream
     `Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj` и проект Contracts, включая файлы,
     подключённые ссылками из компилятора и `src/Dependencies/Contracts`. Ничего лишнего.
   - Ревизия действительно та, из которой собран пакет 5.9.0 (repository commit в nuspec).

2. **Сборка без Arcade эквивалентна upstream.** Сравни `BuildHost.Fork.csproj` и `Contracts.Fork.csproj`
   с upstream-проектами и Arcade-свойствами Roslyn:
   - TFM net8.0, `rollForward: Major`;
   - DefineConstants (`MSBUILDWORKSPACE_BUILDHOST` и другие);
   - LangVersion, Nullable;
   - ссылки: MSBuild 17.11.48 только reference-сборки, Microsoft.Build.Locator 1.10.2;
   - InternalsVisibleTo;
   - имя сборки и версии;
   - ресурсы: SDK-генерация strongly-typed resx вместо Arcade `GenerateSource` — совпадают ли имена
     классов и пространства имён ресурсов, не потерялись ли ресурсы;
   - `deps.json` и `runtimeconfig.json`.

   Отдельно оцени, важно ли что-то из этого:
   - у форка нет подписи strong name и Authenticode, а у пакета есть;
   - не собираются локализации (xlf);
   - размер сборки форка меньше на ~11 КБ.

3. **Схема подмены и отсутствие тихого возврата к оригиналу.** Отчёт утверждает:
   - клиент `Microsoft.CodeAnalysis.Workspaces.MSBuild.dll` 5.9.0 берёт BuildHost только из папки
     `BuildHost-netcore` рядом с собой (`BuildHostProcessManager.GetBuildHostPath`);
   - другого пути поиска и возврата к другому host нет;
   - `BuildHost-net472` и Mono не форкнуты.

   Проверь это по исходникам Roslyn того же коммита. Найди случаи, когда в «форковой» стороне сравнения
   на самом деле работает оригинальный host. Например:
   - переменные окружения или настройки, которые переопределяют путь к BuildHost;
   - net472-проекты на Windows;
   - кэш или повторно используемые процессы.

4. **Измерительный код не должен скрывать отличия.**
   - `SolutionObservation` и критерии приёмки task-00 должны совпадать: project variants,
     documents/generated sources, options, references/aliases, output paths, diagnostics, analyzer
     provenance. Чего observation не снимает (например, generated sources, analyzer provenance сверх
     путей)? Записано ли это в отчёте как явное ограничение, как требует задача?
   - `ObservationComparer` против `compare.py`:
     - эталон — первый запуск оригинала;
     - «естественный разброс» — значение, которое встречалось в других запусках оригинала;
     - правило для диагностик (ever/always);
     - вердикт;
     - медианы только по раундам ≥ 1.

     Есть ли путь, при котором реальное отличие форка попадёт в natural variance или не попадёт в
     сравнение совсем (отсутствующее поле, пустая строка вместо null, порядок)?
   - `BuildHostSampler` и `ProcessCommandLine`:
     - распознавание BuildHost по командной строке (кавычки, пробелы в путях);
     - фильтр по `StartTime`;
     - гонки при опросе каждые 100 мс;
     - CPU как нижняя граница;
     - корректность P/Invoke `NtQueryInformationProcess` (класс 60, раскладка `UNICODE_STRING`, длина
       буфера на x64/x86/ARM64);
     - процесс, чью командную строку не удалось прочитать, засчитывается как host.
   - `FailureScenario`:
     - корректно ли определяется `canceled` и `failed`;
     - момент `DisturbedAtMs`;
     - лимит 5 мин;
     - не маскирует ли `WaitForExitAsync` (добивание оставшихся процессов) утечку процессов. Утечка
       должна попадать в отчёт до добивания.
   - Скрипты PowerShell:
     - чередование порядка сторон;
     - `bin-fork` гарантированно содержит только форк (проверка версии `*fork*`);
     - `bin-fork-broken`;
     - фикстура изолирована от `Directory.Build.props` репозитория;
     - совместимость с Windows PowerShell 5.1: stderr нативных команд при `ErrorActionPreference=Stop`,
       StrictMode 2.0;
     - вырезание путей в `scenarios.md`.

5. **Отчёт соответствует данным и приёмке.**
   - Числа и выводы в `evidence/task-00-fork-baseline.md` согласованы между разделами и с README и
     task-файлом.
   - Выводы не сильнее данных: например, «не замедляет» при медиане 3 замеров, CPU клиента ×1,057 в
     офисе.
   - Требования task-00 («Что сделать» 1–5, «Приёмка») закрыты. Если нет — какие ещё открыты.
     В частности: Smoke и PowerToys упомянуты в задаче, а прогоны были на `RoslynMcpServer.sln`,
     Roslyn.slnx и офисном решении. Записано ли это расхождение?
   - Ведение статусов по разделу «Task status bookkeeping» в `AGENTS.md`.
   - В публичный репозиторий не попало ничего, что позволяет опознать офисное решение: пути, имена
     проектов, внутренние имена. Допустимы только агрегаты.

6. **Стиль.**
   - Файлы в `forks/` соответствуют стилю Roslyn и его `.editorconfig`, а не `docs/code-style.md`.
   - C#-код в `spikes/BuildHostFork/` и изменённые строки `spikes/ParallelSearch/SolutionLoader.cs`
     соответствуют `docs/code-style.md`: порядок членов, именование, комментарии. Старый код, который
     задача не меняла, не оценивать.

Если есть .NET SDK 10 и PowerShell 7, можно запустить:
- `pwsh spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1` — по умолчанию грузит
  `RoslynMcpServer.sln`;
- `pwsh spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1 -Solution RoslynMcpServer.sln -Runs 1`.

Укажи, что именно запускал и на какой ОС. Если не запускал, напиши об этом прямо.

## Формат отчёта

1. **Вердикт:** можно ли принять task-00 в заявленном объёме. Если нет — что блокирует.
2. **Находки** по убыванию серьёзности: blocker / major / minor / nit. Для каждой:
   - файл:строка (или путь в upstream);
   - что не так;
   - конкретный сценарий, при котором это ломает вывод или поведение;
   - доказательство (цитата кода, ссылка на строку upstream, команда);
   - предлагаемое исправление.
3. **Проверено и в порядке:** пункт и как проверено (сравнение хешей, чтение upstream, запуск). Так
   будет видно, что проверено, а что нет.
4. **Не проверено** и почему (нет доступа к Windows, к офисным данным и т. п.).

Не выдавай предположение за факт. Если вывод сделан без чтения кода или без запуска, пометь его как
непроверенный.
