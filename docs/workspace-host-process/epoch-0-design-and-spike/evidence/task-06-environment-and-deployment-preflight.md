# Task-06 — preflight среды и развёртывания

## Главное

- **Completed, 2026-10-29:** принят сокращённый владельцем preflight scope Linux/Windows. Независимое ревью: 2 раунда, F01–F05 подтверждены, новых must-fix нет. O-01 остаётся выбором владельца; shared activation не разрешена.
- **Объём сокращён владельцем 2026-10-29.** Прототипы механизмов — пункты 2, 3 (кроме MSBuild-контрпримера) и 5, сценарии 24–25 — перенесены в [task-02](../task-02-bridge-spike-hi.md): они реализуются один раз на настоящем Roslyn, а не дважды.
- **Реальные пары Linux/Claude Code и Windows/Codex — go по наблюдаемым входам.** Три Linux MCP одного worktree и два Windows MCP root/subagent одного checkout совпали по owner/cwd/exe/config/plugins. Linux session env различается и по O-05 не дробит ключ; Windows env не сравнивалась. Это совместимость candidate inputs, не работающий admission/схлопывание. Negative control — другой checkout; на Windows это явно CLI MCP.
- **Цена общего контекста подтверждена контрпримером.** Один проект при тех же свойствах, но другой переменной среды даёт другой набор Compile-файлов. O-05 принимает эту цену явно: блок фактических параметров при reuse и `forceFresh`.
- **Storage — go по фактам родительских ФС.** Linux `/tmp` — tmpfs без `noexec`, `~/.cache` — ext4 с mode 700. Windows LocalAppData — NTFS C:, около 734,6 GiB свободно. Целевые Windows roots отсутствуют: owner-only DACL/admission/отказы получает task-02.
- **Стоимость фаз identity измерена.** Linux, 231 файл / 105,9 MiB: hash 680–757 мс с advisory eviction без доказанного cold cache и 100–102 мс warm; copy/hash/flush 2,0–2,7 с. Windows, 231 файл / 106,0 MiB на томе будущего StorageRoot: warm hash 83–86 мс, copy/hash/flush 248–268 мс, n=3. End-to-end guarded startup, initialize timeout harness и atomic publication не проверены.
- **Reuse snapshot launcher — no-go.** Identity snapshot — size/mtime двух файлов, неизменяемость по соглашению, манифеста нет. Собственный host image не доказывает provenance уже исполняемого proxy: prelaunch guard на обеих ОС остаётся **unverified**, task-02 обязан закрыть его до shared admission/activation.
- **Новая находка для L-01.** Lease launcher (`flock` на fd 9) наследуется всеми потомками .NET, в том числе через `setsid`. Общий host или осиротевший build-процесс незаметно удерживает snapshot launcher.
- **Исправлен опасный дефект benchmark F01:** раньше рекурсивно удалялся произвольный scratch. Теперь удаляется только свой UUID-подкаталог; source/scratch overlap и перезапись output запрещены. Пять checks: red 5 failures → green 5 passed. F02–F05 исправляют выводы/cache labels/учёт; [ревью](../../review-task-06-2026-10-29/result.md).
- Production C# не менялся. Windows final Release build успешен, 3 прежних warnings; main suite **1432/1432 passed**. Дополнительно исправлен docs-дефект task-01 R10-F01: readOnly metadata не доказывает отсутствие эффектов load/prepare/MSBuild.

## База и scope

База `c67283e`, ветка `research/workspace-host-process`, сервер ветки 1.5.18. Живые MCP
исполняют сборку основного checkout 1.5.17 (snapshot launcher `9bc5bbf7706d`, build
output 2026-10-27). Ubuntu 26.04.1, Linux 7.0.0-34-generic, native; .NET SDK 10.0.113,
runtime 10.0.13; Claude Code 2.1.312. Исполнитель — `claude-opus-5-5` (Claude Code).

Windows-дополнение и исправления: база `7882c161ada2fa11df93287047ab90e6a6693b23` + diff,
2026-10-29, GPT Sol-6.1 (`gpt-6.1-sol`). Windows build 26200, runtime 10.0.13.
Живые MCP Codex — **1.5.16.0**, publish основного checkout от 2026-10-24;
benchmark — FDD build ветки **1.5.18**. Они не выдаются за одну сборку.
Нулевой вариант — нынешние отдельные MCP/workspace — остаётся действующим;
task-06 не сравнивает с ним RAM/latency настоящего shared Roslyn.

