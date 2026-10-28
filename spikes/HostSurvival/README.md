# Probe применимости долгоживущего workspace

Изолированный стенд [C/E0/task-05](../../docs/workspace-host-process/epoch-0-design-and-spike/task-05-applicability-checks-med.md).
Production, solution, CI и publish не меняет. Требуется .NET 10; замеры памяти — Windows и Python 3.
Использует существующие stdio/Win32 memory helpers из `scripts/workspace-state-cache/ordinary_baseline.py`.

```powershell
dotnet build RoslynMcpServer.sln -c Release
dotnet build spikes/HostSurvival/HostSurvival.csproj -c Release
dotnet build spikes/HostSurvival/InventoryHook/InventoryHook.csproj -c Release

python spikes/HostSurvival/measure.py lifecycle --out artifacts/host-survival/control-new
python spikes/HostSurvival/measure.py memory --out artifacts/host-survival/memory-new
python spikes/HostSurvival/measure.py cohort --out artifacts/host-survival/cohort-new
```

Каждый `--out` должен быть новым каталогом. `memory` занимает около 16 минут:
три свежих процесса на режим GC, чередование режимов, 10 секунд после initialize,
load и ещё 10 секунд, поиск `SolutionManager`, 60 секунд idle, reset и ещё 60 секунд.
Workspace по умолчанию — решение этого checkout, Configuration=Release, full profile,
обычные defaults prewarm/BuildHost. Остальные параметры load опущены.
Числа — только для PID MCP, без дочерних BuildHost и без принудительного GC/trim.

`cohort` одновременно удерживает 20 MCP: восемь копий решения, восемь копий
`samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`, четыре без workspace. Все — Server GC;
замер после минимум 60 секунд без запросов. Это синтетическое распределение.

Inventory сборок запускается отдельно от измерений памяти через `DOTNET_STARTUP_HOOKS`.
Hook через 20 секунд сохраняет `GCSettings.IsServerGC`, runtime и имена загруженных
сборок. В production/control измерениях hook отсутствует. Полный environment,
чужие command lines и содержимое stderr не записываются в evidence.

## Настоящий Codex app-server без вызова модели

Указать путь установленного `codex.exe`:

```powershell
python spikes/HostSurvival/codex_lifecycle.py --codex C:/path/to/codex.exe --out artifacts/host-survival/codex-new
```

Только `initialize` и `mcpServerStatus/list(serverName=host_survival)`.
Чатов, subagent и вызовов модели нет. Настройки MCP передаются `-c` только новому
процессу. Глобальный конфиг и рабочий Codex не меняются. Schema установленного CLI
проверялась через `codex app-server generate-json-schema`.

Сценарии: завершение временного discovery; EOF stdin app-server; kill app-server.
Для последних двух `serve <directory> hold` удерживает `resources/list` до EOF,
не блокируя наблюдение EOF. Перед событием проверяется жизнь MCP и обоих детей.
Проверка через две секунды после выхода harness даёт верхнюю границу времени
завершения MCP, а не точную задержку kill. Timeout probe — десять минут.

## Дополнительные ручные события desktop harness

Историческая инструкция для расширенного прогона. По
[уточнению scope от 2026-10-28](../../docs/workspace-host-process/evidence/2026-10-28-task-05-scope.md)
сравнение клиентов и MCP на чат/окно не входят в task-05; выполнять эту матрицу
для завершения задачи не требуется.

Для каждого события нужен новый каталог, например `artifacts/host-survival/manual-reload`.
Во временной MCP-конфигурации harness зарегистрировать:

```json
{
  "command": "E:/Devel/RoslynMcpServer-workspace-host-process/spikes/HostSurvival/bin/Release/net10.0/HostSurvival.exe",
  "args": ["serve", "E:/Devel/RoslynMcpServer-workspace-host-process/artifacts/host-survival/manual-reload"]
}
```

1. Убедиться, что harness подключился: в JSONL есть `initialize` и `tools/list`.
2. Записать версию harness, время и событие: закрыть только тестовый чат,
   Reload тестового окна, штатный выход либо crash отдельного тестового экземпляра.
3. Через 2 и 10 секунд проверить PID из `start`/`child-start` и рост heartbeat.
   Отметить `stdin-eof`, `console-cancel`, `sigterm`, `process-exit` либо их отсутствие.
   Отсутствие финальной записи само по себе не устанавливает механизм kill.
4. Для количества MCP повторить с двумя чатами и двумя окнами; subagent — только
   при отдельной авторизации. Сохранить число процессов до/после и соответствие PID.
5. Создать пустой файл `stop` в каталоге наблюдения: оставшиеся дети завершатся
   за секунду. Сам MCP завершает harness; страховочный timeout — десять минут.

После завершения убрать только временную регистрацию probe. В evidence включать
лишь JSONL probe и разрешённые поля процесса. Не передавать полный конфиг harness.

## Linux и логи

Для Linux собрать те же три файла (`Program.cs`, `WindowsProcess.cs`, csproj) в
отдельном каталоге **под `spikes/HostSurvival/artifacts/`**, затем:

```sh
python3 spikes/HostSurvival/measure.py lifecycle --probe /absolute/path/HostSurvival --out /absolute/path/new-control
```

Обычный ребёнок сохраняет session/group; второй запускается через `setsid` с
отдельными stdio. Это контроль ОС, поведение Cursor/Codex он не доказывает.

Санитизированное чтение существующих логов:

```powershell
python spikes/HostSurvival/measure.py inventory --logs C:/path/to/publish/logs --out artifacts/host-survival/logs-new
```

Сохраняются только session token, timestamps, версия и whitelist параметров load.
Интервалы записей не доказывают одновременную жизнь процессов; короткий session
suffix может повториться. Первичные наблюдения и ограничения — в
[отчёте](../../docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-05-applicability.md).
