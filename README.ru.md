# RoslynMcpServer

**Агент читает C# как обычный текст. RoslynMcpServer даёт ему доступ к тому, что знает компилятор.**

RoslynMcpServer — локальный [MCP](https://modelcontextprotocol.io)-сервер для .NET-решений. Он загружает `.sln`, `.slnx` или `.csproj` через MSBuild и Roslyn, с теми же файлами проектов, SDK и ссылками, с которыми работает `dotnet build`. Claude Code, Codex, Cursor, OpenCode и другие MCP-клиенты получают то, что обычно знает только IDE: на что на самом деле ссылается имя, кто вызывает метод, кто реализует интерфейс, что происходит внутри NuGet-зависимости и почему проект не собирается. Запускать IDE для этого не нужно.

[![Release](https://img.shields.io/github/v/release/VuDZ/RoslynMcpServer)](https://github.com/VuDZ/RoslynMcpServer/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Platforms](https://img.shields.io/badge/platform-win--x64%20%7C%20linux--x64%20%7C%20osx--arm64-lightgrey)

[Быстрый старт](#быстрый-старт) · [Документация](#документация) · [English version](README.md)

## Почему агенту мало grep

В небольших проектах агент вполне справляется с поиском по файлам. Но по мере роста решения этот подход начинает давать сбои, причём в одних и тех же местах. Одно имя носят перегрузки метода, член интерфейса и его реализация, метод расширения и какой-нибудь посторонний приватный хелпер, а текстовый поиск их не различает. Значительная часть логики живёт в NuGet-пакетах, исходников которых в репозитории нет, и агент рассказывает о них по памяти, иногда о другой версии. Упавший `dotnet test` выдаёт тысячи строк, хотя суть укладывается в десяток. А переименование через поиск и замену затрагивает всё, что совпало по тексту, и это редко совпадает с тем, что видит компилятор.

RoslynMcpServer отвечает на такие вопросы по модели решения, которую строит компилятор. Все примеры ниже — реальные вызовы на [Jellyfin](https://github.com/jellyfin/jellyfin), открытом медиасервере из 41 проекта. Точные вызовы, коммит Jellyfin и полные ответы лежат в [docs/readme-examples](docs/readme-examples/README.md) (на английском).

## Как это выглядит

### «Переименуй этот метод, и только его»

`GetItemById` встречается в 278 строках C#-кода Jellyfin. Вы просите агента:

> Переименуй `ILibraryManager.GetItemById(Guid)` в `FindItemById`. Остальные перегрузки не трогай и покажи изменения до записи.

Сначала агент выясняет, что стоит за этим именем. `find_usages` возвращает список объявлений (фрагмент ответа):

```text
- `Emby.Server.Implementations.Library.LibraryManager.GetItemById` (method, 4 overloads, 246 location(s))
- `Jellyfin.LiveTv.Channels.ChannelManager.GetItemById` (method, 12 location(s))
- `MediaBrowser.Controller.Library.ILibraryManager.GetItemById` (method, 4 overloads, 246 location(s))
- `MediaBrowser.Controller.Library.LibraryManagerExtensions.GetItemById` (method, 10 location(s))
```

Интерфейс и `LibraryManager` — одно семейство методов с четырьмя перегрузками, поэтому у обоих по 246 мест. Метод расширения принимает строковый идентификатор и просто вызывает интерфейс. `ChannelManager.GetItemById<T>` — посторонний приватный хелпер, который сам вызывает `_libraryManager.GetItemById(id)`. Поиск с заменой переименовал бы заодно и его, и обобщённые перегрузки.

По умолчанию `rename_symbol` только показывает, что изменится: 152 места, связанных с перегрузкой с `Guid`, включая тесты. После применения изменились 50 файлов. Обобщённые перегрузки и хелпер сохранили свои имена, а решение собиралось без ошибок и до переименования, и после.

### «Что на самом деле делает эта библиотека?»

Jellyfin создаёт клиент TMDb так:

```csharp
// Not really interested in NotFoundException
_tmDbClient.ThrowApiExceptions = false;
```

> TMDb иногда ограничивает частоту запросов. Что при такой настройке произойдёт на 429, 401 и 404?

TMDbLib 3.0.0 подключена как NuGet-пакет, её исходников в репозитории нет. Через `explore_assembly` агент находит код запроса в той самой сборке, на которую ссылается Jellyfin, и читает его через `get_decompiled_method_body`. Вот фрагмент, который отвечает на вопрос (вывод ILSpy, с сокращениями):

```csharp
case HttpStatusCode.Unauthorized:
    throw new UnauthorizedAccessException("Call to TMDb returned unauthorized. ...");
case HttpStatusCode.NotFound:
    if (_client.ThrowApiExceptions)
        throw new NotFoundException(statusMessage);
    return null;
default:
    throw new GeneralHttpException(resp.StatusCode);
```

Настройка делает ровно то, что обещает комментарий, и ничего сверх этого. На 404 возвращается `null`, а 401 и ошибки сервера по-прежнему приводят к исключению. Для 429 есть отдельная ветка: цикл ждёт столько, сколько указано в `Retry-After` (или секунду, если заголовка нет), повторяет запрос, а когда попытки заканчиваются, бросает `RequestLimitExceededException`. По имени свойства об этом не догадаться, а модель, которая вспоминает библиотеку по памяти, может этого и не знать.

### Что ещё можно спросить

- *«Перечисли все фоновые задачи сервера».* `find_implementations` для `IScheduledTask` нашёл 20 классов в четырёх проектах.
- *«Кто вызывает эту перегрузку `DeleteItem` и что вызывает она сама?»* `get_call_graph` работает с конкретной перегрузкой, а у `LibraryManager` в Jellyfin их три.
- *«Исправь ошибки в этом файле».* `get_diagnostics_for_file` выдаёт ошибки и предупреждения компилятора по файлу без сборки. `get_code_fixes` и `apply_code_fix` применяют штатные исправления Roslyn, те же, что предлагает «лампочка» в IDE, вместо самодельного патча.
- *«Прогони тесты на то, что я сейчас поменял».* `run_dotnet_test` и `run_specific_test` возвращают упавшие тесты и ошибки компиляции, а не весь лог. `estimate_affected_tests` подсказывает, какие тестовые проекты и классы могут зависеть от сохранённых правок, и может сразу их запустить. Это именно подсказка: последнее слово по-прежнему за полным прогоном тестов и CI.
- *«Есть ли у нас уязвимые или устаревшие пакеты?»* Для этого есть `run_nuget_audit` и `list_outdated_packages`.

## Что получает агент

55 инструментов, сгруппированных по задачам:

| Задача | Что входит |
|---|---|
| **Понять решение** | Определения, использования, реализации и наследники, графы вызовов, скелеты классов без тел методов, граф проектов, списки тестов. |
| **Заглянуть в зависимости** | Обзор и декомпиляция любой подключённой сборки через ILSpy, причём той версии, которую использует ваш проект. |
| **Изменить код** | Семантическое переименование с предпросмотром, исправления Roslyn (code fixes), правки по синтаксическому дереву (using, методы, тела методов), переименование проекта, добавление и удаление NuGet-пакетов, `dotnet format`. |
| **Проверить** | Сборка, тесты (проект, класс, метод или фильтр VSTest), запуск, аудит NuGet. Результаты приходят уже разобранными, а полный лог остаётся на сервере, и его можно дочитать по курсору. |

Инструменты рассчитаны на ограниченный контекст агента. Длинные списки результатов обрезаются, продолжение можно получить по курсору. Профиль `lite` стартует с 16 основными инструментами, что удобно для небольших локальных моделей, а остальные группы агент включает по мере надобности. `get_tool_help` расскажет о любом инструменте. Через плагины можно добавить свои инструменты, работающие с загруженным решением ([руководство по плагинам](samples/RoslynMcpPlugin/README.md#russian-version)).

## Быстрый старт

**1. Скачайте** архив для своей платформы со страницы [Releases](https://github.com/VuDZ/RoslynMcpServer/releases) и распакуйте. Вариант `self-contained` не требует установленного .NET runtime для самого сервера, но SDK в него не входит. Чтобы загрузить решение, на машине должен быть .NET SDK, которым оно собирается (см. `global.json`), а пакеты решения должны быть восстановлены, например после `dotnet restore` или первой сборки.

В Linux (в macOS то же самое, только `osx-arm64` вместо `linux-x64`):

```bash
VERSION=1.6.0   # example; use the version you picked on the Releases page
ARCHIVE=RoslynMcpServer-$VERSION-linux-x64-self-contained.tar.gz
curl -fLO "https://github.com/VuDZ/RoslynMcpServer/releases/download/v$VERSION/$ARCHIVE"
tar -xzf "$ARCHIVE"   # creates RoslynMCP/RoslynMcpServer
```

В Windows, в PowerShell:

```powershell
$Version = "1.6.0"   # example; use the version you picked on the Releases page
$Archive = "RoslynMcpServer-$Version-win-x64-self-contained.zip"
Invoke-WebRequest "https://github.com/VuDZ/RoslynMcpServer/releases/download/v$Version/$Archive" -OutFile $Archive
Expand-Archive $Archive -DestinationPath C:\Tools   # creates C:\Tools\RoslynMCP\RoslynMcpServer.exe
```

**2. Зарегистрируйте** сервер в MCP-клиенте как локальный stdio-сервер. Большинство клиентов понимают такой формат (Cursor: `.cursor/mcp.json`; у Claude Desktop, Windsurf и других тот же блок `mcpServers`):

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "/absolute/path/to/RoslynMCP/RoslynMcpServer",
      "env": { "ROSLYN_MCP_WORKSPACE": "/absolute/path/to/your/repo" }
    }
  }
}
```

В Windows укажите в `command` путь к `.exe`. В JSON можно писать прямые слэши, тогда экранировать ничего не придётся:

```json
"command": "C:/Tools/RoslynMCP/RoslynMcpServer.exe",
"env": { "ROSLYN_MCP_WORKSPACE": "C:/src/YourApp" }
```

`ROSLYN_MCP_WORKSPACE` указывать необязательно, но желательно: он задаёт корень репозитория, и тогда `global.json` выберет нужный SDK.

<details>
<summary>Claude Code</summary>

Из корня вашего репозитория:

```bash
claude mcp add roslyn --scope project -e ROSLYN_MCP_WORKSPACE="$PWD" -- /absolute/path/to/RoslynMCP/RoslynMcpServer
```

</details>

<details>
<summary>Codex</summary>

В `~/.codex/config.toml`. Увеличьте таймаут инструментов: по умолчанию он равен минуте, а сборка и тесты часто идут дольше.

```toml
[mcp_servers.roslyn]
command = "/absolute/path/to/RoslynMCP/RoslynMcpServer"
env = { ROSLYN_MCP_WORKSPACE = "/absolute/path/to/your/repo" }
tool_timeout_sec = 600
```

</details>

<details>
<summary>VS Code</summary>

В `.vscode/mcp.json`:

```json
{
  "servers": {
    "roslyn": {
      "type": "stdio",
      "command": "/absolute/path/to/RoslynMCP/RoslynMcpServer",
      "env": { "ROSLYN_MCP_WORKSPACE": "${workspaceFolder}" }
    }
  }
}
```

</details>

<details>
<summary>OpenCode</summary>

В архиве есть `install2opencode.ps1`. Запустите его через PowerShell (в Linux и macOS это `pwsh`) из корня репозитория. Скрипт зарегистрирует сервер в `opencode.json` с таймаутом вызова 10 минут (с меньшим OpenCode обрывает сборки и тесты) и добавит в `AGENTS.md` правила для агента из шага 3.

```powershell
& /absolute/path/to/RoslynMCP/install2opencode.ps1
```

</details>

**3. Объясните агенту, что сервером нужно пользоваться.** Этот шаг легко пропустить, хотя он важнее, чем кажется. Подключённый сервер лишь делает инструменты *доступными*, а по привычке агент всё равно возьмётся за grep и `dotnet build` в терминале. В архиве лежит [`AGENTS.md.sample`](samples/AGENTS.md.sample) — правила, которые подсказывают агенту, какой инструмент когда использовать: сначала загрузить workspace, идентификаторы C# искать через Roslyn, собирать и тестировать через MCP-инструменты. Скопируйте файл в репозиторий как `AGENTS.md` или добавьте его содержимое в существующий. Codex, Cursor и OpenCode читают `AGENTS.md` сами, а для Claude Code добавьте в `CLAUDE.md` строку `@AGENTS.md`.

**4. Попробуйте.** Перезапустите клиент и попросите:

> Используй Roslyn MCP: вызови `get_mcp_server_info`, загрузи решение из этого репозитория и перечисли его проекты. Потом найди все реализации `<знакомый вам интерфейс>`.

В ответе будут версия сервера, найденный SDK, состояние restore и список загруженных проектов. Если загрузить решение не удалось, в ошибке будет сказано почему: например, нет нужного SDK или multi-target проекту нужен `targetFramework`.

Подробнее — в разделе [Установка и настройка](docs/setup.ru.md): все платформы, сборка из исходников, профили инструментов, `RoslynMcp.jsonc`, переменные окружения, логи. В [настройках загрузки и сборки](docs/setup.ru.md#загрузка-и-сборка-проекта-напрямую) можно потребовать путь к решению и задать текст подсказки для прямого вызова проекта.

## Что важно знать

- **Только C#.** Семантические инструменты работают с C#-проектами, которые может загрузить MSBuild. Остальные файлы доступны как текст: их можно читать и искать по ним.
- **Правки подхватываются.** Сохранённые изменения в `.cs`-файлах, которые уже входят в решение, попадают в семантическую модель автоматически. После добавления или удаления файлов, а также после изменения файлов проектов агент снова вызывает `load_workspace`. Несохранённые изменения в редакторе сервер не видит.
- **Сервер работает с вашими правами.** Он читает и записывает файлы, запускает `dotnet` и Git от имени вашего пользователя. Это не песочница: работайте под контролем версий и просматривайте изменения агента.
- **Всё остаётся на вашей машине.** Нет удалённой телеметрии и облачного бэкенда: сам сервер ничего никуда не отправляет. В сеть могут обращаться только операции, которые запрашиваете вы или агент: команды NuGet, а также сборки, тесты и приложения, запущенные инструментами. Логи, включая входящие аргументы инструментов, хранятся в вашем профиле, как и локальная статистика использования; она включена по умолчанию и отключается переменной `ROSLYN_MCP_USAGE=0`. См. [PRIVACY.md](PRIVACY.md) и [логи](docs/setup.ru.md#логи).
- **Платформы.** Windows x64 и Linux x64. Архивы для macOS ARM64 проходят нативный CI, но сам упакованный архив на Mac пока не проверялся.

## Документация

- [Установка и настройка](docs/setup.ru.md): платформы, клиенты, профили инструментов, конфигурация, логи, безопасность
- [Справочник инструментов](docs/tools.ru.md): все инструменты и их параметры
- [`AGENTS.md.sample`](samples/AGENTS.md.sample): правила для агента, которые нужно скопировать в ваш репозиторий
- [Архитектура](docs/ARCHITECTURE.md) (на английском): как устроены workspace, отслеживание файлов, инструменты сборки и тестов
- [CHANGELOG](CHANGELOG.md) (на английском): изменения, заметные агенту, по версиям
- [Руководство по плагинам](samples/RoslynMcpPlugin/README.md#russian-version): свои инструменты

## Сборка из исходников

Нужен .NET 10 SDK. Запускайте опубликованный исполняемый файл, а не `dotnet run`:

```bash
dotnet publish src/RoslynMcpServer/RoslynMcpServer.csproj -c Release -r linux-x64   # или win-x64, osx-arm64
# → bin/Release/net11.0/linux-x64/publish/RoslynMcpServer
```

## Лицензия

[MIT](LICENSE)
