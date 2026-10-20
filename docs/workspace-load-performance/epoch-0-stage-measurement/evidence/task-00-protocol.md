# D/E0/task-00 — Протокол замера для офисного компьютера

Замер не меняет сервер. Скрипт сам запускает опубликованный `RoslynMcpServer.exe`
по stdio, вызывает `load_workspace` и `find_symbol_definition`, останавливает процесс
и разбирает строки, которые сервер уже пишет в `logs/mcp-*.log`.

Скрипт: [`scripts/Measure-WorkspaceLoad.ps1`](../../../../scripts/Measure-WorkspaceLoad.ps1).
Нужен только этот файл и Windows PowerShell 5.1 или PowerShell 7.

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

- корень решения заменён на `<repo>`, профиль — на `<user>`, имя компьютера — на `<machine>`;
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
