# Task-06 — preflight среды и развёртывания

## Главное

- **In progress, 2026-10-29.** Linux/Claude Code выполнен; пара Codex/Windows ждёт данных владельца. Без неё окончательный O-01 не выбирается.
- **Объём сокращён владельцем 2026-10-29.** Прототипы механизмов — пункты 2, 3 (кроме MSBuild-контрпримера) и 5, сценарии 24–25 — перенесены в [task-02](../task-02-bridge-spike-hi.md): они реализуются один раз на настоящем Roslyn, а не дважды.
- **Пара Claude Code/Linux — go по входам допуска.** Три настоящих MCP одного worktree: euid, cwd, образ, конфиги и plugins совпадают. Среда различается только переменными сессии harness (`CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_MESSAGING_*`, `CLAUDE_PID` и др.). По O-05 они в ключ не входят, так что пара схлопывается. По прежнему полному env-хешу она бы разошлась. Negative control — другой checkout: отличаются и путь, и образ.
- **Цена общего контекста подтверждена контрпримером.** Один проект при тех же свойствах, но другой переменной среды даёт другой набор Compile-файлов. O-05 принимает эту цену явно: блок фактических параметров при reuse и `forceFresh`.
- **Storage на этой машине — go по фактам.** `/tmp` — tmpfs без `noexec`, годится для rendezvous. `~/.cache` — ext4 с правами 700, годится для StorageRoot. Путь socket — до 98 байт из 103. Проверку отказов (tmpfs/ramfs/noexec, ENOSPC без частичного образа) получает task-02.
- **Стоимость identity — go.** Для 231 файла / 105,9 МиБ: полный манифест — 0,68–0,76 с в холодном кеше и ~0,1 с в тёплом; копирование с хешем и fsync — 2,0–2,7 с; готовый манифест со stat-сверкой — 1–15 мс. Stat-штамп не замечает подменённых байт, хеш замечает.
- **Reuse snapshot launcher — no-go.** Identity snapshot — size/mtime двух файлов, неизменяемость держится на соглашении, манифеста нет. Host строит свой образ на StorageRoot.
- **Новая находка для L-01.** Lease launcher (`flock` на fd 9) наследуется всеми потомками .NET, в том числе через `setsid`. Общий host или осиротевший build-процесс незаметно удерживает snapshot launcher.
- Production-код не менялся; в spike добавлен только замерный режим `identity-bench`.

## База и scope

База `c67283e`, ветка `research/workspace-host-process`, сервер ветки 1.5.18. Живые MCP
исполняют сборку основного checkout 1.5.17 (snapshot launcher `9bc5bbf7706d`, build
output 2026-10-27). Ubuntu 26.04.1, Linux 7.0.0-34-generic, native; .NET SDK 10.0.113,
runtime 10.0.13; Claude Code 2.1.312. Исполнитель — `claude-opus-5-5` (Claude Code).

Решение владельца 2026-10-29 на вопрос о прототипах механизмов на стенде: «можно порезать».
В task-06 остаются пары, факты среды, замеры и аудит, то есть вход для O-01.
Что перенесено в task-02 — в разделе «Неизвестные и получатели».

## 1. Пары MCP

| Пара | Результат | Данные |
|---|---|---|
| Claude Code / Linux, один worktree | **go** по входам допуска O-05 | [claude-code-linux-pair.json](task-06-data/claude-code-linux-pair.json) |
| Другой checkout (negative control) | несовместимы: путь и образ | там же |
| Codex / Windows, один checkout | **unverified**, ждёт данных владельца | — |

Метод для Linux. Две headless-сессии запущены одновременно из корня worktree:

```sh
env -u CLAUDECODE claude -p "Call the roslyn MCP tool get_mcp_server_info once and print its full output verbatim, nothing else." \
  --model haiku --allowedTools mcp__roslyn__get_mcp_server_info --no-session-persistence < /dev/null
```

Пока сессии жили, их MCP снимались из `/proc`: cwd, образ, euid, lease-дескриптор и
среда. Значения среды хешировались только в памяти и не сохранялись. Третий участник —
MCP интерактивной сессии исполнителя в том же worktree. Snapshot launcher заранее
прогрет запуском `launch.sh < /dev/null` и оказался прежним (`9bc5bbf7706d`), поэтому
одновременные сессии не копировали 2 ГБ.

Итог: у всех трёх один cwd, один каталог образа, один euid. `get_mcp_server_info` обеих
headless-сессий: v1.5.17, profile full, plugins none, `RoslynMcp.jsonc` нет ни в exe, ни в cwd,
без merged load settings. Различаются только переменные harness: между двумя headless —
`CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_MESSAGING_SOCKET`, `CLAUDE_CODE_MESSAGING_TOKEN`;
с интерактивной сессией добавляются `CLAUDE_PID`, `CLAUDE_EFFORT`, `CLAUDE_CODE_EXECPATH`,
`CLAUDE_CODE_ENTRYPOINT`, `CLAUDE_CODE_SESSION_ATTENDED`, `GIT_EDITOR`, `OLDPWD`.

