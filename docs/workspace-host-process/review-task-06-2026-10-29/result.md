# Task-06 — независимое research-task-review, раунд 2

## Главное

**Принято в сокращённом исследовательском scope task-06. F01–F05 закрыты;
новых must-fix нет.** Подтверждены 5 исправлений, итог task-06 — **2 раунда / 5 fixes**.
Windows additions закрывают недостававшие наблюдения пары, FS, environment fixture и
стоимости фаз. Это не проверка работающего shared host, не выбор O-01 и не production
activation. Финальные task/index/status/counters обновляет координатор.

На обеих ОС prelaunch provenance остаётся **unverified**; shared admission запрещён
до успешного gate task-02. Частные host/receipt/recovery, actual roots/ACL и failure
механизмы, initialize budget и cached process identity переданы task-02 по решению
владельца; этот review не возвращает их в сокращённый preflight.

Самый важный code fix: benchmark удаляет только свой UUID-child, сохраняет scratch
parent и существующий output. Windows — warm-only; исходный Linux raw сохранён,
а его `cold` ограничен advisory, без доказанной residency. Новая Linux advice-ветка
не запускалась. Влияние отличия GC и реальный guarded startup не объявлены измеренными.

Зависимый docs-риск task-01 **R10-F01** закрыт отдельно в round 11:
[результат](../review-task-01-2026-10-29/result.md). Его не прибавляем к пяти fixes task-06.
Выбор владельца global retire сохранён; чистая read-stage и effectful load/prepare
имеют разные reset outcomes. Reviewer runtime/MCP/builds не запускал.

## Срез

Дата **2026-10-29**, reviewer — та же отдельная сессия Codex
`/root/task01_research_review`, точный model ID **unavailable**.
HEAD `7882c161ada2fa11df93287047ab90e6a6693b23` + текущий working diff.
Исполнители: исходный Linux — `claude-opus-5-5`; Windows/fixes — `gpt-6.1-sol`
по уточнению владельца. Production 1.5.18 не менялся. Живые Codex MCP 1.5.16,
Linux MCP 1.5.17 и benchmark output 1.5.18 разделены в evidence.

SHA-256 проверенного среза до финального bookkeeping координатора:

| Файл | SHA-256 |
|---|---|
| [Task-06](../epoch-0-design-and-spike/task-06-environment-and-deployment-preflight-med.md) | `449B080C97C7225C2C22EAB22034366B899179C00DA8F767403055E52B814CA3` |
| [Evidence](../epoch-0-design-and-spike/evidence/task-06-environment-and-deployment-preflight.md) | `B827EBF2BA21C705610271A1628EA5BEF111E509AE3F74175D8F0EB24E41575D` |
| [Author response/validation](../evidence/2026-10-29-task-06-windows-review-fixes.md) | `8BCB1575B0CB7014FAD9A8F88A2DA97B2695062553FBB3AB5F3B17D3E9137A94` |
| [IdentityBench.cs](../../../spikes/PipeRelay/Preflight/IdentityBench.cs) | `DBBA0C11376409E43CA69EEFED0D092C5EACBACCD5EBC228370A6E50EDFBA035` |
| [IdentityBenchChecks.cs](../../../spikes/PipeRelay/Preflight/IdentityBenchChecks.cs) | `BA2F9DA069F98A08129F72B0E595DA00B81E30BE4809BB122550043C2B39BFCF` |

15 raw JSON: 3 исходных Linux не изменились (git blob IDs совпали с round 1),
12 Windows добавлены. Digest списка `Name SHA256\n`, отсортированного по имени,
UTF-8, uppercase hashes:
`6D3F40A0C2FFC041CAA49808FC1BD051E480EC6AAF3F87D51311581387B9F4A6`.
Pending review/counters во входном draft не приняты за финальный статус.

## Перепроверка findings

