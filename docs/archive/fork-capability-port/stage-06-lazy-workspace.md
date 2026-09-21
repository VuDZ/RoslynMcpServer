# Этап 6. Конфиг RoslynMcp.jsonc и ленивая загрузка

Статус: сделан (v1.4.9). От навигационных этапов не зависит.

Идущую загрузку не прерывать. `load_workspace` либо дожидается её и смотрит, что уже подцепилось, либо сразу сравнивает с уже опубликованным решением. Второй MSBuild запускается только если явно переданный параметр отличается.

## Цель

Необязательный `RoslynMcp.jsonc` в том виде, которым уже пользуются на форке. Нет файла — сервер как сегодня: решение появляется только после `load_workspace`.

Файл, README и текст для агентов описывают одно и то же: где лежит конфиг, какие ключи читаются, и что явный `load_workspace` перекрывает файл только теми аргументами, которые в вызове есть.

## Формат файла

Имя `RoslynMcp.jsonc`. Допустимы комментарии `//` и `/* */`. Обычный `AddJsonFile` это не обещает, нужен отдельный разбор и тест на комментарий.

Два места, как в форке. Сначала каталог exe (общие значения), затем текущий каталог процесса (перекрывает те же ключи). Относительный `workspace-path` считается от текущего каталога, не от каталога exe.

`ROSLYN_MCP_WORKSPACE` по-прежнему в начале процесса переключает текущий каталог на корень репозитория. Тогда файл проекта — это файл в cwd. Без этой переменной cwd часто домашний каталог, и «файл проекта» может прочитаться не оттуда. Это пишется и в README, и агентам.

Рабочий пример в репозитории — `RoslynMcp.jsonc.sample`, не боевой файл. Боевой файл с машинным путём в git не коммитить.

Ключи, которые уже стоят в чужих файлах. Неизвестный ключ не валит старт: предупреждение в лог и в `get_mcp_server_info`. Битый JSON не прячется: ленивой загрузки нет, `load_workspace` работает, ответ говорит, что файл не разобран.

| Ключ | Смысл |
|---|---|
| `workspace-path` | `.sln`, `.slnx` или `.csproj`. Нет ключа — ленивой загрузки нет |
| `configuration` | MSBuild Configuration |
| `platform` | MSBuild Platform |
| `target-framework` | Один внутренний TFM, если у проекта несколько |
| `max-results` | Запасной предел списков, когда аргумент инструмента опущен. Явный аргумент и `ROSLYN_MCP_MAX_RESULTS` сильнее файла |
| `preview` | Запасное значение `preview` у навигации, когда аргумент опущен |
| `ripgrep-path` | Путь к `rg`, только если поиск вызван с `useRipgrep: true` (этап 7). Сам по себе движок не переключает |

`build-args` и теневые анализаторы в файле форка нет, в этот файл их не добавлять. Теневые копии по-прежнему включаются только явным `shadowCopyInSolutionAnalyzers: true` у `load_workspace`. Опущенный флаг в сравнение загрузки не входит и уже включённые копии не гасит.

## Когда грузить из файла

Первый семантический вызов, решения ещё нет, в файле есть `workspace-path`: `SolutionManager.LoadAndPrepareAsync` с полями файла. Тот же вход, что у `load_workspace`. Сырой `workspace.CurrentSolution` не публиковать.

Пока эта загрузка держит замок, второй вызов ждёт. Отменять её нельзя: MSBuild смотрит токен между проектами, а чужой вызов этим токеном не владеет. После ожидания сработать правило сравнения ниже. Совпало — второй раз не открывать. Не совпало — открыть ещё раз, уже с учётом явных аргументов.

Ошибка загрузки возвращается из того инструмента, который её дождался, тем же текстом, что `load_workspace`. Снимок при этом не опубликован, следующий вызов может попробовать снова.

## Что сравнивает load_workspace

Сейчас `MsBuildWorkspaceProperties.IsSameLoadCache` считает опущенный аргумент неравным уже загруженному: платформа `x64` в сессии и `platform: null` в вызове — это промах кэша и новый MSBuild без платформы. Для конфига так нельзя.

Сравниваются только аргументы, которые в вызове переданы. Опущенный (`null` или пробелы) в сравнение не входит и уже подцепленное значение не стирает.

Путь у `load_workspace` обязателен, он сравнивается всегда.

