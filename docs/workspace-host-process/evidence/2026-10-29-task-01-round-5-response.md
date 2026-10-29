# Task-01: ответ и исправления по раунду 5

## Главное

- База обновлена fast-forward с `e9de5ee` до **`6abad7c`**, production **1.5.18**. Прочитаны [result](../review-task-01-round-5-2026-10-29/result.md), [coverage](../review-task-01-round-5-2026-10-29/coverage.md), raw JSON и `.status`; оригиналы не редактируются.
- F-01 **PARTIALLY ACCEPT**: raw inherited environment не обеспечивает схлопывание. Добавлен proposed O-05 canonical-v1 с явной ценой parity; реальный go/no-go перенесён до выбора модели в task-06. Разные checkout исходной пары не объявлены совместимой группой.
- F-02 **ACCEPT**: rendezvous и storage разделены, image/logs исключены из `/tmp`; storage проверяет backing FS/noexec/space и обрабатывает ENOSPC без partial launch.
- F-03 **PARTIALLY ACCEPT**: предлагается soft reset/новая epoch с сохранением процесса; hard retire — при restart-required/неисправимом admission. Это не обещает дешёвую перезагрузку самого graph.
- F-04 **PARTIALLY ACCEPT**: identity/copy/verify имеют явные budgets и metrics, pinned identity считается один раз на процесс/образ; stat stamps не заменяют content hash.
- F-05 **ACCEPT**: учтён внешний launcher и conditional reuse verified snapshot; лишние RID outputs зарегистрированы как [L-01](../README.md#l-01-launcher) с владельцем. Внешний скрипт не менялся.
- Новый major **R6-N01 — ACCEPT**: late identity pin заменён проверяемой immutable publication/use lease до CreateProcess; raw mutable proxy без proof отклоняет shared. Исправление подтверждено в раунде 7.
- Добавлен [task-06](../epoch-0-design-and-spike/task-06-environment-and-deployment-preflight-med.md), **planned**, для дешёвого preflight до окончательного task-01. У task-06 нет зависимости от завершённого task-01, цикла нет. Task-02 не начат.
- O-01…O-04 всё ещё отложены владельцем; пересмотренный O-02 и новый O-05 не выбраны. Design остаётся proposed. Раунды 6–7 подтвердили fixes; итого task-01 **7 раундов / 12 исправлений**.

Исполнитель ответа/правок — GPT Sol-6.1 (`gpt-6.1-sol`, уточнено владельцем
2026-10-29), Codex. Изменяются только
документы. Наблюдения раунда 5 принадлежат `claude-opus-5-5` на его native Ubuntu;
это не новые локальные Windows/WSL измерения автора.

## r5/F-01 — PARTIALLY ACCEPT

Согласен с дефектом полного environment hash и привязкой прежнего preflight только
к CODEX именам. Не согласен с доказательством «два измеренных совместимых MCP»:
[raw](../review-task-01-round-5-2026-10-29/linux-env-and-image-probe.json) прямо говорит
о разных checkout; PWD/CLAUDE_PROJECT_DIR различны по этой причине. Их key и должен
различаться по workspace/startup context. Остальные session/terminal differences
подтверждают общий риск, но для положительной совместимой пары нужен новый контроль.

В §2 [design](../design.md) O-05 сравнивает canonical-v1 и strict/ordinary. Canonical
environment строится **до** запуска host; именно её получают MSBuild и children,
а policy/digest входят в key. Удаляются документированные families correlation и
terminal/login metadata; credentials и оставшиеся inputs сохраняются, CLI proxy
получает прежнюю полную среду. Удаление только из hash отвергнуто.

Цена явно записана: проект/plugin, читающий удалённое имя, может отличаться от ordinary.
Выбор O-05 нужен владельцу до activation; нельзя выдать такую нормализацию за parity.
MSBuild logging не объявлен полным detector прямых Environment reads custom tasks.
Task-06 исследует этот предел на изолированном fixture. До O-01 он также сравнивает
точный descriptor реальных пар **одного checkout** Codex/Windows и Claude Code/Linux;
task-02 затем подтверждает policy на реализации. Данные несовпадающих checkout и
прежние CODEX presence probes не закрывают этот gate.

Следствия: proposed environment contract, диагностические names-only различия в
server info, дополнительный input/task-06 перед выбором модели. Публичные tool
параметры/CLI среды сейчас не меняются; это не новый факт реализованной экономии RAM.

## r5/F-02 — ACCEPT

В §3/§8/§9 разделены **RendezvousRoot** (locks/socket/малые metadata/activity records)
и **StorageRoot** (image/staging/logs). Linux storage использует абсолютный
XDG_CACHE_HOME или `~/.cache`; macOS — user Caches; Windows — LocalApplicationData.
Проверяется actual backing filesystem: одно имя `.cache` не доказывает дисковое
размещение. Storage tmpfs/ramfs/noexec отвергается; space preflight не резервирует
место и не исключает ENOSPC. Copy failure не запускает partial image и не переносит
её обратно в `/tmp`. ACL/UID/symlink/immutable/use-lease гарантии сохранены.

Правило cache/default/absolute paths сверено с
[XDG specification](https://specifications.freedesktop.org/basedir/latest/).
Linux [mmap(2)](https://man7.org/linux/man-pages/man2/mmap.2.html) описывает отказ
PROT_EXEC на noexec filesystem; native image не считается проверенным одним успешным copy.
Task-06/task-02 получают проверки roots/noexec/space. Здесь эти runtime checks не запускались.

## r5/F-03 — PARTIALLY ACCEPT

Факт регулярных **рецептов** reset подтверждён в
[AGENTS.md.sample:68–83](../../../AGENTS.md.sample#L68) и
[README:1220](../../../README.md#L1220). При этом инструкция прямо говорит
«not after every build and not for ordinary source saves»; реальная частота reset
у N агентов не измерена. Поэтому не выдаём риск за уже измеренную потерю всего выигрыша.

O-02 теперь рекомендует soft reset: admission/drain, clear и новая epoch, тот же
процесс при живых клиентах, single-flight следующего load. Повторные reset той же
invalidation coalesce; запоздавший запрос не сбрасывает новый опубликованный graph.
Drain timeout до clear возвращает busy. Старые tickets/proofs запрещены; proxy CLI
не убивается от reset. Инициатор сбрасывает свои defaults; соседи сохраняют descriptor.
Если клиентов не осталось, прежняя grace всё равно может завершить host.

Это **не** сохранение прогрева disposed workspace: следующий physical load может
быть долгим. Hard retire остаётся для подтверждённого restart-required и неисправимого
admission/cleanup; обычный project load failure остаётся shared failure, не restart loop.
Если V2 analyzer выявлен лишь после soft reset, load сначала сообщает restart-required,
а следующий reset выполняет hard retire. Default reset/stop и продуктовые инструкции
этой docs-only правкой не меняются. Альтернатива global retire сохранена для выбора
владельцем; сценарий 20/task-02 измеряет latency соседей, load/retire counts и рецепты reset→load.

## r5/F-04 — PARTIALLY ACCEPT

Согласен разместить затраты и исключить повторный полный hash на каждом attach/RPC.
Рецепт кеширования только по stat не принимается как identity proof: изменённые
bytes с сохранёнными size/mtime не должны стать прежней сборкой. Это нарушило бы
полный content manifest, принятый после предыдущих замечаний.

В §2/§7/§9 введены pinned process/image identity и conditional reuse audited immutable
publisher manifest под непрерывной prelaunch use lease. Копирование считает hash
вместе с чтением и проверяет полный manifest. Stat служит invalidation hint;
изменение source не присваивает работающему proxy новую code identity.
Startup identity имеет предел 30 с и дополнительно budget initialize harness;
discovery/copy/verify — 30 с внутри оставшегося caller budget. Existing CLI timeout
не продлевается. Metrics разделяют bytes/files/cache mode и фазы, startup включён
в time-to-first-useful. Task-06 измеряет cold/warm, task-02 весь attach.
Наблюдения 0,98 с / 0,26 с / 15,11 с
относятся к разным bundle/cache условиям одного прогона раунда 5, не к средним значениям
или гарантированной цене production attach.

## r5/F-05 — ACCEPT

§9 различает Windows publish holders и Linux launcher snapshot. Возможность reuse
предпочитается второй копии лишь после audit полного manifest/file set, владельца,
FS, immutable publication, config origin и **совместимого** lease/cleanup протокола.
Имена `<hash>` и любой shared flock сами этого не доказывают; fcntl на другом lock
не защищает snapshot launcher. Не прошедшая audit snapshot не удаляется новым reaper;
fallback — собственная проверенная копия на StorageRoot.

L-01 фиксирует внешний launcher/RID bloat с owner и ссылкой на исходный замер.
Ремонт скрипта вне checkout не включён в эту работу. Task-06 проверяет правильный
file set; broad exclusion всех subdirectories отвергнут из-за native/BuildHost/plugin dependencies.

## R6-N01 — ACCEPT

Раунд 6 подтвердил пять исходных r5 fixes, но обнаружил новый major в моей правке:
hash при первом shared call мог описывать новые файлы, когда proxy уже исполнял
старые загруженные assemblies. Согласен; это отдельный finding, не продолжение F-04.

Теперь §2 требует проверяемую immutable publication/use lease **до OS CreateProcess**,
включая стартовый exe/server image, с ранним guard до Roslyn/MSBuild/plugins.
Managed Program lock и поздний hash не доказывают происхождение уже исполняемого кода.
Raw mutable deployment без proof отклоняет shared с deployment-unverified; нужен
restart через verified image, ordinary default сохраняется. Действующий Windows
controlled-stop publish не объявлен готовым startup guard. Наличие такой границы
и initialize latency — go/no-go task-06; task-02 повторяет source-update race.
Это уточнение proposed contract, bootstrap сейчас не реализован и не проверен.

## Validation и учёт

Применён research-task-authoring: go/no-go перед окончательным выбором, отдельные
цены/альтернативы O-02/O-05, известные primitives, L-01 с owner и неизвестные с
принимающими task-06/task-02/E1. План дополнен, без запуска task-06 или activation.
Research-task-review выполнен в отдельном сабагенте Codex
`/root/task01_research_review` (точный model ID недоступен):
[result](../review-task-01-2026-10-29/result.md),
[coverage](../review-task-01-2026-10-29/coverage.md). Раунд 6 подтвердил пять r5 fixes,
но нашёл R6-N01; раунд 7 подтвердил его исправление без новых must-fix в затронутом
контракте. Это шесть дополнительных уникальных fixes к прежним шести.
Исходный reviewer `claude-opus-5-5` повторно новый diff не проверял.

Финальные links/anchors и `git diff --check` — PASS; 54/54 уникальных tools,
42/42 state IDs, семь task nodes без циклов. Зависимости шапок совпадают с epoch
index. Четыре исходных файла раунда 5 неизменны относительно HEAD.
[Машинная validation](../epoch-0-design-and-spike/evidence/task-01-data/validation-round-5.json)
содержит базу, counts, source hashes и проверенный scope. Task-01 in progress,
task-06/task-02 planned; epoch acceptance не объявлена. Правки плана проверены
как input/go-no-go этого diff, полное исходное ревью плана E0 не повторялось.
Build/main/SourceStructure/AnalyzerLifecycle не запускались: C#/tests/build settings
не меняются. Runtime preflight task-06 ещё не выполнен.