Решение владельца 2026-10-29 на вопрос о прототипах механизмов на стенде: «можно порезать».
В task-06 остаются пары, факты среды, замеры и аудит, то есть вход для O-01.
Что перенесено в task-02 — в разделе «Неизвестные и получатели».

## 1. Пары MCP

| Пара | Результат | Данные |
|---|---|---|
| Claude Code / Linux, один worktree | **go** по входам допуска O-05 | [claude-code-linux-pair.json](task-06-data/claude-code-linux-pair.json) |
| Другой checkout (negative control) | несовместимы: путь и образ | там же |
| Codex / Windows, root + subagent, один checkout | **go по наблюдаемым входам**, общий parent app-server | [root](task-06-data/codex-root-info.json), [subagent](task-06-data/codex-agent-info.json), [CIM/SID](task-06-data/windows-preflight-facts.json) |
| Windows, другой checkout, контролируемый CLI MCP | negative по candidate path, это не третий чат Codex | [raw](task-06-data/windows-negative-control.json) |

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

**Метод Windows.** Root и отдельный subagent Codex вызвали настоящий
`get_mcp_server_info` без load. PID 45500/54104, parent 59364, owner SID одинаков
(CIM GetOwnerSid, status 0). Это два MCP-контекста одного Codex app-server,
не независимые UI-окна. Оба: exe/base из `E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish`,
cwd `E:\Devel\RoslynMcpServer-workspace-host-process`, profile full, 54 tools,
plugins none, exe/cwd конфиги отсутствуют, merged load properties отсутствуют.
Candidate path задан экспериментом — `RoslynMcpServer.sln` в этом checkout;
Configuration/Platform/TFM не заданы, значения Debug/AnyCPU/TFM не выдумываются.
Workspace не загружался; фактическое key/admission и схлопывание проверяет task-02.
Среда процессов Windows не сравнивалась; environment parity не заявлена.

Negative control — тот же опубликованный exe, CLI stdio MCP с cwd и candidate
solution `E:\Devel\RoslynMcpServer`. Он отделяется по candidate path даже при том же exe.
CLI spawn/initialize/info 210,841 мс — baseline, не Codex budget/guarded startup.
После закрытия stdin exit 0. Subagent остановил только собственный MCP;
CIM подтвердил отсутствие PID 54104/57824 и сохранение прежних 45500/42596.

Совпавшие exe/base/version — наблюдаемые входы, не full image identity proof.
Поле «Binary modified» при FDD относится к процессу `dotnet`, а не серверной DLL;
источник — [McpServerInfoHelper.cs](../../../../Services/McpServerInfoHelper.cs).

## 3 (часть). Контрпример среды для MSBuild

Fixture: SDK-проект с `<Compile Include="Extra.cs" Condition="'$(PROBE_FEATURE)' == '1'" />`.
`PROBE_FEATURE=<v> dotnet msbuild Probe.csproj -getItem:Compile`: при 0 — `Common.cs`,
при 1 — `Common.cs`, `Extra.cs`. Путь и глобальные свойства одинаковы, graph разный.
Клиент, присоединившийся к host с другой средой создателя, увидит graph создателя.
Это принятая O-05 цена. Детектор чтения среды через логирование MSBuild здесь не
проверялся и универсальным не объявляется. [Данные](task-06-data/linux-preflight-facts.json).

Windows повтор: два свежих MSBuild процесса с одинаковыми
`-p:Configuration=Release -p:Platform=AnyCPU -p:TargetFramework=net10.0`,
различается только публичная fixture-переменная `PROBE_FEATURE`.
При 0 — Common.cs, при 1 — Common.cs/Extra.cs, оба exit 0.
[Полный XML, argv и items](task-06-data/msbuild-environment-windows.json).
Это контрпример graph parity, не реализация shared/fresh механизма.

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

Windows: [факты ФС/ACL](task-06-data/windows-preflight-facts.json).
LocalAppData на NTFS Fixed C:, available 788758437888 bytes, owner текущий пользователь,
родитель не reparse point. Inherited ACL включает sandbox/capability SID и не является
owner-only DACL. Candidate `RoslynMcpServer/WorkspaceHosts/v1/{runtime,storage}`
отсутствуют: они не создавались и не принимались. Task-02 проверяет actual roots,
owner-only права, peer/reparse admission и отказы. Linux mode родителя также не
доказывает права ещё не принятого root. Free-space check не резервирует место.