| ID | Исходная severity | Проверенное исправление | Итог |
|---|---|---|---|
| F01 | major | IdentityBench:24–42 валидирует args/overlap/output, создаёт `identity-run-UUID`; :66–79 использует CreateNew и удаляет только child. Existing parent/output не удаляются; source/scratch containment отвергается. Checks проверяют пять исходных failure scenarios, включая output внутри scratch parent и сохранность sentinel. [Red](../epoch-0-design-and-spike/evidence/task-06-data/identity-checks-red.json):5 failures; [green](../epoch-0-design-and-spike/evidence/task-06-data/identity-checks-green.json):5 passed. | **Закрыт.** Прежнее удаление произвольного scratch устранено. |
| F02 | major | Evidence §§7/O-01, design §§2/9/12 и task-02 различают свой host copy и proxy provenance. Обе ОС unverified; proof до CreateProcess обязателен до shared admission/activation. Windows pair не является достаточным условием safe deployment. Publisher manifest receiver — task-02/E1. | **Закрыт.** Scope transfer не превратился в pass или обход admission. |
| F03 | major | IdentityBench:48 выпускает warm-only вне Linux, :62–64 точно описывает метод. Linux :275–278 проверяет return и не обещает cold residency. Старый Linux JSON не переименован; evidence ограничивает исходную методику. Все 6 новых Windows samples имеют label warm, ColdMethod unavailable. | **Закрыт.** Новая Linux-ветка остаётся not run. |
| F04 | minor | Evidence/README различают file flush cost и неизмеренные atomic publication/crash/ENOSPC. Код :195–196 не объявляет себя publisher. Guarded initialize/полный startup не выводятся из phase costs. | **Закрыт.** |
| F05 | minor | Текущие task/topic/epoch/design summaries согласованы: замеры обеих ОС завершены, review pending; старые task-01 slices явно исторические. При перепроверке найден остаток design header planned и устранён координатором. Финальное reduced-scope completed/counters назначены координатору после verdict. | **Закрыт по содержанию.** Финальное bookkeeping — интеграционный шаг координатора. |

Новых findings нет. Source/path guard — защита от ошибочных аргументов обычного
локального benchmark; этот review не объявляет его secure publisher или защитой
от враждебной конкурентной подмены каталогов.

## Windows additions и числа

- **Реальная пара:** [root](../epoch-0-design-and-spike/evidence/task-06-data/codex-root-info.json)
  и [subagent](../epoch-0-design-and-spike/evidence/task-06-data/codex-agent-info.json)
  — два MCP PID одного app-server, одинаковые SID, cwd, exe/base/version, full/54,
  plugins/config отсутствуют, workspace не загружен. Configuration/Platform/TFM
  остаются null; значения defaults не придуманы. Это observation входов candidate,
  а не проверка actual key, bytes identity или реализации collapse.
- [Negative control](../epoch-0-design-and-spike/evidence/task-06-data/windows-negative-control.json)
  — явно CLI MCP с другим candidate path/cwd, тем же exe; не третий чат Codex.
  210,841 мс spawn/initialize/info — CLI baseline, не будущий startup budget.
- [FS/ACL facts](../epoch-0-design-and-spike/evidence/task-06-data/windows-preflight-facts.json)
  — NTFS C:, 788758437888 bytes = **734,588539 GiB** available. Родитель не reparse;
  actual roots отсутствуют, inherited ACL не выдаётся за owner-only. Root admission
  и failure tests остаются task-02, не подменены правами родителя.
- [MSBuild fixture](../epoch-0-design-and-spike/evidence/task-06-data/msbuild-environment-windows.json)
  содержит XML/argv и два exit0 списка Compile при одном path/properties и разном
  публичном PROBE_FEATURE. Цена creator context подтверждена контрпримером;
  shared/private механизм этим не измерен.
- Основная [C: серия](../epoch-0-design-and-spike/evidence/task-06-data/identity-bench-windows-storage.json):
  231 файл, **111181807 bytes = 106,031234 MiB**, n=3 warm. Независимый пересчёт:
  stat 0,6549–0,9302; hash 82,8438–86,4051; copy/hash/flush 248,3485–268,3384;
  verify 103,6711–106,5657; manifest/stat 1,3460–5,4836 мс. Текущий source build file
  set независимо пересчитан по exclusions: **231 / 111181807**, совпал с этой серией.