| Уже загружено | Вызов | Результат |
|---|---|---|
| `A.sln`, `Sit-Debug`, `x64`, `net10.0` | `load_workspace(A.sln)` | Ничего из настроек не передано. Повторно не открывать |
| то же | `load_workspace(A.sln, configuration: Release)` | Платформу и TFM не передали, их не сравнивать и не сбрасывать. Configuration другая — открыть заново `Release` + прежние `x64` и `net10.0` |
| то же | `load_workspace(A.sln, platform: x64)` | Переданная платформа совпала. Повторно не открывать |
| то же | `load_workspace(B.sln)` | Путь другой. Открыть `B.sln` с прежними configuration, platform и TFM, потому что их не передавали |
| ещё ничего, в файле те же поля | `load_workspace(A.sln)` | Брать опущенное из файла и открыть один раз |

`buildArgs`, если его когда-нибудь передали раньше, тем же правилом: опущенный аргумент не затирает уже записанный хвост. В файл форка этот ключ не входит.

Пока загрузка из файла ещё идёт, `load_workspace` ждёт её конца и только потом применяет таблицу. Две оценки подряд бывают, если явный аргумент и правда другой. Прерывания первой нет.

## Текст для людей и для агентов

Оба текста обязательны в том же изменении, что и код. Краткий русский — в README сервера, раздел для агентов, не второй устав. Полный английский — в `AGENTS.md.sample`, секция полной инициализации. Те же факты — в описаниях `load_workspace`, `reset_workspace`, `get_mcp_server_info` и в `McpToolHelpCatalog`. В остальные инструменты одна фраза: решение может подняться из `RoslynMcp.jsonc`; другие настройки — только те аргументы `load_workspace`, которые переданы.

Английский абзац для sample:

> `RoslynMcp.jsonc` is optional. The server reads it from the executable directory, then from the process working directory; the working-directory file wins. `ROSLYN_MCP_WORKSPACE` sets that directory to the repo root at startup. Keys match the existing file: `workspace-path`, `configuration`, `platform`, `target-framework`, `max-results`, `preview`, `ripgrep-path`. Comments are allowed. No file means call `load_workspace` yourself.
>
> If `workspace-path` is set, the first semantic tool loads that solution and waits. It does not cancel a load already in progress. `get_mcp_server_info` shows which config files were read, the workspace path, and whether the solution is not loaded, loading, or loaded. The first load of a large solution can take minutes.
>
> `load_workspace` compares only arguments you passed. An omitted configuration, platform, or targetFramework is not a change and does not clear the value already loaded from the file or from an earlier call. Passing a different configuration reloads with that configuration and keeps the platform and TFM you did not mention. The same path and no explicit setting differences returns the workspace already loaded.
>
> `ripgrep-path` is used only when `search_code` is called with `useRipgrep: true`. It does not switch the default search. `max-results` and `preview` apply only when the tool argument is omitted.
>
> `shadowCopyInSolutionAnalyzers` is not a config key. Enable it by passing true to `load_workspace`. Turn it off with `reset_workspace` and a load without the flag.

`get_mcp_server_info` показывает оба прочитанных пути, итоговые ключи после слияния и откуда взялось текущее решение: из файла или из явного `load_workspace`.

## Файлы

- Разбор jsonc и слияние двух путей: новый тип. Не подключать форковский `WorkspaceConfig` целиком и не тащить его ленивый `CurrentSolution`.
- `Program.cs` — прочитать оба файла на старте.
- `Services/MsBuildWorkspaceProperties.cs` — сравнение «только переданные аргументы». Старый `IsSameLoadCache`, где `null` не равен загруженной строке, для этого вызова не использовать.
- `Services/SolutionManager.cs` — перед новым `LoadCoreAsync` собрать эффективные свойства: переданное перекрывает уже загруженное, опущенное остаётся.
- `Tools/WorkspaceTools.cs`, `Tools/ServerLifecycleTools.cs`, `Hosting/McpToolHelpCatalog.cs`
- Навигация и `search_code` читают `max-results` и `preview` только как запас, если аргумент опущен.
- `AGENTS.md.sample`, README, `RoslynMcp.jsonc.sample`
- Тесты: комментарий в jsonc; cwd перекрывает exe; опущенная платформа не вызывает повторное открытие и не стирает `x64`; переданная другая configuration открывает заново и сохраняет прежнюю платформу; нет файла — загрузки из конфига нет; вызов во время чужой загрузки ждёт и не отменяет её.

Тесты форка про подъём от файла к ближайшему `.csproj` не переносить.

## Инварианты

`LoadAndPrepareAsync`, опубликованный снимок, opt-in теневых анализаторов. Конфиг не пишет `.csproj` и не вызывает `TryApplyChanges`. Отмена чужого MSBuild не добавляется.

## Проверка

`run_specific_test` на разборе файла и на сравнении аргументов `load_workspace`.

Патч версии, строка README «Agent tools by version» и оба текста выше.
