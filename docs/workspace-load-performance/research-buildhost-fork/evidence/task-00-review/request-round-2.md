# Независимое ревью, раунд 2: исправления BuildHost/task-00

Ты проводишь независимое ревью. Не меняй файлы репозитория, кроме файла с отчётом.

## Куда сохранить результат

Отчёт сохрани рядом с этим запросом:

```
docs/workspace-load-performance/research-buildhost-fork/evidence/task-00-review/review-<model_name>-round-2.md
```

`<model_name>` — короткое имя модели в нижнем регистре через дефис, как в первом раунде
(`review-gpt-6-1-sol.md`, `review-grok-4-7.md`). В начале отчёта укажи модель, дату и проверенный
commit (HEAD ветки на момент ревью). Если писать файлы ты не можешь, выведи весь отчёт одним
markdown-документом; владелец сохранит его под этим именем.

## Контекст

Первый раунд: [запрос](request.md), ревью [GPT-6.1 Sol](review-gpt-6-1-sol.md) и
[Grok 4.7](review-grok-4-7.md) на commit `a72fbbc`. Оба вердикта: task-00 принимать нельзя.
Разбор находок и что сделано по каждой: [disposition.md](disposition.md). Номера `F-xx` и `G-xx`
ниже — оттуда.

Репозиторий: https://github.com/VuDZ/RoslynMcpServer, ветка `research/workspace-load-performance`.
Запрос написан при HEAD `1afd494`. Коммиты после первого раунда:

- `baa9f55` — исправления spike и сборки форка;
- `562eb8b` — `disposition.md`, отчёт и прогоны на Linux после исправлений;
- `d934992` — имя офисного решения убрано из документов E0 и из одного теста;
- `72e90d7` — решения владельца по объёму task-00, новая задача task-00b;
- `7a5d41d` — тесты с закреплённым SDK вынесены из main-набора, SDK релиза закреплён (часть B);
- `7bc00ac`, `61a7e14`, `1afd494` — прогоны на Windows и в офисе после исправлений.

Решения владельца (не оспаривать, но проверить, что записаны верно): PowerToys исключён из объёма
task-00; утечка BuildHost при ранней отмене принята как ограничение upstream и вынесена в
[task-00b](../../task-00b-early-cancel-buildhost-leak-low.md); перенос research-ветки в `main` — без
её истории.

Главное в этом раунде — **закрыты ли находки первого раунда и не внесли ли исправления новых
ошибок**. Полное повторное ревью форка не нужно: копия upstream и схема подмены в первом раунде
подтверждены обоими ревьюерами.

## Часть A. Исправления task-00

Файлы:
- `spikes/BuildHostFork/`: `ObservationComparer.cs`, `ComparerSelfCheck.cs`, `BuildHostSampler.cs`,
  `ProcessQuery.cs` (бывший `ProcessCommandLine.cs`), `FailureScenario.cs`, `Program.cs`,
  `ProjectMerger.cs` (бывший `ProjectMergerKey.cs`), `BuildHostFork.csproj`,
  `BuildHostForkCommon.ps1`, `Invoke-BuildHostForkBaseline.ps1`, `Invoke-BuildHostForkScenarios.ps1`;
- `forks/roslyn-buildhost/`: `Directory.Build.props`, переименованные проекты в `build/`, `README.md`;
  `.gitattributes`;
- `docs/workspace-load-performance/research-buildhost-fork/`: `evidence/task-00-fork-baseline.md`,
  `evidence/task-00-review/disposition.md`, `task-00-buildhost-fork-baseline-hi.md`,
  `task-00b-early-cancel-buildhost-leak-low.md`, `README.md`.

Что проверить:

1. **Разбор находок.** По каждой строке `disposition.md`: исправление действительно закрывает находку
   и не сужает её. Отдельно оцени отклонённые G-09 (`Invoke-Native` и stderr в Windows PowerShell 5.1)
   и G-10 (`global.json`) — верны ли доводы. Если ты автор находки первого раунда, проверь свою.