- Ранняя [E: серия](../epoch-0-design-and-spike/evidence/task-06-data/identity-bench-windows-build.json)
  — 111181395 bytes / 106,030841 MiB, n=3 warm; hash 91,2434–97,9021 и copy
  428,2122–449,4411 мс. Серии не объединены между собой или с Linux.
- Linux исходные raw и числа сохранены. Advice не считается verified cold;
  обе Windows stale-stat probes — true/false. Отсутствие harness startup measurement
  и невалидированный manifest admission прямо вынесены в неизвестные.

## Claims/source и validation

Повторно проверены 5 consequential claims: owned cleanup/CreateNew; warm-only/
checked advisory branch; content hash/copy/flush без publication; info path/mtime
не full image proof; текущий load API не имеет forceFresh и production compile
исключает spikes. Последние две исходные claims unchanged относительно round 1.
Дополнительно прослежено устранение R10-F01 и общий receiver task-02.

Координаторские [final build raw](../epoch-0-design-and-spike/evidence/task-06-data/final-build-mcp.json)
и [main raw](../epoch-0-design-and-spike/evidence/task-06-data/final-main-mcp.json)
подтверждают success, три названных warnings и **1432 passed / 0 failed**, корректный
main filter. Release/noBuild/noRestore и команды записаны автором. Это предоставленные
результаты координатора, не запуски reviewer. Spike red/green проверены по source/raw;
новая Linux validation не приписана Windows fixes.

Независимые проверки: 11 актуальных Markdown, **352 local links/anchors, broken0**;
54 уникальных production names = 54 table-only names §5 без пропусков;
S01–S42 в §10 ровно 42 уникальных; dependency DAG 7 узлов без цикла task01↔task06.
`git diff --check` exit0. Проверены также новые ссылки review prefix и exact history.
Подробности и ограничения — [coverage round 2](coverage.md).

## История round 1

Ниже исходный round 1 сохранён **дословно**. SHA-256 прежних целых файлов:
result `EEC7E647797352C285E5E3DEA99173BE282DB36CDC1F199DD99A026822830FF2`,
coverage `30FD01FE7616A313676F78FE4E6C2AD70B06D2A2B1FFEAFAD038F14D54D09B4B`.
Его open findings отражают тот срез, а не текущий verdict.

---

# Task-06 — независимое research-task-review, раунд 1

## Главное

- **Вердикт: нужны исправления.** В исходном Linux-срезе найдены 3 major и 2 minor; F01–F05 переданы координатору и приняты им. Проверка исправлений и Windows additions — отдельный раунд 2.
- **F01:** новый `identity-bench` рекурсивно удаляет переданный пользователем scratch, даже если каталог существовал до запуска. До исправления запуск на каталоге с нужными данными опасен.
- **F02:** вывод «свой образ host вместо reuse launcher» пропускает отдельное доказательство происхождения уже запущенного proxy. Linux go допустим по наблюдённым входам ключа, стоимости и FS, но не как доказанный deployment admission.
- **F03:** Windows получает образцы `cold` без eviction; Linux использует непроверенный advisory `posix_fadvise`, который не доказывает чтение с диска. Числа полезны при точной маркировке условий.
- **F04/F05:** benchmark не измеряет atomic publication; в epoch README остались текущие строки `Task-06 planned`.
- Сокращение scope владельцем учтено. Отсутствие Windows и перенос механизмов пунктов 2/3/5, storage failures и сценариев 24–25 в task-02 **не являются самостоятельными findings**. Task-06 остаётся in progress; O-01, activation и приёмка эпохи этим review не выбираются.
- Политика O-05 не оспаривается: общий graph контекста создателя — принятая цена; CLI/private fresh и механизмы receipt ещё не доказаны этим наблюдением.

## Срез и независимость

