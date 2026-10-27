# D/E0/task-10 — Диагностика первого поиска: реализация и validation

Дата: **2026-10-23**. Статус: **implemented; validation на Linux пройдена;
проверки под Windows (PowerShell 5.1, end-to-end скрипта замера) pending**.
Исполнитель: claude-opus-5-5; независимое ревью не проводилось.
Задача: [task-10](../task-10-first-search-exception-and-trace-diagnostics-med.md).

## Что сделано

- Сервер **1.5.11**: `SearchExceptionRecorder` на время цикла поиска подписывается
  на `AppDomain.FirstChanceException`. Текущий проект хранится в `AsyncLocal`;
  исключение относится к проекту только в потоке его поиска, остальное считается
  unattributed. Группы — тип, проект, имя файла или сообщение I/O (до 300 символов);
  не больше 200 групп, в лог — 20 крупнейших. Строка `workspace_search_exceptions`
  пишется перед `workspace_search_summary`, в том числе при cancellation и failure.
  Ответы инструментов не меняются.
- `Measure-WorkspaceLoad.ps1`: разбор новой строки с редактированием путей, секция
  в `summary.md`, надпись для старого сервера; параметр `-TracerExe` добавляет
  отдельный trace-прогон первого поиска (вне медиан).
- `Invoke-FirstSearchDiagnostics.ps1`: publish сервера из дерева в
  `artifacts/first-search-diagnostics/server-<commit>`, `dotnet-trace` 10.0.750501
  в локальную папку `tools`, запуск замера; `-TracerExe`, `-NoTrace`, `-PrepareOnly`.
- [Протокол](task-00-protocol.md) дополнен командой офисного прогона.

## Как останавливается trace

Проверено на `dotnet-trace` 10.0.750501:

- С перенаправленным выводом строка «Press &lt;Enter&gt;» не печатается, а Enter в stdin
  trace не останавливает; SIGINT останавливает, но на Windows послать Ctrl+C одному
  дочернему процессу из PowerShell ненадёжно.
- Если цель завершается, trace заканчивается сам («Trace completed»), а rundown
  при остановке runtime сохраняется: `dotnet-trace report … topN` показывает имена
  управляемых методов.

Поэтому скрипт ждёт строку `Output File`, выполняет поиск, штатно закрывает сервер
и ждёт завершения `dotnet-trace`. Файл покрывает и время остановки сервера.

## Validation

| Проверка | Результат |
|---|---|
| Focused: `DeclarationSearchTimingTests`, `SearchExceptionRecorderTests`, resolver и equivalence | 25/25 |
| Нечитаемый документ (TextLoader с IOException) | группа `System.IO.IOException` проекта, count 6, attributed 6 |
| Release build решения (MCP `run_dotnet_build`) | PASS |
| Main CI `Category!=AnalyzerLifecycle&Category!=RequiresWindows`, Release, Linux (MCP `run_test_by_filter`, SDK из `~/.dotnet`) | 1381 passed / 0 failed / 0 skipped; SourceStructure входит |
| `Test-MeasureWorkspaceLoad.ps1`, PowerShell 7.6.8 (Linux) | PASS |
| `Test-MeasureWorkspaceLoad.ps1`, Windows PowerShell 5.1 | **не выполнялся**: Windows недоступна |
| Обёртка `-PrepareOnly`, Linux | PASS: publish linux-x64, установка и повторное использование `dotnet-trace` |
| Опубликованный сервер + `dotnet-trace` на Smoke (MCP-клиент на Python) | PASS: строка сводки в логе, trace 6,9 МБ, имена методов разрешены, tracer exit 0 |
| `Measure-WorkspaceLoad.ps1` end-to-end с `-TracerExe` | **не выполнялся**: скрипт опирается на `Win32_Process` (CIM), нужна Windows |
| Цена обработчика: 200 000 throw+catch IOException, медиана 5 раундов | 2,73 мкс без регистратора; 3,37 мкс с проектом; 3,54 мкс без проекта |

AnalyzerLifecycle не запускался: загрузка, prepare и lifecycle не менялись.

## Границы

Сводка показывает исключения, но не доказывает их причину: происхождение путей
(Compile items, obj, linked, антивирус) разбирается в task-09 по офисным данным.
Trace охватывает только первый поиск отдельного прогона; overhead trace сравнивается
с медианой обычных прогонов того же пакета. Офисный прогон под Windows одновременно
закрывает pending проверки скрипта; при ошибке он фиксируется как failure, не как PASS.