## 6. Стоимость identity

Режим [`identity-bench`](../../../../spikes/PipeRelay/Preflight/IdentityBench.cs) стенда PipeRelay,
n=3 в двух Linux сериях. Старая raw-метка `cold` означает попытку
`posix_fadvise(DONTNEED)` без проверки return code/residency, **не доказанный cold cache**.
Raw сохранён без переименования. Новый код пишет `cold-advised`, проверяет return;
успешный advice тоже не доказывает residency. Новая Linux-ветка здесь не перезапускалась.
File set — build output основного checkout `bin/Release/net10.0` без вложенных серверных
RID-каталогов и `logs`: **231 файл, 105,9 МиБ**. Runtime .NET 10.0.13, workstation GC
(влияние отличия от production server GC не измерено), 2 ядра,
ext4. Staging — на диске, в `~/tmp`.

```sh
dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll identity-bench \
  /home/vudz/projects/RoslynMcpServer/bin/Release/net10.0 ~/tmp/task06-bench <out.json> 3 \
  logs win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 osx-arm64
```

| Операция | Advice, residency unverified, мс | Тёплый кеш, мс | Возможное применение фазы |
|---|---|---|---|
| Stat-скан всех файлов | 0,6–0,9 | 0,4–0,5 | Подсказка инвалидации кеша identity |
| Полный SHA-256 манифест | 680–757 | 100–102 | Хеш выбранного набора, не process provenance |
| Копирование с хешем и fsync каждого файла | 2516–2731 | 2010–2228 | Создание образа, один раз на сборку |
| Повторный хеш копии | 96–105 | 95–97 | Сверка staging перед публикацией |
| Готовый манифест + stat-сверка | 1,6–15,2 | 1,4–2,2 | Stat invalidation без чтения bundle; не admission proof |

Повторный хеш копии читает только что записанные страницы из кеша и не доказывает
содержимое на диске. Flush включён в стоимость, но atomic rename/publication,
crash durability и отсутствие partial launch при ENOSPC не выполнялись — task-02.
Проба stat: в копии `BuildHost-net472/Microsoft.Bcl.AsyncInterfaces.dll` изменён один байт
и восстановлен mtime — `StatStampEqual=true`, `ContentHashEqual=false`.
[Сырые данные](task-06-data/identity-bench-linux.json).

Windows: runtime 10.0.13, workstation GC, 16 processors, **warm only**, n=3;
cold unavailable. Основная серия после final build/test — 231 файл / 111181807 bytes,
staging в свежем изолированном LocalAppData probe на NTFS C:, томе будущего StorageRoot.
Production root не создавался, пустой probe parent удалён.
[Метод/ACL](task-06-data/identity-bench-windows-storage-method.json),
[raw](task-06-data/identity-bench-windows-storage.json).

| Фаза | Windows warm C:, мс, n=3 |
|---|---|
| Stat scan | 0,65–0,93 |
| Full SHA-256 manifest | 82,84–86,41 |
| Copy + hash + file flush | 248,35–268,34 |
| Hash staging | 103,67–106,57 |
| Manifest read + stat check | 1,35–5,48 |

