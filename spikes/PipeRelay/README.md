# Spike локального транспорта и MCP-фасада

Стенд [C/E0/task-03](../../docs/workspace-host-process/epoch-0-design-and-spike/task-03-pipe-relay-spike-med.md):
один общий счётчик, отдельные MCP-сессии, SDK 2.2.0, .NET 10.
Production, solution, CI, publish и глобальная конфигурация MCP не меняются.

```powershell
dotnet build spikes/PipeRelay/PipeRelay.csproj -c Release
dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll verify all
```

`verify sessions` — сокращённый прогон без отрицательных transport/ACL проверок.
Полный результат — `bin/Release/net10.0/checks/result.json`; рабочие каталоги
каждого fixture имеют новые UUID. Успешный прогон проверяет обе модели и обе
версии протокола, два одновременных SDK-клиента, отрицательные сообщения,
смерть host и cleanup. Ожидается exit code 0.
На этой машине Windows sandbox не позволил соединение `CurrentUserOnly`;
положительный контроль и финальные прогоны выполнены обычной Windows identity.
Защита канала при этом не снималась.

## Режимы

- `auto <pipe> [ready-file]` — stdio MCP-фасад с фиксированным remote host.
- `auto <pipe> <ready-file> --local-host` — тот же фасад одновременно владеет
  счётчиком и обслуживает remote proxy; роль явно выбирает runner.
- `host <pipe> [ready-file]` — отдельный host без локального stdio-клиента.
- `direct` — контрольный счётчик напрямую по stdio без IPC.

Без аргументов используется `auto` с именем канала из identity сборки.
Новый режим task-04 описан ниже. TTL процесса снят; deadlines и cleanup принадлежат runner.

Три инструмента фиксированного фасада: `count`, `slow(seconds=1..120)`, `toggle`.
`toggle` меняет только свою коллекцию и показывает диагностический inventory
сборок/GC/PID; включаемый `extra` выполняется локально. Roslyn и MSBuild отсутствуют.
Чаты в стенде не являются сущностями или идентификаторами подключения.

## Linux / WSL

Можно выполнить те же framework-dependent DLL на Linux runtime .NET 10:
скопировать только файлы DLL/JSON из build output в отдельный каталог
**под `spikes/PipeRelay/artifacts/`**, затем `dotnet /absolute/path/PipeRelay.dll verify all`.
Не копировать C# исходники в корневой `artifacts/`.

Изолированная проверка разных UID запускается от root, используя существующие
UID владельца (по умолчанию 1000) и `nobody`; аккаунты не меняются:

```sh
python3 spikes/PipeRelay/check_unix_access.py /absolute/path/PipeRelay.dll /absolute/path/linux-access.json 1000
```

Скрипт создаёт свой временный каталог, запускает host под UID владельца,
проверяет Unix socket и порядок «владелец → nobody → владелец», затем убирает
свой host, socket и временные файлы. Общий каталог с DLL должен читаться владельцем.
Windows-контроль проверяет owner-only DACL и отказ под anonymous Windows identity,
затем восстанавливает identity runner.

Метод измерений, границы handshake/фреймов, найденные проблемы, результаты и
ограничения для task-04 — в [отчёте](../../docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-03-pipe-relay-spike.md).

## Маршрутизация и recovery (task-04)

```powershell
dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll routing-checks all
```

`quick` сокращает cold/recovery с 10 до 1 повторов на версию MCP.
Raw полной серии — `bin/Release/net10.0/routing-result.json`, события включают cleanup.
Это отдельный режим; старые fixed-host проверки task-03 сохранены как контроль.
`verify recovery-red` ожидаемо завершается с кодом 1: фиксированный фасад не восстанавливается.

- `route <options.json>` — stdio-фасад с автоматическим discovery, `switch(optionsFile)` и `effect(replyBarrier)`.
- `routed-host <options.json>` — внутренний дочерний host из проверенной теневой копии.
- Options: абсолютный `Root`, эффективный `Descriptor`, `Context`; остальные поля `RouteOptions` управляют только fake load и barriers runner.

Root задаётся fixture, на Unix имеет 0700 и не зависит от TMPDIR.
Канал и locks строятся от одного root; Unix absolute socket path ограничен 103 байтами.
Host живёт до последнего подключения плюс 1 с grace; до первого подключения ждёт 8 с.
Shared load не отменяется отдельным waiter; при подключённых proxy продолжается и без waiter.
Неуспешный load кешируется в поколении, startup failure даёт общий cooldown 2 с.
MCP 2026 subscriptions отменяются по настоящему EOF. Runner использует собственные процессы
и `StreamClientTransport`: SDK stdio cleanup может подменить EOF убийством дерева.
macOS не поддержан этим контролем FileStream.Lock. GC — обычный runtime default
стенда; замеры памяти task-03 не повторяются и не переносятся на новые процессы.

Linux peer-UID контроль при открытом всем сокете:

```sh
python3 spikes/PipeRelay/check_unix_access.py /absolute/path/PipeRelay.dll /absolute/path/access.json 1000 --world-socket
```

Контракт, найденные ограничения и данные — в [task-04](../../docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-04-routing-and-recovery-spike.md).

## Стоимость identity образа (task-06)

```sh
dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll identity-bench <source> <scratch> <out.json> <runs> [excluded top-level dir...]
```

Замер для одного file set сервера: stat-скан, полный SHA-256 манифест, копирование
с хешем и fsync, повторный хеш копии, повторное использование записанного манифеста.
Каждый замер выполняется `runs` раз в холодном и тёплом кеше. На Linux холодный кеш
даёт `posix_fadvise(DONTNEED)` по каждому исходному файлу, без root; на других ОС
только тёплый. Проба меняет один байт копии и восстанавливает mtime: stat-штамп
совпадает, хеш — нет. Scratch, отдельный от source, удаляется после замера; кладите
его на дисковую файловую систему, а не в tmpfs `/tmp`.
Результаты — в [task-06](../../docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-06-environment-and-deployment-preflight.md).
