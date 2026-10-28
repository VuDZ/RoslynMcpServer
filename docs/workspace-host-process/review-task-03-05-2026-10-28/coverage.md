# Покрытие ревью task-03 и task-05, 2026-10-28

## Прочитано

- `AGENTS.md`, task-файлы task-03 и task-05, карта эпохи, README темы,
  строка `docs/README.md`, входные правки task-01 и task-04 (diff `b4d4b68..65bbc94`).
- Полностью: `evidence/task-05-applicability.md`, `evidence/2026-10-28-task-05-scope.md`,
  `evidence/task-03-pipe-relay-spike.md`.
- Код стенда: `spikes/PipeRelay/{Program,Facade,CounterHost,CounterState}.cs`,
  `PipeRelay.csproj`, `Checks/SessionChecks.cs` (отмена), `check_unix_access.py`;
  `spikes/HostSurvival/README.md`, `measure.py` (сессия и замер памяти).
- Данные: `task-05-data/memory.json`, `cohort.json`; `task-03-data/windows.json`
  (память), `tdd.json`.
- Не читались построчно: `Checks/ClientPair.cs`, `TransportChecks.cs`,
  `WindowsAccessCheck.cs`, `BoundedStream.cs`, `LocalHandshake.cs`, `BuildIdentity.cs`,
  `codex_lifecycle.py`, `HostSurvival/Program.cs`, JSONL жизненного цикла
  и сабагентов (сверены только ссылки и сводки).

## Проверено и запущено

- Пересчёт медиан GC-таблицы и сумм cohort из JSON (Python) — совпали.
- Декомпиляция .NET 10.0.13 `System.IO.Pipes`: `PipeStream.GetPipePath` (префикс
  от `Path.GetTempPath()`, поддержка абсолютного имени) и
  `NamedPipeServerStream.HandleAcceptedSocket` (`GetPeerID` при `CurrentUserOnly`).
- Декомпиляция `ModelContextProtocol.Core` 2.2.0: `McpSessionHandler.SendRequestAsync`
  и `RegisterCancellation` (fire-and-forget `notifications/cancelled`).
- Ссылки Markdown всех документов темы, `docs/README.md` и двух README стендов:
  502 ссылки. Битые — две на `C:/Users/...` (F-03); 19 ссылок вида `file:line` —
  особенность скрипта проверки, файлы существуют. Номера строк пяти ссылок
  на production-код из task-05 и четырёх на код стенда проверены вручную.
  Одна указывает на пустую строку (F-03).
- **Собственный замер GC** ([данные](gc-probe-linux.json)): Linux 7.0.0-34,
  3 GiB RAM, runtime 10.0.13, сервер 1.5.18 из `bin/Release` HEAD `65bbc94`,
  `RoslynMcpServer.sln` Release, full profile, по одному прогону на режим GC.
  Stdio-драйвер на Python и startup hook вне репозитория (`~/tmp/reviewgc`).
  Hook по файлу-триггеру вызывает 2× `GC.Collect(2, Forced, blocking, compacting)`
  с LOH `CompactOnce` (forced), затем `GC.Collect(2, GCCollectionMode.Aggressive)`,
  и пишет `GC.GetGCMemoryInfo`. Этапы: пустой 10 с → forced → load → поиск
  `SolutionManager` → idle 30 с → forced → reset → 30 с → forced → aggressive → 20 с.
  Счётчики — `VmRSS`/`RssAnon`/`RssFile` из `/proc/<pid>/status`. Linux-счётчики
  не равны Windows private bytes, поэтому сравниваются доли, а не абсолютные
  значения с task-05. Hook сам по себе меняет процесс (поток опроса), но его вклад
  виден в пустом замере.

## Не сделано

- Windows, Codex и Cursor недоступны: Job/breakaway, сабагенты, выход desktop
  и Windows ACL не воспроизводились.
- task-03 runner (`verify all`) не перезапускался; Release build и main suite
  исполнителя не повторялись: production и стенд с их прогона не менялись,
  ревью меняет только документы.
- Причина остатка 20–25 MiB живого heap после reset не исследовалась (нужен gcdump).
- Документы и код задач рецензент не правил.