Дата: **2026-10-29**. Рецензент — отдельный Codex-сабагент
`/root/task01_research_review`, точный model ID **unavailable**.
Исполнитель исходного Linux-результата — `claude-opus-5-5` (Claude Code).
Координатор Windows-дополнений — `gpt-6.1-sol` по сообщению владельца.

Проверен неизменяемый исходный срез **`7882c161ada2fa11df93287047ab90e6a6693b23`**.
В evidence указана база `c67283e`; production version — 1.5.18. Реальные Linux MCP
в наблюдении — 1.5.17 из snapshot основного checkout; это явно записанное отличие,
а не измерение startup нового shared implementation. Во время review координатор
готовит Windows/fixes: они не включены в этот verdict.

Git blob IDs проверенного среза:

| Артефакт | Blob ID |
|---|---|
| [Task-06](../epoch-0-design-and-spike/task-06-environment-and-deployment-preflight-med.md) | `87f96cd9833221587fb622558efc3989e5973e6d` |
| [Linux evidence](../epoch-0-design-and-spike/evidence/task-06-environment-and-deployment-preflight.md) | `a67ef22723242623f076cd6ff50976a339e01b76` |
| [IdentityBench.cs](../../../spikes/PipeRelay/Preflight/IdentityBench.cs) | `0aff8918975aac65884c43603b2f949e97579fa0` |
| [Linux pair JSON](../epoch-0-design-and-spike/evidence/task-06-data/claude-code-linux-pair.json) | `9b1f8f47eadb9956d3b0fec23b8702014c4e185c` |
| [Linux identity JSON](../epoch-0-design-and-spike/evidence/task-06-data/identity-bench-linux.json) | `5e1d8c7c37690e019a0321a23bad37d04a80bd05` |
| [Linux facts JSON](../epoch-0-design-and-spike/evidence/task-06-data/linux-preflight-facts.json) | `ad3df131cc713b3d0e4b5ed6c6fcfef5a874f505` |

Номера строк ниже относятся к этому срезу. Другие review-каталоги не использовались
для формирования findings. Primary docs и C# рецензент не менял; runtime/MCP/builds
не запускал. Применён [research-task-review](../../../.agents/skills/research-task-review/SKILL.md).

## Findings