Дополнительный ранний [контроль E:](task-06-data/identity-bench-windows-build.json):
231 файл / 111181395 bytes, hash 91–98 мс, copy 428–449 мс. Build output между
сериями изменился; эти серии и Linux не объединяются и не считаются равными file set.
Windows stale-stat также дал true/false. Benchmark не проверяет cached process identity,
audited immutable-manifest admission или guarded startup. Измеренные фазы меньше
design deadline 30 с, но end-to-end initialize/harness budget этим не доказан.

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
StorageRoot пока не доказывает provenance уже исполняемого proxy. На Linux snapshot
writable, Windows controlled-stop publish также не доказывает prelaunch lease.
Оба пути **unverified**: task-02 обязан закрыть границу до первого shared admission,
иначе `deployment-unverified` и restart через verified image. Поздний disk hash не
описывает старый исполняемый код. Host под launcher унаследует fd 9: snapshot
launcher проживёт дольше, но на образ host это не влияет. Новые пункты передаются владельцу
launcher в [L-01](../../README.md#l-01-launcher); скрипт не менялся.

## Выполнимость O-05 и условия O-01

На Linux и Windows O-05 выполним по наблюдаемым входам: сессии одного checkout имеют одинаковые
входы ключа и допуска, а различия сессий harness в ключ не попадают. Цена — graph
создателя для присоединившихся клиентов с иной средой; её смягчают reuse block и `forceFresh`.
Измеренные phase costs и родительские ФС не выявили препятствий; reuse launcher
отклонён. Это вход для выбора O-01 владельцем, а не достаточное доказательство
безопасного shared режима. Пара Windows закрыла один пробел; prelaunch provenance
proxy/host остаётся gate task-02 **до shared admission/activation**. Перенос по решению
владельца не является pass. При неуспехе — no-go, без позднего hash/fallback в N host.

## Найденные дефекты

| Что | Владелец | Где записано |
|---|---|---|
| Identity snapshot launcher по size/mtime двух файлов; неполная проверка согласованности копии | Владелец Linux launcher | [L-01](../../README.md#l-01-launcher) |
| Fd 9 lease наследуется всеми потомками .NET, включая `setsid` | Владелец Linux launcher | [L-01](../../README.md#l-01-launcher) |

Task-06 F01 — дефект самого benchmark: исправлен с пятью checks,
[red](task-06-data/identity-checks-red.json) / [green](task-06-data/identity-checks-green.json).
F02–F05 исправляют provenance/cache/publication/status claims. F01–F05 подтверждены независимым раундом 2;
[ответ и validation](../../evidence/2026-10-29-task-06-windows-review-fixes.md).
Task-01 R10-F01 (readOnly metadata не доказывает отсутствие load effects) исправлен
в design, регрессия передана task-02. Production C# не менялся.

## Неизвестные и получатели

| Что | Получатель |
|---|---|
| Receipt created/reused, reuse block, cached/brief, launch race (пункт 2) | task-02 |
| Приватный host по `forceFresh`: scope, repeat/new fresh, failure до commit, recovery, EOF (пункт 3) | task-02 |
| Отказы storage tmpfs/ramfs/noexec, проверка места, ENOSPC без частичного образа (пункт 4) | task-02 |
| Prelaunch provenance proxy/host до CreateProcess, race source (пункт 5) | task-02, unverified, обязательный gate до shared admission/activation |
| Сценарии 24–25 design | task-02 |
| Negative controls ключа по каждому свойству на реализации | task-02 |
| Guarded startup/initialize timeout harness, cached process identity, actual root admission, macOS | task-02 / V-02/V-03 |
| Подтверждённый cold cache; повтор новой advice-ветки Linux | Непроверено; task-02 при сравнении startup |

## Validation

- Spike: `dotnet build spikes/PipeRelay/PipeRelay.csproj -c Release` — 0 предупреждений, 0 ошибок. Spike не входит в решение.
- Решение: `dotnet build RoslynMcpServer.sln -c Release` — 0 ошибок, 3 прежних предупреждения вне изменённых файлов.
- Main suite, Linux-фильтр `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk`, Release, `TMPDIR=~/tmp/testtmp`: **1428/1428 passed**, 0 failed, 0 skipped.
- SourceStructure и AnalyzerLifecycle не запускались: production C# и lifecycle paths не менялись.
- Markdown: 1051 локальная ссылка/anchor в документах темы, `docs/README.md` и README spike — битых 0, не считая прежних ссылок вида `file:line`; `git diff --check` — PASS.
- Предыдущие пять пунктов — историческая Linux validation исходного результата. Scratch в `~/tmp` удалены; `/tmp` не использовался.
- Windows final solution build через MCP, Release — success, 0 errors, 3 прежних warnings CS8603/CS8601/xUnit1031; [raw](task-06-data/final-build-mcp.json). Предварительный build также имел MSB3061 из-за DLL, занятых старым MCP; финальный build их не выдал.
- Windows final main filter `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, Release, noBuild/noRestore: **1432/1432 passed**, 0 failed; [raw](task-06-data/final-main-mcp.json). Это отдельный запуск, не сумма с Linux.
- Spike rebuilt Release 0 warnings/errors; `identity-checks`: red exit 1, 5 failures; green exit 0, 5 passed. Production/lifecycle paths не менялись, SourceStructure/AnalyzerLifecycle отдельно не требовались; обычные SourceStructure входят в main suite.
- Финальные Markdown/diff checks и независимое ревью — [validation исправлений](../../evidence/2026-10-29-task-06-windows-review-fixes.md).