2. **`ObservationComparer` против `spikes/ParallelLoad/compare.py`.**
   - Эталон — раунд 1 оригинала; естественный разброс — только различия раундов 1 и 2 оригинала;
     различие допускается, если новое значение — одно из двух значений поля в этих раундах;
     диагностики — симметрическая разность с присутствием как значением.
   - Раунды 3+ оригинала проверяются тем же правилом; прогрев участвует только в проверке ответа
     поиска и процессов.
   - Есть ли путь, при котором реальное отличие форка попадает в natural или пропадает: поле только
     у одной стороны, проект только у одной стороны, пустая строка против `null`, совпадение ключа
     проекта и сообщения диагностики, один измеренный раунд (`-Runs 1`).
   - Проверки процессов (`ProcessIssues`): папка host, `RemainingBuildHosts`, `unidentified`; поведение
     на JSON старого формата без этих полей. Не делает ли это сравнение старых результатов ложно
     зелёным.
   - Покрывает ли `ComparerSelfCheck` нужные случаи; каких не хватает.

3. **`BuildHostSampler` и `ProcessQuery`.**
   - Учёт только потомков процесса spike: разбор `/proc/<pid>/stat` (имя процесса со скобками и
     пробелами), `PROCESS_BASIC_INFORMATION` через `NtQueryInformationProcess` (класс 0, раскладка на
     x86/x64/ARM64), обход до трёх уровней, защита от переиспользованного PID через время старта и
     допуск 1 с.
   - Нечитаемая командная строка у потомка — `unidentified`, не host и не цель убийства.
   - `Kill` и `WaitForExitAsync` сверяют время старта; `Stop()` дожидается идущего опроса.
   - Гонки: процесс, появившийся и завершившийся между опросами; родитель, завершившийся раньше опроса
     потомка (у дочернего процесса BuildHost родитель — сам spike, но проверь общий случай).

4. **Сценарии.**
   - Задержка по умолчанию — половина неотменённой загрузки оригинала после появления host. Может ли
     она дать неубедительный сценарий, который скрипт не отметит.
   - Ожидаемые исходы и проверка пар. После убийства host принимаются оба исхода (ошибка или
     завершение новым host) и исходы сторон не сравниваются, потому что в upstream исход зависит от
     момента (см. `disposition.md`, «Найдено при исправлениях»). Не ослабляет ли это обнаружение
     регрессии форка сверх необходимого; как проверить сильнее без потери устойчивости.
   - Одинаковый остаток host у обеих сторон — запись «upstream», не провал. Корректно ли это для
     task-00.
   - Вырезание имени и путей для решения вне репозитория; сокращение сообщения ошибки до типа.
   - Совместимость с Windows PowerShell 5.1 и StrictMode 2.0; коды выхода обоих скриптов.

5. **Сборка форка.** Переименование проектов и `deps.json`, `Features=strict`, `WarningLevel=9999`,
   `.gitattributes` для `forks/roslyn-buildhost/upstream/**`. Не потеряно ли что-то из первого раунда.

6. **Отчёт и статусы.**
   - Числа разделов «после ревью» согласованы со сводками, README и task-файлом; выводы не сильнее
     данных (CPU и время на машине с 2 vCPU, первый поиск форка медленнее на 2,6–3,4 %, два процесса
     BuildHost в офисе).
   - Список «Что не сравнивается» полон.
   - Объяснение утечки при ранней отмене и ссылка на исправление upstream (`dotnet/roslyn`
     `2624f5c156`) верны; правка действительно в клиенте и новее пакета 5.9.0.
   - Объяснение недетерминированного исхода убийства host согласуется с upstream
     (`BuildHostProcessManager`, `MSBuildProjectLoader.Worker`).
   - Готов ли task-00 к приёмке в записанном объёме; если нет — что блокирует. Статусы — по разделу
     «Task status bookkeeping» в `AGENTS.md`.

7. **Публикация.** В tracked-файлах ветки нет имени офисного решения, его путей и имён проектов;
   допустимы агрегаты. История Git по решению владельца не переписывается — оцени только текущие
   файлы.