| ID | Severity | Где | Что не так | Что сделать |
|---|---|---|---|---|
| F01 | major | [IdentityBench.cs](../../../spikes/PipeRelay/Preflight/IdentityBench.cs):23–54; [Program.cs](../../../spikes/PipeRelay/Program.cs):18–20 | `scratch` — произвольный аргумент; `CreateDirectory` принимает уже существующий каталог, а `finally` без проверки владения делает `Directory.Delete(scratch, recursive:true)`. Существующие файлы удаляются даже при ошибке измерения. Совпадение scratch с source/его предком разрушает source; output внутри scratch исчезает после успешного возврата. | Создавать отдельный новый owned run-subdirectory внутри заданного parent либо fail-closed для existing/overlap paths. Удалять только каталог, созданный этим запуском, исключить source/output overlap; проверить сохранность sentinel-файлов и output regression-тестом. Владелец: координатор spike. |
| F02 | major | [Evidence](../epoch-0-design-and-spike/evidence/task-06-environment-and-deployment-preflight.md):141–154; [topic L-01](../README.md):477–478; [design](../design.md):203–218, 881–905 | После no-go launcher audit вывод предлагает свой host image и называет Windows-пару условием окончательного O-01. Но design требует verified происхождение самого proxy с границей до CreateProcess; собственная копия host не легализует неизвестный proxy. Наблюдённые writable snapshot/неполная identity этого доказательства не дают. | Сохранить go только по фактам key/cost/storage; явно записать prelaunch provenance как unverified с получателем task-02 и обязательным gate до shared admission/activation. Согласовать прежние ссылки task-06/no-go в design с разрешённым переносом scope, убрать Windows-only sufficient condition. Не требуется выполнять перенесённый механизм в этом review. Владелец: координатор docs, исполнение — task-02. |
| F03 | major | [IdentityBench.cs](../../../spikes/PipeRelay/Preflight/IdentityBench.cs):34–47, 212–224; [spike README](../../../spikes/PipeRelay/README.md):101–104; [Evidence](../epoch-0-design-and-spike/evidence/task-06-environment-and-deployment-preflight.md):94–123 | Цикл всегда выпускает `cold`/`warm`, хотя на Windows Evict ничего не делает. На Linux return code advice игнорируется, residency не проверена, но комментарий обещает disk read, README — холодный кеш, вывод — доказанный cold hash. `posix_fadvise` advisory; DONTNEED лишь пытается освобождать страницы. | Windows — только warm, cold unavailable. Linux — `cold-advised`/best-effort с учётом ошибок advice и явным отсутствием доказанного cold; не переименовывать старый raw в реально выполненный новый метод. Для подтверждённого cold нужен отдельный проверяемый метод. Владелец: координатор spike/evidence. |
| F04 | minor | [Evidence](../epoch-0-design-and-spike/evidence/task-06-environment-and-deployment-preflight.md):114–115; [IdentityBench.cs](../../../spikes/PipeRelay/Preflight/IdentityBench.cs):84–93, 124–151 | Текст приписывает защиту от частичной копии `fsync` и atomic rename, но benchmark только flush-ит каждый файл, перечитывает staging и удаляет его. Rename/publication/crash/ENOSPC в нём нет. | Обозначить fsync как измеренную операцию, а atomic publication и отсутствие partial launch — как немеренный контракт task-02. Не выдавать timing benchmark за проверку durable/atomic image publication. Владелец: координатор evidence. |
| F05 | minor | [Epoch README](../epoch-0-design-and-spike/README.md):66, 177 | В актуальных summaries сохранилось `Task-06 planned`, при header/table/topic/task in progress. Строка 66 говорит о нынешней зависимости, а не о датированном историческом срезе. | Обновить текущие summaries на in progress с Linux/Windows scope; исторические записи сохранить явно как историю. Счётчики review обновляет координатор после интеграции. Владелец: координатор bookkeeping. |

