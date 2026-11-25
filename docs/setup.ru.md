# Установка и настройка

[English version](setup.md) · [Назад к README](../README.ru.md)

Здесь собраны подробности, которых нет в [README](../README.ru.md): платформы, регистрация в клиентах, профили инструментов, конфигурация, логи и то, как сервер отслеживает правки. Параметры отдельных инструментов — в [справочнике](tools.ru.md).

## MCP и агент за одну минуту

- **MCP-клиент** — Cursor, OpenCode или другой совместимый хост. Запускает сервер и предоставляет его tools модели.
- **AI-агент** — модель и orchestration клиента. Именно агент выбирает tools; он не является частью этого репозитория.
- **RoslynMcpServer** — локальный stdio-процесс. У него нет собственного чата и автономного цикла.
- **`AGENTS.md`** — правила репозитория для агента. MCP-конфиг подключает tools, а этот файл определяет, когда ими пользоваться.

Типовой поток:

`запрос разработчика → агент → MCP tool → Roslyn/MSBuild/ILSpy/dotnet → компактный результат → агент`

## Требования

- MCP-клиент с поддержкой локального stdio-сервера.
- .NET SDK, совместимый с анализируемым solution; учитывайте его `global.json`.
- **.NET 10 SDK** для сборки самого RoslynMcpServer из исходников.
- Git настоятельно рекомендуется для review и отката изменений.

## Поддерживаемые платформы

| RID | Архив релиза | Поддержка |
|---|---|---|
| `win-x64` | `.zip`, self-contained и framework-dependent | Поддерживается |
| `linux-x64` | `.tar.gz`, self-contained и framework-dependent | Поддерживается |
| `osx-arm64` | `.tar.gz`, self-contained и framework-dependent | Нативный CI пройден; smoke релиза ожидается |
| `TEST_HOST`, `linux-arm64`, `linux-musl-x64` | Есть в списке RID проекта и собираются | Не поддерживаются |

Платформы и статус проверок: [релизы](github-releases/README.md), [macOS ARM64](github-releases/task-01-macos-arm64-med.md).

У macOS ARM64 тот же корень архива `RoslynMCP/` и executable `RoslynMcpServer`, что у Linux.
Framework-dependent требует ARM64 runtime .NET 10; обоим вариантам нужен ARM64 SDK, совместимый с вашим проектом.

### Установка релиза Linux

GitHub Releases публикует `RoslynMcpServer-<version>-linux-x64-self-contained.tar.gz` и `RoslynMcpServer-<version>-linux-x64-framework-dependent.tar.gz`. Корень архива — `RoslynMCP/`.

```bash
tar -xzf RoslynMcpServer-<version>-linux-x64-self-contained.tar.gz
```

Хост — `RoslynMCP/RoslynMcpServer`.

- Self-contained требует `libicu` и glibc **2.27** или новее (базовая линия .NET 10 для x64).
- Framework-dependent требует runtime .NET 10. Задайте `DOTNET_ROOT`, если runtime стоит не в стандартном месте (`/usr/share/dotnet`, `/usr/lib/dotnet` или `~/.dotnet`).
- Для анализируемого проекта нужен .NET SDK. SDK из пакетов дистрибутива — best effort.
- Загруженное решение держит disk watcher, и на Linux каждый из них — отдельный экземпляр inotify. `fs.inotify.max_user_instances` по умолчанию равен 128 на все процессы пользователя, поэтому несколько копий MCP, IDE и `dotnet watch` могут его исчерпать; тогда `get_mcp_server_info` показывает отказы старта watcher. Постоянно поднять лимит: `echo 'fs.inotify.max_user_instances = 1024' | sudo tee /etc/sysctl.d/90-inotify.conf && sudo sysctl --system`.

```json
{
  "mcpServers": {
    "roslyn-mcp-server": {
      "command": "/absolute/path/to/RoslynMCP/RoslynMcpServer",
      "env": {
        "ROSLYN_MCP_WORKSPACE": "/absolute/path/to/YourApp"
      }
    }
  }
}
```

## Первый запуск

