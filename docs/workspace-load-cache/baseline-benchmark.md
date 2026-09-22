# Baseline benchmark — workspace load

Статус: **исполняемый runbook** для снятия текущего baseline. Runtime кеша нет.
Токен сверки со скриптом: `baseline-benchmark-1`.
Инструмент: [baseline_bench.py](baseline_bench.py). Его пины, таймауты и правила
выборки совпадают с этим файлом. Расхождение чинится по этому файлу.

Прогон нужен, чтобы на двух закреплённых публичных деревьях увидеть задержку
`load_workspace` и первого `find_symbol_definition` в новом процессе, снять
один CPU-профиль и оставить отчёт, с которым потом сравнивается кеш.
Это ещё не закрывает [U-ARB-03](UNRESOLVED-v2.md#u-arb-03--репрезентативная-нагрузка-и-численный-budget):
численный budget, hit-rate и public activation здесь не утверждаются.

## Что сделать агенту

1. Работать на Windows x64. Другая ОС — отдельный отчёт, в эту медиану не входит.
2. Не вызывать `load_workspace` этих корпусов из сессии Cursor. Таймер — только
   `baseline_bench.py`: новый процесс `RoslynMcpServer.exe`, stdio MCP, часы
   вокруг `tools/call`. Ответ `load_workspace` длительность не возвращает.
3. Не менять продукт, версию, `global.json` корпусов, пины SHA, таймауты и
   состав median. Не добавлять в скрипт флаги, которыми это можно переопределить.
4. Не передавать в `load_workspace` файл `.slnf`. Не включать
   `shadowCopyInSolutionAnalyzers`. Не вызывать `dotnet build` / `dotnet test`
   на корпусах. Restore входит в скрипт и в тайминг load не входит.
5. Запустить скрипт. Если предусловие SDK не выполнено — одна починка из
   раздела «SDK» и один повторный запуск. Клон с тем же SHA переиспользуется.
6. В ответе указать путь к `report.md`, compare key каждого корпуса и таблицы
   Warm median как в файле. Числа руками не пересчитывать. U-ARB-03 не закрывать
   и `UNRESOLVED-v2.md` не править.

## Корпуса

Медианы не складываются. Узкое место одного не переносится на другой.

| id | Репозиторий | Pin | Файл для `load_workspace` | Зачем |
|---|---|---|---|---|
| `orchard-wide` | `https://github.com/OrchardCMS/OrchardCore.git` | tag `v3.0.1`, SHA `b9c4b2f23e56ef11fbdbd28603c871d1b0fc9deb` | `OrchardCore.sln` | широкий SDK-style граф, Razor, `Directory.Build.props` |
| `roslyn-deep` | `https://github.com/dotnet/roslyn.git` | SHA `cf91b80aa2f0eb5b64d7bdb544170d9744883f72` (main на 2026-09-22; GitHub Release roslyn для пина не используется) | `src/Compilers/CSharp/Portable/Microsoft.CodeAnalysis.CSharp.csproj` | один толстый проект: parse и первый semantic |

Оба дерева публичные, без секретов. Клон — `git fetch --depth 1 origin <sha>`
в каталог вне репозитория RoslynMcpServer. После checkout `git rev-parse HEAD`
обязан совпасть с пином.

Символы первого semantic-вызова, по порядку, в том же процессе:

- orchard-wide: `ShellSettings`, затем `ManifestConstants`
- roslyn-deep: `CSharpCompilation`, затем `Binder`

Успех semantic — текст с `Found ` и `declaration symbol(s) matching`.
Время промаха по первому имени в `useful` не входит. Входит время имени,
которое нашлось.

## Нормативные константы

| Константа | Значение |
|---|---|
| `WARM_ATTEMPTS` | 10 |
| `CONSECUTIVE_FAILURES` | 3 |
| `RESTORE_TIMEOUT_S` | 3600 |
| `LOAD_TIMEOUT_S` | 2700 |
| `SEMANTIC_TIMEOUT_S` | 1200 |
| `INIT_TIMEOUT_S` | 120 |

Сервер каждого замера: `bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe`
относительно корня этого репозитория, либо `ROSLYN_MCP_SERVER`, либо `--server`.
Прогон сам publish не делает и процесс IDE не завершает. Если exe нет — остановиться
и написать это в ответе.

Клоны: `<родитель-репозитория>/roslyn-mcp-bench/clones/<id>` (`--bench-root`).
Отчёт: `artifacts/workspace-load-baseline/<stamp>/` внутри репозитория (`--out`).
Каталог `artifacts/` уже в `.gitignore`. Отчёт не коммитить, пока об этом не попросили.
На томе клонов нужно ≥ 20 ГиБ свободно, на томе отчёта ≥ 4 ГиБ. Иначе скрипт
выходит с кодом 1 и отчёт не пишет.

## Предусловия

- Python 3.11+ (`python` или `py -3`), `git`, `dotnet` в `PATH`.
- Опубликованный `RoslynMcpServer.exe` (см. выше).
- `dotnet-trace` в `PATH` желателен (один CPU-профиль на корпус). Если его нет,
  скрипт пропускает `profiled-warm` и пишет это в отчёт. Ставить его можно
  (`dotnet tool install -g dotnet-trace`) до запуска; в тайминг load это не входит.

### SDK

Скрипт вызывает `dotnet --version` с cwd = корень клона, чтобы сработал
`global.json`. Ненулевой код или текст про ненайденный SDK — blocker корпуса.

Один раз разрешено докачать **ровно** `sdk.version` из этого `global.json`
в `<bench-root>/dotnet` скриптом `dotnet-install.ps1` и повторить прогон
с `DOTNET_ROOT` и этим каталогом в начале `PATH`. Другую версию SDK не ставить.
`global.json` корпуса не менять. Повторный прогон переиспользует клон с тем же SHA.

Restore (`dotnet restore` на тот же файл, который грузится) делает скрипт.
Лог: `<out>/<id>/restore.log`. Ненулевой код — blocker корпуса, замеры не
начинаются. NuGet.config и пакеты руками не подменять.

## Протокол одного замера

Новый процесс, cwd и `ROSLYN_MCP_WORKSPACE` = корень клона.

1. `startupMs` — от старта процесса до ответа `initialize`. В warm median load
   не входит. В отчёте лежит отдельно.
2. `loadMs` — от отправки `tools/call load_workspace` до ответа.
   Аргументы: абсолютный `workspacePath`, `briefOutput=true`.
   `configuration` / `platform` не передаются.
   `targetFramework` передаётся только после правила ниже.
3. `semanticMs` — успешный `find_symbol_definition`.
4. `usefulMs` = `loadMs + semanticMs`, когда оба успешны.
5. `pidMs` = `startupMs + usefulMs`.

`loadOutcome=success` — в тексте есть `Successfully loaded workspace.`
`graph-only` (граф открыт, semantic недоступен) в median не входит.
Пик working set снимается снаружи, раз в 2 с, на всё время процесса.

Серии, по порядку:

1. `post-restore` — первый процесс после restore. В warm median не входит.
2. Если этот ответ — `Workspace Load Failed (missing Compile target)` и в нём
   перечислены TFM, один повтор в новом процессе. `targetFramework` =
   `net10.0`, если он есть в списке, иначе первый из списка. Дальше все замеры
   корпуса идут с этим значением. Второй TFM не перебирать.
   `VS 2026 / MSBuild 18 BuildHost` и прочие отказы не перебирать другим файлом.
3. Если load `post-restore` так и не стал `success`, корпус останавливается.
   Профиль и warm не запускаются.
4. `profiled-warm` — один процесс, только если `dotnet-trace` есть в `PATH`.
   CPU sampling с момента после `initialize` до конца semantic. В median не входит.
   Рядом пишутся `.nettrace` и `*-trace.txt` (`dotnet-trace report ... topN -n 25`).
5. `warm` — до 10 новых процессов. Файловый кэш ОС не сбрасывается.
   Эти замеры не называть cold. Три неуспеха подряд останавливают серию.

Median и p95 считаются только по `series=warm`.

- load: `loadOutcome=success`
- semantic: `semanticOutcome=success`
- useful: оба успеха

p95 — nearest-rank, индекс `ceil(0.95 * n) - 1` после сортировки по возрастанию.
При n=10 p95 совпадает с max; так и записывается. Если успешных значений меньше 10,
строка помечается `insufficient`, median в текст всё равно кладётся рядом со счётчиком,
и для доли semantic используются только строки с n ≥ 10.

Доля semantic — `semantic median / (load median + semantic median)` при обеих
выборках n ≥ 10.

## Результат

Каталог прогона:

| Файл | Содержимое |
|---|---|
| `report.md` | текст для человека |
| `attempts.jsonl` | один JSON на замер, миллисекунды как числа |
| `workload.json` | метаданные машины, инвентарь, те же замеры |
| `<id>/restore.log` | stdout/stderr restore |
| `<id>/*-load.txt`, `*-semantic-*.txt` | тела ответов, обрезка 2 000 000 символов |
| `<id>/*-trace.txt`, `*.nettrace` | профиль, если снят |

В отчёте по корпусу есть SHA, `global.json`, `dotnet --version`, число `.csproj`,
сколько из них содержит `TargetFrameworks`, Razor/Web SDK, `OutputItemType="Analyzer"`,
наличие `Directory.Build.props` и `NuGet.config`, число project instances из ответа
load и compare key `sha|dotnet --version|targetFramework`.

Сравнивать два отчёта можно при совпадении compare key **и** file version
`RoslynMcpServer.exe` из шапки. Разные корпуса и разные версии сервера
в одну медиану не объединять.

Скрипт печатает последней строкой `REPORT <абсолютный путь к report.md>`
и возвращает 0, если отчёт записан, даже когда у корпуса есть blocker.
Код 1 — нет Windows, нет exe, нет `dotnet`, мало места. Это тоже результат,
его нужно принести как есть.

## Запуск

Из корня репозитория RoslynMcpServer:

```powershell
python docs/workspace-load-cache/baseline_bench.py
```

Если `python` не найден:

```powershell
py -3 docs/workspace-load-cache/baseline_bench.py
```

Повтор одного корпуса после починки SDK: `--corpus orchard-wide` или
`--corpus roslyn-deep`. Оба корпуса — без флага или `--corpus all`.

Каталог с `.git`, чей HEAD не равен пину, скрипт не удаляет. Удалить такой
каталог можно только когда blocker говорит `refusing to delete` или оборванный
fetch, затем запустить скрипт снова.

## Что сознательно не входит

Сценарии verification §5 про cache miss, disk/RAM hit, edit, build и live
здесь не выполняются: писать нули hit-rate нельзя. Их снимет тот же клиент
после появления кеша, отдельным изменением протокола, не этим прогоном.
