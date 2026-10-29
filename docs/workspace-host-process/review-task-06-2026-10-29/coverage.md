# Task-06 — покрытие независимого review, раунд 2

## Главное

**F01–F05 закрыты, новых must-fix нет; принято в сокращённом scope результата.**
2026-10-29, тот же Codex `/root/task01_research_review`, точный model ID unavailable.
HEAD `7882c161` + working diff; [hashes/verdict](result.md). Итог — 2 rounds / 5 fixes.
Windows additions проверены отдельно от сохранённого исходного Linux-среза.

## Прочитано и проверено

- Повторно полностью актуальные skill research-task-review и AGENTS; docs README
  bookkeeping. Актуальный task-06, evidence и author response полностью, changes
  topic/epoch/docs indices, task-01/task-02 и design в affected slice.
- Полностью IdentityBench.cs (294 строки), новый IdentityBenchChecks.cs, dispatch
  и новый README раздел. C# проверялся функционально, стиль не оценивался.
- Все **12 новых Windows JSON полностью**: root/agent info, FS/process facts,
  negative CLI, две identity серии, C: staging method/ACL, environment fixture,
  red/green checks, final build/main. 3 Linux JSON не менялись — blob hashes
  независимо сравнены с round 1, повторной Linux runtime validation не заявлено.
- Read-only source claims: path validation/owned cleanup/output CreateNew;
  labels/advice error check; copy hash/flush и отсутствие publication; прежние
  info limitations и отсутствие production shared/fresh API не изменились.
  Новая цепочка reset stages сопоставлена с R10-F01 source proof, отдельный round11.

## Независимый completeness probe

Проверены failure-path cleanup/ownership, output внутри scratch parent, overlaps,
CreateNew и сохранность existing output, соответствие новых checks исходным red
failure. Это не повтор benchmark: source/control-flow inspection без запуска.
Статический поиск старых unconditional readOnly/no-effects, прежних receiver
task-06 prelaunch и planned summaries нашёл два остатка: budget Retire/drain и
design header; координатор устранил их до verdict. Вводить новые IDs за те же
причины не требовалось. Pure-stage wording и task-02/E1 receiver проверены снова.

Независимо от авторских чисел:

- JSON parse/count/min/max/MiB/GiB для двух Windows серий и free space;
- фактический текущий Windows build output по exclusions: 231 / 111181807;
- source catalog и только таблица §5: 54/54 уникальных tool names;
- первая колонка всех строк §10: 42 unique S01–S42; первоначальный exploratory
  regex считал только строки с одним S и не был принят за completeness result;
- Depends on header graph 7 узлов, acyclic; task01 ждёт task06, task06 зависит
  только от task04/05. Proposed design input не превращён в обратную зависимость;
- 11 primary/current Markdown: 352 local file/heading/explicit anchors, broken0,
  без исторических file:line pointers; line anchors отделены от heading anchors.
  Новые review-prefix links проверены отдельно, история не переписывалась;
- `git diff --check` exit0; pending coordinator bookkeeping не считается ни pass
  до его выполнения, ни новым содержательным finding в этом техническом round.

## Границы acceptance

Runtime/MCP/build/tests reviewer не запускал. Build/main/red-green — прочитанные
результаты координатора с raw, не собственные выполненные проверки reviewer.
Windows основная серия warm, Linux new advice branch не запускалась; verified cold,
actual root security, full image trust/admission, end-to-end startup и macOS —
unverified с явным receiver. Owner scope cut не восстанавливался как missing work.

Результат даёт владельцу observations для O-01. Task-02/production host/Roslyn
integration и activation не запускаются review acceptance. Task/index/counters
завершает координатор. Primary docs/C# reviewer не менял, commit/push не делал.

## История

Round 1 ниже оставлен exact suffix; прежние SHA-256 и проверка сохранности — в
result. `.status` completed пишется последним для round 2.

---

# Task-06 — покрытие независимого review, раунд 1

## Главное

Независимая проверка Linux-среза `7882c161ada2fa11df93287047ab90e6a6693b23`,
2026-10-29. **3 major, 2 minor; нужны исправления.** Рецензент Codex
`/root/task01_research_review`, точный model ID unavailable.
Windows additions и fixes координатора ожидают отдельного раунда 2.
[Verdict, стабильные IDs и hashes](result.md).

## Прочитано

- Полностью [research-task-review](../../../.agents/skills/research-task-review/SKILL.md),
  [AGENTS.md](../../../AGENTS.md), раздел учёта [docs README](../../README.md),
  текущие topic/epoch README, task-06 и task-06 evidence.
- Все три JSON исходного `task-06-data`: Linux pair, identity samples, FS/launcher/
  MSBuild facts. Зафиксированы git blob IDs; новые Windows файлы сюда не включены.
- Полностью IdentityBench.cs и его пять model records, command dispatch Program,
  новый раздел spike README; production version/exclusion в csproj.
- В design прочитаны затронутые key/environment/admission, roots, deployment/
  launcher, budgets/scenario21 и O-01/O-05 contracts. Проверены получатели переноса
  механизмов task-02. Исторические task-01 reviews не использовались.
