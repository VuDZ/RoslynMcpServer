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
Выбор владельца, автозапуск, promotion и recovery входят в task-04.
Процессы стенда имеют страховочный TTL три минуты. Fixture останавливает свои
процессы раньше; убитый host проверяется на exit, SDK-клиенты освобождаются.

Три инструмента фасада: `count`, `slow(seconds=1..10)`, `toggle`.
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