Готовые архивы лежат в [GitHub Releases](https://github.com/VuDZ/RoslynMcpServer/releases), их установка описана в [быстром старте README](../README.ru.md#быстрый-старт). Ниже — сборка сервера из исходников.

Используйте **published executable**, а не `dotnet run`. Publish self-contained и ReadyToRun, но намеренно не single-file: Roslyn, MSBuild, analyzers и BuildHost загружают сборки динамически.

### 1. Собрать сервер

```bash
dotnet publish RoslynMcpServer.csproj -c Release -r win-x64
```

Для Windows x64 бинарник будет здесь:

- `bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe`

Для Linux/macOS используется тот же относительный layout с выбранным RID.

### 2. Зарегистрировать локальный stdio-сервер

В Cursor откройте **Cursor Settings → Tools & MCP** или создайте `.cursor/mcp.json` в целевом проекте. В Windows внутри JSON удобно использовать `/`:

```json
{
  "mcpServers": {
    "roslyn-mcp-server": {
      "command": "REPO_ROOT",
      "env": {
        "ROSLYN_MCP_WORKSPACE": "LOCAL_ROOT/YourApp"
      }
    }
  }
}
```

`ROSLYN_MCP_WORKSPACE` необязателен, но рекомендуется: укажите корень целевого репозитория, особенно если там есть `global.json`. Для локальных моделей настройте `ROSLYN_MCP_TOOL_PROFILE` / `ROSLYN_MCP_TOOL_GROUPS` из раздела «Профили инструментов».

После изменения конфигурации перезапустите или reload MCP servers.

### 3. Добавить правила агента в целевой репозиторий

Скопируйте или влейте [`AGENTS.md.sample`](../AGENTS.md.sample) в репозиторий приложения как `AGENTS.md`. Специфичные для приложения entry points и команды добавьте туда отдельно. Это не MCP-конфиг: без policy tools могут быть подключены, но агент всё равно выберет обычный текстовый поиск или shell-команды.

### 4. Проверить первую сессию

Попросите агента:

> Используй Roslyn MCP. Вызови `get_mcp_server_info`, затем `load_workspace` с абсолютным путём к solution этого репозитория. Перечисли загруженные проекты и состояние workspace. Файлы не изменяй.

Исправная сессия покажет путь/версию бинарника, активный профиль tools, состояние SDK/restore и хотя бы один проект. Затем проверьте семантический запрос: «найди объявление и использования `SolutionManager`».

Если tools не видны, вызовите `list_tool_groups`. Если не загрузился workspace, следуйте его диагностике, а не переключайтесь на shell `dotnet`.

### Установщик OpenCode

После `dotnet publish` в каталоге publish лежат [`install2opencode.ps1`](../install2opencode.ps1) и [`AGENTS.md.sample`](../AGENTS.md.sample) рядом с исполняемым файлом хоста. На Linux скрипт запускается через `pwsh`, не через bash.

Из корня **целевого проекта** (вашего приложения, не этого репозитория):

```powershell
cd LOCAL_ROOT/YourApp
& "REPO_ROOT/bin/Release/net10.0/win-x64/publish/install2opencode.ps1"
```

Скрипт:

- создаёт или обновляет **`opencode.json`**, регистрируя `RoslynMcpServer` как локальный MCP-сервер (`type: local`, `enabled: true`, **`timeout`: `600000`** мс = 10 минут);
- создаёт или дополняет **`AGENTS.md`**: если правил Roslyn MCP ещё нет, подмешивает текст из `AGENTS.md.sample` (новый файл или append к существующему).

**Таймаут MCP-хоста OpenCode:** по умолчанию OpenCode обрывает MCP `tools/call` примерно через **60 секунд**. Это **не** аргумент тула `timeoutSeconds` у `run_dotnet_test` / `run_dotnet_run`. Без увеличения host timeout длинные build/test падают с `McpError: MCP error -32001: Request timed out` (~60 с), даже если передать `timeoutSeconds: 900`. Укажите `"timeout": 600000` (или больше) в записи сервера — см. [`opencode.json.sample`](../opencode.json.sample). `install2opencode.ps1` проставляет это сам. Если старая сборка OpenCode игнорирует per-server `timeout`, добавьте также `"experimental": { "mcp_timeout": 600000 }`.

Пример блока сервера:

```json
"roslyn-mcp-server": {
  "type": "local",
  "command": ["REPO_ROOT"],
  "timeout": 600000,
  "enabled": true
}
```

| Параметр | Описание |
| --- | --- |
| `-BinaryPath` | Необязательный абсолютный путь к `RoslynMcpServer.exe`. Если не указан — берётся бинарь рядом со скриптом. |
| `-ProjectPath` | Необязательный корень проекта. По умолчанию — текущая рабочая директория. |

После установки перезапустите OpenCode или перезагрузите MCP-серверы.

## Профили инструментов

По умолчанию **`full`** (все публичные тулы). **`lite`** стартует с 16 core-тулов. Дополнительные группы — при старте процесса или, если клиент обрабатывает `notifications/tools/list_changed`, во время сессии.

| Переменная | Значения | Эффект |
| --- | --- | --- |
| `ROSLYN_MCP_TOOL_PROFILE` | `full` (по умолчанию) или `lite` | Стартовый каталог. Пустое/не задано = `full`. |
| `ROSLYN_MCP_TOOL_GROUPS` | `core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (через запятую, без учёта регистра) | Добавляет группы в **`lite` до первого `tools/list`**. В `full` имена только записываются. Неизвестное имя валит старт. `core` в lite уже есть. |

Пример: `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget`

Примеры OpenCode: [`opencode.json.sample`](../opencode.json.sample). Включайте только один сервер.

| Группа | Назначение | Тулов |
| --- | --- | ---: |
| `core` | workspace, поиск символов, build, test, help | 16 (lite по умолчанию) |
| `files` | чтение/поиск/патч с диска, `get_code_skeleton` | 8 |
| `navigation` | иерархия типов, ссылки по известному объявлению, граф вызовов | 3 |
| `editing` | AST, code fixes, format, rename | 8 |
| `decompile` | сторонние сборки | 4 |
| `nuget` | пакеты: list/audit/search/add/remove | 6 |
| `project` | граф solution и rename проекта | 3 |
| `runtime` | `run`, список тестов, сырой `dotnet` | 3 |
| `operations` | логи, scratchpad, stop | 4 |

`list_tool_groups` / `get_tool_help` / `enable_tool_group` — Markdown-справка и runtime-включение. `tools/list` остаётся JSON Schema. Если клиент не обновляет список после `tools/list_changed`, перезапустите с `ROSLYN_MCP_TOOL_GROUPS=<group>`. Компактный `tools/list` в UTF-8 занимает **47 557 байт для 55 инструментов full** и **21 120 байт для 16 инструментов lite**.

## Cursor: как заставить агента реально вызывать tools

Положите **session policy** в app-репозиторий, иначе клиент часто игнорирует MCP-тулы.

**Канонический файл:** [`AGENTS.md.sample`](../AGENTS.md.sample) — скопируйте в приложение как `AGENTS.md` (или влейте во фрагменты `.cursor/rules`). Текст на EN (LLM лучше следуют английским императивам). Это только политика (когда / MCP vs host / запреты); параметры тулов — в Description и в [справочнике инструментов](tools.ru.md).

**OpenCode:** `install2opencode.ps1` (см. блок **OpenCode** выше) сам создаёт или дополняет `AGENTS.md`.

**Maintainers:** при изменении тулов или agent-visible поведения обновляйте **вместе** `AGENTS.md.sample`, [CHANGELOG.md](../CHANGELOG.md) и [справочник инструментов](tools.ru.md). Полный текст AGENTS в документации не дублировать — только ссылка на sample.

Кратко для модели:

- проверить сервер через `get_mcp_server_info`; отсутствующие `lite`-tools — через `list_tool_groups` / `enable_tool_group`
- Опциональный `RoslynMcp.jsonc` читается рядом с exe, в пользовательском каталоге и в cwd. Cwd перекрывает обычные настройки. Для настройки `plugins` используется конфиг рядом с exe или в пользовательском каталоге. `ROSLYN_MCP_WORKSPACE` в начале процесса ставит cwd на корень репозитория. Ключи: `workspace-path`, `configuration`, `platform`, `target-framework`, `max-results`, `preview`, `ripgrep-path`. Нет файла — вызывайте `load_workspace` сами. Первый семантический tool при `workspace-path` грузит решение и ждёт; чужую загрузку не отменяет. `load_workspace` сравнивает только переданные аргументы (опущенные не сбрасывают уже загруженные). Пример — `RoslynMcp.jsonc.sample`.
- `require-solution` и `direct-project-guidance` управляют [загрузкой и сборкой проекта напрямую](#загрузка-и-сборка-проекта-напрямую).
- после `load_workspace` (или lazy из jsonc) объявления C# — MCP `find_symbol_definition` / `find_usages`, не текстовый поиск и не выдуманный `search`
- нет target `Compile` при load — повторить с `targetFramework` (inner TFM); это не SDK mismatch
- VS 2026 BuildHost / `XMakeElements` — это не SDK mismatch; нужен MCP 1.0.35+ или один SDK-style `.csproj`
- текст по файлам — host Grep IDE, не shell grep
- сборка/тесты — только MCP `run_dotnet_build` / `run_dotnet_test`
- запись файлов — IDE: нативные правки; headless: MCP `apply_patch` / AST
- полный текст политики — в `AGENTS.md.sample`

### Загрузка и сборка проекта напрямую

В `RoslynMcp.jsonc` параметр `require-solution` по умолчанию равен `false`. При загрузке `.csproj` или его сборке через `run_dotnet_build` в конце ответа появляется рекомендация использовать решение, даже если операция завершилась ошибкой. Значение `true` требует указать `.sln` или `.slnx`, в том числе для автозагрузки через `workspace-path`. Чтобы собрать один проект через решение, укажите его имя в `projectName`.

Параметр `direct-project-guidance` заменяет стандартную рекомендацию в предупреждении и отказе. Если параметр опущен, используется встроенный текст.

```jsonc
{
  "require-solution": false,
  "direct-project-guidance": "Откройте или соберите YourApp.sln, чтобы применились настройки решения."
}
```

## История agent-tools по версиям

[CHANGELOG.md](../CHANGELOG.md). Правила агента — [`AGENTS.md.sample`](../AGENTS.md.sample).

Rapid TIA появилась в версии **1.6.0**. `estimate_affected_tests` по сохранённым правкам и зависимостям скомпилированных типов выбирает проекты и классы VSTest. При `run: true` инструмент по очереди собирает выбранные проекты, затем запускает тесты параллельно. Если проекты делят ресурсы, передайте `maxParallelProjects: 1`, чтобы собирать и тестировать каждый проект по очереди. См. [справочник](tools.ru.md) и [архитектуру](ARCHITECTURE.md#rapid-tia).

## Каталог пользователя

Конфиг и drop-in plugins хранятся вне заменяемого каталога пакета npx. Обычные настройки сливаются в порядке «каталог exe → пользовательский каталог → cwd». Сервер игнорирует `plugins` в отдельном конфиге cwd и пишет предупреждение при запуске. Порядок поиска плагинов — `exe/plugins`, пользовательские `plugins/`, пути из конфига exe или пользователя, `ROSLYN_MCP_PLUGINS`. Первый прошедший проверку ID имеет приоритет.

- Windows: `%LOCALAPPDATA%\RoslynMcpServer\`.
- Linux: `$XDG_CONFIG_HOME/roslyn-mcp/` (по умолчанию `~/.config/roslyn-mcp/`) для конфига/plugins; `$XDG_STATE_HOME/roslyn-mcp/` (по умолчанию `~/.local/state/roslyn-mcp/`) для логов/статистики.
- macOS: `~/Library/Application Support/RoslynMcpServer/`.

`ROSLYN_MCP_HOME` задаёт общий корень с `RoslynMcp.jsonc`, `plugins/`, `logs/` и `usage/`. `ROSLYN_MCP_LOG_DIR` имеет приоритет для текстовых логов и не меняет место статистики. Переопределения — абсолютные пути. Фактические пути видны в `get_mcp_server_info`. Конфиг и plugins рядом с exe по-прежнему читаются.

## Логи
- Основной лог: `logs/mcp-*.log` в пользовательском каталоге состояния либо в `ROSLYN_MCP_LOG_DIR`.
- Включено логирование входящих JSON-RPC сообщений (`MCP_LOG_INCOMING_RPC`, `MCP_LOG_INCOMING_RPC_MAX_CHARS`).
- **Ответы tools:** в лог пишется однострочная сводка и отдельные строки warning/error (без полного дублирования ответа MCP). `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` — полный текст ответов tools.
- Переменные: `ROSLYN_MCP_WORKSPACE` (корень репо для MSBuild/SDK), `ROSLYN_MCP_TOOL_PROFILE` (`full`/`lite`), `ROSLYN_MCP_TOOL_GROUPS` (`core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations`; см. **Профили инструментов**), `ROSLYN_MCP_LOG_TOOL_OUTPUT=full`, `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS=0` (загрузка без оптимизаций BuildHost сервера: ссылки раньше проекта, evaluation наперёд и общий кеш SDK resolution проекта; по умолчанию включены, побеждает `buildhost-optimizations` в `RoslynMcp.jsonc`), `ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS=<N>` (потоки evaluation наперёд, по умолчанию 4, `0` выключает только её).

## Локальная статистика использования

Сбор включён по умолчанию; `ROSLYN_MCP_USAGE=0` (или `false`) перед запуском отключает его. В `usage/usage-YYYYMMDD-N.jsonl` пишутся сведения о процессе и клиенте, пути workspace и параметры загрузки, имена и время инструментов, снимки памяти и активности. Аргументы/ответы инструментов, среда и командные строки не записываются. Ротация — 32 MiB, хранение — 60 дней. Файлы можно удалить; включённый сборщик создаёт новые. Автоматической передачи нет.

`pwsh ./scripts/Measure-McpUsage.ps1 -InputDirectory <каталог-usage> -Machine <метка> -OutputPath <префикс-отчёта>` создаёт JSON и Markdown; для нескольких машин передайте массивы каталогов и меток. В отчёте ключи заменены метками `K1`, `K2` в пределах машины. Передавайте агрегаты, сырые файлы остаются приватными. Численные пороги заранее не задаются: стратегия выбирается по результатам. Проверка анализатора — `pwsh ./scripts/Test-McpUsage.ps1`. [Схема](usage-statistics/schema.md), [приватность](../PRIVACY.md).

## Сохранённые правки и семантический снимок

После `load_workspace` семантические тулы доверяют файловому наблюдателю до следующего подтверждения. Подтверждение — это ещё один `load_workspace` или flush сохранения, о котором наблюдатель действительно сообщил. На каждом вызове дерево не хешируется, и снимок не помечается `freshness=unknown`.

- Сохранённый `.cs`, который уже есть в графе, читается один раз и применяется ко всем документам этого пути.
- Сохранённый additional file или analyzer config, который уже есть в графе и не является входом оценки проекта, применяется к опубликованному снимку. XAML, resx и Razor учитываются по роли, их текст не синхронизируется.
- Новый или удалённый файл документ не добавляет и не удаляет. Граф проекта помечается устаревшим; вызовите `load_workspace` снова. Файл проекта, solution и `Directory.Build.*` помечают граф так же.
- Переполнение наблюдателя, его ошибка или переименование каталога просят следующий семантический вызов перечитать известные документы и могут пометить граф устаревшим.
- Правка, о которой наблюдатель не сообщил — файл вне области наблюдения или потерянное уведомление без ошибки, — остаётся невидимой до следующего `load_workspace`. Постоянные дыры покрытия (импорты, входы restore, custom tasks) сами по себе чтение не блокируют.

Остальные политики свежести, которые рассматривались и не выбраны, записаны в [эпохе 3](https://github.com/VuDZ/RoslynMcpServer/blob/archive/research-workspace-state-cache/docs/backlog/workspace-load-cache/epoch-3-live-consistency/spec.md).

## Граница безопасности

Сервер читает и пишет файлы и запускает дочерние процессы `dotnet`/Git с правами хост-процесса. Это **не sandbox**; не все операции ограничены каталогом загруженного workspace.

- Используйте Git и проверяйте изменения агента.
- Не запускайте сервер от администратора/root.
- Не передавайте PAT, пароли и connection strings в prompts или аргументах tools.
- Параметры входящих MCP-вызовов по умолчанию логируются; проверьте раздел «Логи» для чувствительных репозиториев.

## Конфиденциальность

RoslynMcpServer не собирает телеметрию и не передаёт пользовательские данные автоматически.

Процесс локальный (stdio): своего бэкенда и HTTP-клиента нет. Логи и локальная статистика использования хранятся в пользовательских каталогах сервера (см. [Каталог пользователя](#каталог-пользователя)).

Сетевые обращения возникают только в операциях, которые запрашивает пользователь или MCP-клиент. Команды NuGet и restore в составе build/test/run обращаются к настроенным фидам; уходят идентификаторы пакетов и метаданные restore, не исходники и не промпты. Сборка выполняет MSBuild-цели ваших проектов, а тесты и приложения, запущенные инструментами, выполняют ваш код, который может сам ходить в сеть. Подробности: [PRIVACY.md](../PRIVACY.md#when-network-is-used) (на английском).