Предел. Headless-сессии запущены из shell исполнителя и наследуют его терминальные
переменные; интерактивные сессии из разных терминалов различались бы ещё и `TMUX`,
`SSH_*`, `XDG_SESSION_ID` ([ревью раунда 5](../../review-task-01-round-5-2026-10-29/linux-env-and-image-probe.json)).
По O-05 среда в ключ не входит, поэтому вывод от этого не меняется. Shared-режима
нет в коде: «go» значит, что входы ключа и допуска у пары совпадают, а не что реализация
их схлопнула. Ключ по путям и свойствам (path/Configuration/Platform/TFM) задан
построением O-05; его negative controls по каждому свойству проверяет task-02 на
реализации.

**Что нужно для Windows.** Два чата Codex в одном checkout; в каждом вызвать
`get_mcp_server_info` и передать ответы; третий чат в другом checkout — negative control.
Ответ сервера содержит каталог образа, cwd, пути конфигов, plugins и эффективные
настройки — этого хватает для сравнения без доступа к процессам. Поле «Binary modified»
— время изменения процесса `dotnet` при framework-dependent запуске, а не образа сервера.

## 3 (часть). Контрпример среды для MSBuild

Fixture: SDK-проект с `<Compile Include="Extra.cs" Condition="'$(PROBE_FEATURE)' == '1'" />`.
`PROBE_FEATURE=<v> dotnet msbuild Probe.csproj -getItem:Compile`: при 0 — `Common.cs`,
при 1 — `Common.cs`, `Extra.cs`. Путь и глобальные свойства одинаковы, graph разный.
Клиент, присоединившийся к host с другой средой создателя, увидит graph создателя.
Это принятая O-05 цена. Детектор чтения среды через логирование MSBuild здесь не
проверялся и универсальным не объявляется. [Данные](task-06-data/linux-preflight-facts.json).

## 4. Storage

| Корень | Путь | ФС и права | Вывод |
|---|---|---|---|
| Rendezvous | `/tmp/roslyn-mcp-1000/v1` | tmpfs, `rw,nosuid,nodev`, без `noexec`, `/tmp` 1777 | Годится для lock, socket и малых metadata. |
| Storage | `~/.cache/...` (`XDG_CACHE_HOME` не задан) | ext4, `~/.cache` 700, 17 ГБ свободно | Годится для образа и логов. |
| Кеш launcher | `~/.cache/roslyn-mcp` | ext4, 775 | Group-writable; защищён только родителем 700. |

Путь socket при euid 1000 и полном 64-символьном ключе: 93 байта (`pipe`),
95 (`socket`), 98 (`host.sock`) при лимите 103. Сами отказы на tmpfs/ramfs/noexec,
проверка места и ENOSPC без частичного образа требуют механизма и переданы task-02;
настоящий ENOSPC на малой ФС требует root или отдельного тома.

## 6. Стоимость identity

Режим [`identity-bench`](../../../../spikes/PipeRelay/Preflight/IdentityBench.cs) стенда PipeRelay,
n=3 в холодном и тёплом кеше; холодный — `posix_fadvise(DONTNEED)` по каждому файлу, без root.
File set — build output основного checkout `bin/Release/net10.0` без вложенных серверных
RID-каталогов и `logs`: **231 файл, 105,9 МиБ**. Runtime .NET 10.0.13, workstation GC
(production использует server GC; на потоковое хеширование это не влияет), 2 ядра,
ext4. Staging — на диске, в `~/tmp`.

```sh
dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll identity-bench \
  /home/vudz/projects/RoslynMcpServer/bin/Release/net10.0 ~/tmp/task06-bench <out.json> 3 \
  logs win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 osx-arm64
```

| Операция | Холодный кеш, мс | Тёплый кеш, мс | Где применяется |
|---|---|---|---|
| Stat-скан всех файлов | 0,6–0,9 | 0,4–0,5 | Подсказка инвалидации кеша identity |
| Полный SHA-256 манифест | 680–757 | 100–102 | Identity процесса или образа, один раз |
| Копирование с хешем и fsync каждого файла | 2516–2731 | 2010–2228 | Создание образа, один раз на сборку |
| Повторный хеш копии | 96–105 | 95–97 | Сверка staging перед публикацией |
| Готовый манифест + stat-сверка | 1,6–15,2 | 1,4–2,2 | Повторный attach без чтения bundle |

Повторный хеш копии читает только что записанные страницы из кеша и не доказывает
содержимое на диске; от частичной копии защищают fsync и атомарный rename.
Проба stat: в копии `BuildHost-net472/Microsoft.Bcl.AsyncInterfaces.dll` изменён один байт
и восстановлен mtime — `StatStampEqual=true`, `ContentHashEqual=false`.
[Сырые данные](task-06-data/identity-bench-linux.json).