Для F03 проверена первичная документация: advice не binding, DONTNEED attempts to free,
return 0 означает успех вызова, а не доказанную residency.+[Linux man-pages: posix_fadvise(2)](https://man7.org/linux/man-pages/man2/posix_fadvise.2.html).

## Проверка ключевых утверждений по коду

1. **Подтверждено:** `get_mcp_server_info` не доказывает full image identity.
   [McpServerInfoHelper.cs](../../../Services/McpServerInfoHelper.cs):51–73 берёт
   `Environment.ProcessPath` и его mtime, выводит assembly version/base directory/cwd.
   При `dotnet server.dll` timestamp относится к dotnet. Оговорка Linux-отчёта верна;
   совпавший путь snapshot остаётся наблюдением, а не manifest proof.
2. **Подтверждено:** config/plugin context фиксируется при startup из exe/cwd/env.
   [Program.cs](../../../Program.cs):56–59, 96–101 читает settings до plugin pass;
   [McpServerInfoHelper.cs](../../../Services/McpServerInfoHelper.cs):168–205 выводит
   origins/merged settings. Эти ответы пригодны для наблюдения контекста, но не для
   проверки неизменности всех bytes образа.
3. **Подтверждено:** CLI имеет собственный процессный контекст и env overrides.
   [DotNetCliRunner.cs](../../../Services/Build/DotNetCliRunner.cs):326–346 создаёт
   `ProcessStartInfo` с cwd, без очистки всей среды; добавляет pinned SDK,
   `MSBUILDDISABLENODEREUSE`, язык. MSBuild counterexample про environment-dependent
   graph согласуется с этой границей; source не доказывает конкретный выполненный
   Linux fixture вместо предоставленного raw observation.
4. **Подтверждено:** benchmark хеширует содержимое выбранного file set, hash-ит при
   копировании, flush-ит файл, проверяет digest копии; manifest reuse проверяет
   существование/length/mtime, а не повторный content hash.
   [IdentityBench.cs](../../../spikes/PipeRelay/Preflight/IdentityBench.cs):105–183.
   Stale-stat counterexample соответствует алгоритму; atomic publication не реализована.
5. **Подтверждено:** новые shared/private механизмы не являются production behavior.
   [WorkspaceTools.cs](../../../Tools/WorkspaceTools.cs):35–55 не содержит forceFresh;
   [RoslynMcpServer.csproj](../../../RoslynMcpServer.csproj):27–29, 67 сохраняет 1.5.18
   и исключает spikes из production compile. Перенос receipt/private/admission
   проверок в task-02 не считается их реализацией.

## Числа и границы выводов

Из raw JSON независимо пересчитаны **6 samples, по 3 на label** и
`111069385 / 1048576 = 105,924020 MiB`, 231 файл. Диапазоны evidence после округления
совпадают: hash 679,9512–757,1686 / 99,5724–102,4523 мс; copy+hash+file flush
2516,4565–2731,2125 / 2010,0885–2227,6832 мс; verify 96,1432–105,2654 /
95,2264–97,0250 мс; reuse 1,5621–15,1980 / 1,3959–2,1698 мс. Для labels действует F03.
Workstation GC, 2 CPU, ext4 и runtime записаны; влияние Server GC не измерено.
Guarded startup, whole initialize, attach и time-to-first-useful этими числами
не измерены. Harness budget/macOS честно оставлены неизвестными с получателем.

Linux pair содержит три реальных PID: два headless и интерактивный MCP, одинаковые
cwd/image path/euid, различия session env names без значений. Negative control
другого checkout одновременно отличается image — это отрицательный контроль,
не positive compatible pair. Полный namespace/content admission и collapsed load
в shared implementation не проверены; отчёт в основном делает эту оговорку правильно.
Raw аудита внешнего launcher — структурированное наблюдение автора: сам скрипт и
Linux машины здесь недоступны; fd9 inheritance/FS facts независимо не воспроизводились.

## Независимый completeness probe и получатели

Независимо от метода автора прослежены scratch/source/output ownership и все
`CreateDirectory`, `Delete`, `Move`, `WriteAllText`, `Flush` нового benchmark: так
получены F01/F04. Дополнительно искались process-global mutations в production
(без tests/spikes/samples/forks): cwd setter в Program, plugin AssemblyLoadContext,
AsyncLocal/ThreadStatic search diagnostics; скрытого runtime env rewrite не найдено.

По отдельной просьбе координатора проверена зависимая новая запись O-02.
**Это отдельный риск входного design, не шестой finding task-06:**
[reset decision](../evidence/2026-10-29-task-01-reset-decision.md):39 и
[design](../design.md):661 ошибочно трактуют `readOnly` из каталога как proof
отсутствия effects. [McpToolCatalog.cs](../../../Hosting/McpToolCatalog.cs):215–216
помечает list/graph readOnly, но [UtilityTools.cs](../../../Tools/UtilityTools.cs):1420,
1485 может запускать LoadAsync. Semantic read также вызывает config lazy load через
[SolutionManager.cs](../../../Services/Workspace/SolutionManager.cs):441–443;
MSBuild open/capture достигается через :2134 и
[AnalyzerProvenanceCaptureService.cs](../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs):195–204.
До чистого read-stage возможны load/prepare/generated outputs/произвольные MSBuild
effects. **Получатели: task-01/O-02 для уточнения стадий cancellation и task-02 для
admission/reset regression.** Координатору передано с source-ссылками; выбор владельца
global retire этим не оспаривается, production bug не объявляется.

## Следующий шаг

Координатор исправляет F01–F05, сохраняет происхождение Linux raw и отдельно добавляет
Windows evidence/validation. Тот же reviewer проверяет diff и Windows additions в
раунде 2. Текущее review завершено как проверка с findings; это не acceptance task-06.
Подробности границ — [coverage](coverage.md).
