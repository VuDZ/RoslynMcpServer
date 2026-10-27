# D/E0/task-00 — Протокол замера для офисного компьютера

Замер не меняет сервер. Скрипт сам запускает опубликованный `RoslynMcpServer.exe`
по stdio, вызывает `load_workspace` и `find_symbol_definition`, останавливает процесс
и разбирает строки, которые сервер уже пишет в `logs/mcp-*.log`.

Скрипт: [`scripts/Measure-WorkspaceLoad.ps1`](../../../../scripts/Measure-WorkspaceLoad.ps1).
Нужен только этот файл и Windows PowerShell 5.1 или PowerShell 7.
Для разбивки по проектам нужны актуальный скрипт и сервер **1.5.8+**.
Для следующего сравнения source-only поиска и чтения второго binlog — **1.5.9**.
В нём сводка дополнительно показывает формат, число project starts и failures
каждого binlog. Распаковывать ZIP целиком в отдельную папку; EXE и DLL должны иметь
одну версию. Команда запуска прежняя, `-ServerExe` указывает на новый EXE.
Сервер 1.4.14 пишет старый формат без session id: этот скрипт не выделяет из него
логи измеряемого процесса. Session id поддерживается начиная с 1.4.16.

## Диагностика первого поиска одной командой (task-10, сервер 1.5.11+)

Этот путь заменяет ручную установку ZIP. Нужны Git-дерево `main`, .NET SDK и Windows PowerShell 5.1 или PowerShell 7.

```powershell
git -C <путь к RoslynMcpServer> switch main
git -C <путь к RoslynMcpServer> pull --ff-only
powershell -ExecutionPolicy Bypass -File <путь к RoslynMcpServer>\scripts\Invoke-FirstSearchDiagnostics.ps1 `
  -Workspace '<путь>\<решение>.sln' -SymbolName <Тип> -Note 'office, warm'
```

Скрипт публикует сервер из этого дерева в отдельную папку
`artifacts\first-search-diagnostics\server-<commit>` (Cursor её не использует),
ставит `dotnet-trace` 10.0.750501 в локальную папку `tools` рядом и запускает
`Measure-WorkspaceLoad.ps1`: прогрев, три замера, RAM hit и отдельный trace-прогон
первого поиска. `-Configuration`/`-Platform`/`-TargetFramework` — как в обычном
`load_workspace`. Без доступа к NuGet: скачать single-file `dotnet-trace.exe`
(https://aka.ms/dotnet-trace/win-x64) и передать `-TracerExe <путь>`; без trace — `-NoTrace`.
`-PrepareOnly` только готовит сервер и `dotnet-trace`, не измеряя.

Результаты — в `artifacts\first-search-diagnostics\<решение>-<время>`. Прислать
`summary.md`: в нём сводка исключений поиска по проектам и итог trace-прогона.
`run-*-find.nettrace` содержит CPU samples и сообщения исключений с путями файлов;
его присылать только там, где это допустимо. Условия ниже («Перед запуском») те же.

## Перед запуском

- Закрыть сборку того же решения в VS/Cursor: параллельный MSBuild искажает время.
- Решение уже восстановлено (`restore`) и, как обычно у коллег, хотя бы раз собрано.
- Выбрать тип из решения для первого запроса, например главный ViewModel.
- Узнать путь к `RoslynMcpServer.exe`, которым пользуется Cursor (`get_mcp_server_info`).

## Прогоны

Подставить свои значения в угловых скобках. `-Configuration` и `-Platform` —
те же, что в обычном `load_workspace`; если их там нет, не указывать.

1. Обычный тёплый прогон: прогрев и три замера в новых процессах, затем RAM hit.

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Measure-WorkspaceLoad.ps1 `
     -ServerExe '<путь>\RoslynMcpServer.exe' -Workspace '<путь>\<решение>.sln' `
     -SymbolName <Тип> -Runs 4 -Note 'office, warm'
   ```

2. Холодный прогон: перезагрузить компьютер, ничего не открывать, сразу запустить
   тот же скрипт с `-Runs 2 -Note 'office, cold after reboot'`. Первый прогон — холодный.

3. Если был `git pull` или переключение ветки с большим diff: один прогон
   `-Runs 2 -Note 'office, first load after pull'` до открытия решения в IDE.

Каждый прогон пишет папку `workspace-load-measure\<решение>-<время>` в текущем каталоге.

## Что прислать

Из каждой папки только `summary.md`. В нём:

- корень Git-репозитория решения (вне Git — папка решения) заменён на `<repo>`,
  клон RoslynMcpServer — на `<server-repo>`, профиль, сегмент пути с именем пользователя
  и `ДОМЕН\пользователь` — на `<user>`, нестандартная папка NuGet — на `<nuget>`,
  имя компьютера — на `<machine>`. До 2026-10-23 заменялась только папка решения,
  и проекты монорепо выше неё оставались с полными путями;
- в конце скрипт проверяет текстовые файлы отчёта на оставшиеся абсолютные пути (кроме
  Program Files и Windows) и печатает предупреждение с примерами. `.nettrace` не обезличивается;
- имя антивируса, CPU, RAM, список SDK и версия сервера;
- environment, токены и содержимое файлов не пишутся.

Файлы `run-*-log.txt`, `run-*-response.md` и `runs.json` остаются у коллег.
Их просить отдельно, если в `summary.md` будет `n/a` или ошибка.

## Как читать `summary.md`

- `open` — от `tools/call` до строки `Analyzer provenance capture finished` минус `ReplayMs`:
  ожидание lock, старт BuildHost, evaluation, design-time build, IPC, построение snapshot.
- `post-open` — execution gate, публикация, diagnostics, input map.
- `watchers` и `response` — старт watchers и формирование ответа.
- `busy cores` в таблице CPU — процессорное время сервера и его потомков (BuildHost,
  `XamlCompiler.exe`, `cmd.exe`), делённое на время вызова. Около 1 — одно занятое ядро.
  Опрос идёт раз в 2 с, последние доли секунды жизни BuildHost могут не попасть.
- Колонки — оценки по меткам лога, не измеренные spans. Пустая метка даёт `n/a`, а не 0.
- В секциях `project timings` — top-10 проектов design-time build и первого поиска,
  top-10 targets/tasks. Полные проектные строки сохраняются в `runs.json`.
- Времена design-time build включают вложенную работу: родительские и дочерние
  contexts пересекаются. `inclusive sum` нельзя складывать с другими стадиями
  или выдавать за wall time. Повторные сборки, конфигурации и TFM объединены по пути проекта.
- `Design-time coverage` показывает полноту наблюдения; при отказе replay остаются
  доступные интервалы и коды отказа. `unattributed remainder` — остаток от измеренного
  MSBuild open после вычитания диапазона меток binlog, а не отдельная измеренная стадия.
  Этот измеренный open охватывает только `OpenSolutionAsync`/`OpenProjectAsync`;
  ранняя колонка `open` — более широкая оценка, включающая setup и другие промежутки.
  Root evaluations могут не попадать в binlog. Отрицательный остаток не подменяется нулём.
- Поиск показывает каждый проект отдельно, число документов, кандидатов и исход.
  Итог охватывает цикл поиска, а `Handler time outside measured search loops` —
  подготовку, форматирование и остальную работу обработчика. При повторной попытке
  после sanitizer строки отличаются по `searchId` и не сливаются.