Вывод: даже холодный полный хеш укладывается в startup budget design (30 с) с большим
запасом. Повторный attach с готовым манифестом весь bundle не читает. Самое дорогое —
создание образа, около 2–3 с, и оно происходит один раз на сборку. Startup timeout
конкретных harness здесь не измерялся.

## 7. Аудит launcher

Read-only: `~/.local/share/roslyn-mcp/launch.sh` вне репозитория не менялся.

| Свойство | Наблюдение | Вывод для reuse |
|---|---|---|
| Источник | Новейший `bin/{Release,Debug}/net10.0` или `*/publish` фиксированного основного checkout, независимо от cwd | Worktree-сессии исполняют сборку основного checkout. |
| Identity snapshot | sha1 от пути и size/mtime только `RoslynMcpServer.dll` и `.deps.json` | Не content identity: изменение других файлов при прежних двух штампах переиспользует старый snapshot. |
| Согласованность копии | После rsync перепроверяются те же два штампа | Изменения остальных файлов во время копирования не обнаруживаются. |
| Неизменяемость | По соглашению; каталоги writable, манифеста нет | Не audited immutable publication. |
| Use lease | `flock LOCK_SH` на `<snapshot>/.complete` через fd 9, переживает `exec` | Протокол есть, но несовместим с fcntl-registry design. |
| Наследование lease | Проверено: потомки .NET `Process.Start`, напрямую и через `setsid`, наследуют fd 9 | Host, BuildHost и CLI незаметно удерживают snapshot; осиротевший потомок держит его бессрочно. |
| Cleanup | `flock -n -x` по чужим `.complete`, затем `rm -rf`; `.tmp.*` старше 30 мин | Корректен для своих держателей. |
| Config origin | Каталог snapshot, копия build output | Совпадает с семантикой exe-config. |
| File set | Копирует вложенные серверные RID-выходы: 2,0 ГБ / 4868 файлов вместо 105,9 МиБ / 231 | L-01. |

**Reuse snapshot launcher — no-go.** Design-путь — собственный проверенный образ на
StorageRoot. Host, запущенный proxy под launcher, всё равно унаследует fd 9: snapshot
launcher проживёт дольше, но на образ host это не влияет. Новые пункты передаются владельцу
launcher в [L-01](../../README.md#l-01-launcher); скрипт не менялся.

## Выполнимость O-05 и условия O-01

На Linux O-05 выполним по входам: совместимые сессии одного checkout имеют одинаковые
входы ключа и допуска, а различия сессий harness в ключ не попадают. Цена — graph
создателя для присоединившихся клиентов с иной средой; её смягчают reuse block и `forceFresh`.
Стоимость identity и место хранения препятствий не создают; reuse launcher не нужен.
Условие окончательного O-01 — тот же результат для пары Codex/Windows, основного сценария
20+ MCP. Если там совместимые чаты разойдутся по образу, cwd или конфигу, это no-go
до выбора O-01, а не повод молча запускать N host.

## Найденные дефекты

| Что | Владелец | Где записано |
|---|---|---|
| Identity snapshot launcher по size/mtime двух файлов; неполная проверка согласованности копии | Владелец Linux launcher | [L-01](../../README.md#l-01-launcher) |
| Fd 9 lease наследуется всеми потомками .NET, включая `setsid` | Владелец Linux launcher | [L-01](../../README.md#l-01-launcher) |

Production-дефектов не найдено.

## Неизвестные и получатели

| Что | Получатель |
|---|---|
| Пара Codex/Windows и negative control другого checkout | task-06, данные владельца |
| Receipt created/reused, reuse block, cached/brief, launch race (пункт 2) | task-02 |
| Приватный host по `forceFresh`: scope, repeat/new fresh, failure до commit, recovery, EOF (пункт 3) | task-02 |
| Отказы storage tmpfs/ramfs/noexec, проверка места, ENOSPC без частичного образа (пункт 4) | task-02 |
| Prelaunch immutable publication/use lease до CreateProcess, race обновления source (пункт 5) | task-02 |
| Сценарии 24–25 design | task-02 |
| Negative controls ключа по каждому свойству на реализации | task-02 |
| Startup timeout harness, macOS | task-02 / V-02 |

## Validation

- Spike: `dotnet build spikes/PipeRelay/PipeRelay.csproj -c Release` — 0 предупреждений, 0 ошибок. Spike не входит в решение.
- Решение: `dotnet build RoslynMcpServer.sln -c Release` — 0 ошибок, 3 прежних предупреждения вне изменённых файлов.
- Main suite, Linux-фильтр `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk`, Release, `TMPDIR=~/tmp/testtmp`: **1428/1428 passed**, 0 failed, 0 skipped.
- SourceStructure и AnalyzerLifecycle не запускались: production C# и lifecycle paths не менялись.
- Markdown: 1051 локальная ссылка/anchor в документах темы, `docs/README.md` и README spike — битых 0, не считая прежних ссылок вида `file:line`; `git diff --check` — PASS.
- Scratch-каталоги в `~/tmp` удалены; `/tmp` не использовался. Независимого ревью результата пока нет.