8. **Стиль.** Новый и изменённый C# в `spikes/BuildHostFork/` и `RoslynMcpServer.Tests` — по
   `docs/code-style.md` (прочитать полностью); `forks/` — по стилю Roslyn. Старый код, который эти
   коммиты не меняли, не оценивать.

## Часть B. SDK тестов и релиза (`7a5d41d`)

Не входит в task-00, но изменение затрагивает CI и правила проверки, а независимо его никто не
смотрел. Файлы: `RoslynMcpServer.Tests/Support/TestCategories.cs`, `TestCategoryInventory.cs`,
`PlatformSupport/WindowsOnlyTestInventoryTests.cs`, `WorkspaceLoadCache/PinnedSdk.cs`,
`PinnedSdkTestInventoryTests.cs` и помеченные тесты в `WorkspaceLoadCache/`;
`.github/workflows/test-suite.yml`, `release.yml`; `AGENTS.md`; `docs/ARCHITECTURE.md`;
`docs/architecture-review/README.md`, `findings.md` (AR-13, AR-18);
`docs/backlog/workspace-load-cache/README.md`.

1. В категорию `RequiresPinnedSdk` попали ровно тесты, которым нужен SDK 10.0.300; ни один тест,
   который без него проходит и проверяет что-то другое, не исключён. Замена строки `10.0.300` на
   `PinnedSdk` не изменила байты фикстур (`global.json` с переводом строки и без).
2. Рефакторинг `WindowsOnlyTestInventoryTests` на общий хелпер не изменил его поведение.
3. CI: фильтры обоих unit-шардов, удаление 10.0.300 из `setup-dotnet` — не нужен ли он ещё
   каким-то тестам, в том числе lifecycle-шардам.
4. Release: `global.json` пишется в checkout до `setup-dotnet` с `global-json-file`; собирается ли
   релиз ровно 10.0.402 на Windows и Linux runner'ах, если на них предустановлен более новый SDK.
5. AR-18: подтверди или опровергни по коду (`GlobalJsonSdkReader`, `DotNetSdkEnvironment`,
   `SdkMismatchDiagnostics`, `MsBuildBootstrapper`), что сервер игнорирует `rollForward` и выдаёт
   ложный `MCP_MSBUILD_SDK_MISMATCH`. Отчёт пишет, что это вывод по чтению кода, не воспроизведённый.
   Если можешь, воспроизведи. Оцени, оправдано ли из-за этого откладывать корневой `global.json`.

## Что можно запустить

С .NET SDK 10 и PowerShell 7 из корня репозитория:

- `dotnet build spikes/BuildHostFork -c Release` и
  `dotnet spikes/BuildHostFork/bin/Release/net10.0/BuildHostFork.dll check`;
- `pwsh spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1` (по умолчанию `RoslynMcpServer.sln`);
  второй запуск подряд проверяет F-01;
- `pwsh spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1 -Solution RoslynMcpServer.sln -Runs 3`;
- main-набор: `dotnet test RoslynMcpServer.sln -c Release --filter "Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk"`
  (на Linux ещё `&Category!=RequiresWindows`). На Linux с маленьким `/tmp` задай `TMPDIR` в каталог с
  местом: тесты плагинов копируют файлы во временную папку.

Укажи, что именно запускал и на какой ОС. Если не запускал, напиши прямо. Сырых результатов прогонов
в репозитории нет (`results/` исключены), офисные данные не публикуются: доступны только сводки в отчёте.

## Формат отчёта

1. **Вердикт:** закрыты ли находки первого раунда; можно ли принять task-00 в записанном объёме;
   отдельно — часть B. Если нет — что блокирует.
2. **Находки** по убыванию серьёзности: blocker / major / minor / nit. Для каждой: файл:строка; что
   не так; конкретный сценарий, при котором это ломает вывод или поведение; доказательство (цитата
   кода, ссылка на строку upstream, команда); предлагаемое исправление. Отметь, новая ли это находка
   или незакрытая из первого раунда (с её номером).
3. **Проверено и в порядке:** пункт и как проверено.
4. **Не проверено** и почему.

Не выдавай предположение за факт. Если вывод сделан без чтения кода или без запуска, пометь его как
непроверенный.