- Production spot-check: Program startup/settings/plugins, McpServerInfoHelper,
  DotNetCliRunner, WorkspaceTools load signature; по независимому probe —
  McpToolCatalog/McpToolDescriptor, UtilityTools list/graph, SolutionManager
  lazy/published/load boundary и AnalyzerProvenanceCaptureService.
- По отдельной просьбе координатора — новая O-02 reset-decision запись и её
  зависимые design claims. Это отдельный tracked risk, не расширение findings task-06.

## Покрытие spec с учётом решения владельца

| Категория | Наблюдение review |
|---|---|
| Реальная Linux-пара одного checkout | Три PID, одинаковые cwd/image/euid, два headless info; names-only отличия env. Выполнено как observation входов; реальное shared collapse не заявляется доказанным. |
| Windows-пара | Unverified в исходном срезе; явная незавершённость, не баг. Координатор делает additions отдельно. |
| Negative controls | Другой checkout есть; properties на реализации переданы task-02. Нельзя подменить positive pair отрицательным контролем. |
| Receipt/cache/created-reused/launch races, private fresh, recovery | Пункты 2/3 и сценарии24–25 перенесены владельцем в task-02. Не требовались здесь как работающий механизм. |
| MSBuild env counterexample | Условный Compile с PROBE_FEATURE даёт разные списки в raw facts. Фиксация принимается как авторское Linux observation, сам runtime не воспроизводил. |
| Storage facts | Actual FS/mount/parent permissions/free space/socket lengths записаны. Reject/symlink/space/ENOSPC механизмы не доказаны и оставлены task-02. |
| Identity costs/stat trap | n=3+3, bytes/files/runtime/GC/source и same-size/mtime counterexample записаны. Ranges пересчитаны. F03 ограничивает cold; F04 отделяет publication. |
| Guarded startup/initialize/time-to-first-useful | Не измерены. Перенос prelaunch proof в task-02 допустим; F02 требует не выдавать остальные observations за снятие admission gate. |
| Launcher audit | No-go reuse, полные зависимости не удаляются по широкой маске. L-01 имеет owner. Аудит внешнего файла доступен лишь как raw observation, не локальный source. |
| Summary/bugs/unknowns | Главное есть; L-01 и receiver task-02 перечислены. Новые F01–F05 записаны review и переданы координатору. |
| Bookkeeping | In progress в task/index/header, platform limitation честная. Остатки planned — F05. Review counters ещё — допустимы до этого review; coordinator обновляет их после интеграции. |

## Независимые команды и проверки

- Read-only `git rev-parse`, `git log`, `git show`, `git ls-tree` зафиксировали HEAD,
  drift от evidence base, production 1.5.18 и hashes первичных артефактов.
- `rg` по production для `SetCurrentDirectory`, `SetEnvironmentVariable`,
  `AppContext.SetSwitch`, `AssemblyLoadContext`, `Console.Set`, `ThreadStatic`,
  `AsyncLocal`; test/spike/sample/fork файлы не считались production findings.
  Найдены Program cwd setter, plugin ALC, diagnostic AsyncLocal/ThreadStatic.
- `rg` по новому benchmark для cleanup/create/move/write/flush и trace callers:
  произвольный scratch не имел ownership check; publication rename отсутствовал.
  Это независимый completeness probe, не повтор авторской latency-команды.
- PowerShell `ConvertFrom-Json`, grouping/count/min/max и MiB division повторно
  вычислили raw samples, все диапазоны в result.md и 105,924020 MiB.
  Первый exploratory запрос использовал несуществующие имена свойств, давал null;
  он не считался результатом и повторён с реальными именами records.
- По cache API проверена первичная
  [Linux man-pages](https://man7.org/linux/man-pages/man2/posix_fadvise.2.html):
  advisory semantics, DONTNEED и return errors. Это проверка контракта, не runtime test.
- `git diff --check` до записи каталога — exit0. Собственные local Markdown file
  links и финальный diff каталога проверены до `.status`; execution marker пишется последним.

## Не запускалось и ограничения

Не запускались benchmark, процессы MCP/harness/launcher, load workspace, MSBuild
fixture, build/tests, Linux mount/FD probes, Windows/macOS integration. Это прямое
ограничение делегированного независимого review; coordinator владеет Windows и
финальной AGENTS validation. Авторские main-suite 1428/1428 и build claims
перечислены в исходном evidence, но reviewer их не присваивает себе как выполненные.
SourceStructure после production C# не требовался: production C# не менялся.
Нового C# рецензент не пишет и стиль не оценивает.

Локального Linux output file set и внешнего launch.sh нет. Поэтому 231 файла как
наличие Linux dependency set, все audit statements/fd9 inheritance, версия живых
Linux PID и compile-item результаты не независимо воспроизводимы на этом Windows
checkout. Их метод и raw provenance проверены как наблюдения с названным автором.
Результат не доказывает content admission по одному image path.

Прочие reviewer-каталоги до формирования findings не читались. Primary docs/code
не изменялись reviewer. Создан только этот каталог; commits/push не выполнялись.
