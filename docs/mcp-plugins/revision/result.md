# Specification Revision Result

## Status

`COMPLETED`

Предыдущие файлы не переписывались при ревизии. Норма — `docs/mcp-plugins/v2/`. Требования v1, review, защита и арбитраж лежат в `docs/mcp-plugins/_archive/`.

## Applied changes

| P-ID | Human state | Revision state | Requirement/section |
|---|---|---|---|
| P-001 | ACCEPTED | APPLIED | `v2/README.md` (пример и три входа), `v2/authoring.md` («Чем пользоваться»), `v2/epoch-5-assembly-load.md` (тест диска) |
| P-002 | ACCEPTED | APPLIED | `v2/README.md` («Совместимость»), `v2/authoring.md` (таблица `minHostVersion` и оси), `v2/_archive/epoch-2-manifest-and-paths.md` |
| P-003 | ACCEPTED | APPLIED | `v2/authoring.md`, `v2/_archive/epoch-4-sample-plugin.md`; location частной DLL — `v2/epoch-5-assembly-load.md` |
| P-004 | ACCEPTED | APPLIED | `v2/authoring.md` |
| P-005 | ACCEPTED | APPLIED | `v2/_archive/epoch-1-registration.md`, `v2/epoch-5-assembly-load.md` |
| P-006 | ACCEPTED | APPLIED | `v2/_archive/epoch-1-registration.md`, `v2/epoch-5-assembly-load.md`, `v2/epoch-6-host-startup.md` |
| P-007 | ACCEPTED | APPLIED | `v2/_archive/epoch-1-registration.md` |
| P-008 | ACCEPTED | APPLIED | `v2/_archive/epoch-2-manifest-and-paths.md`; стык со стартом — `v2/epoch-6-host-startup.md` |
| P-009 | ACCEPTED | APPLIED | `v2/_archive/epoch-3-shadow-copy.md` (тест байтов не про ALC), `v2/epoch-5-assembly-load.md` |
| P-010 | ACCEPTED | APPLIED | `v2/README.md` (общий файл), `v2/_archive/epoch-4-sample-plugin.md`, `v2/epoch-7-documentation.md` |
| P-011 | ACCEPTED | APPLIED | `v2/epoch-5-assembly-load.md` |
| P-012 | ACCEPTED | APPLIED | `v2/README.md` (граница), `v2/authoring.md`, `v2/_archive/epoch-3-shadow-copy.md`, `v2/epoch-5-assembly-load.md` |
| P-013 | ACCEPTED | APPLIED | `v2/epoch-6-host-startup.md` |
| P-014 | ACCEPTED | APPLIED | `v2/epoch-6-host-startup.md` |
| P-015 | ACCEPTED | APPLIED | `v2/epoch-6-host-startup.md` |

Носитель версии ссылки, который спека обязана была назвать (`P-002`): compile-запись в `deps.json` рядом с entry. Поля `plugin.json` под версии ссылок не вводились: имён полей в решении нет. Разбор `deps.json` не нормирован.

## Human modifications

Нет. `P-001`…`P-015` приняты как предложено арбитражем.

## Rejected proposals

Нет.

## Unresolved

Нет. `U-001` закрыт человеком 2026-09-27 и входит в `P-002`.

## Pending arbiter findings

`A-001` человеком 2026-09-27 выведен из серии. Отдельное ревью не заказано. Гейт `Microsoft.Extensions.*` в v2 не добавлен. В контракте остаётся принятый остаток: другой мажор этого семейства может дать `TypeLoadException` после записи в info.

## Revision blockers

Нет.

## Requirements created

Отдельной схемы ID у темы нет, новые ID не вводились. Новые нормы живут в существующих разделах копий v2:

- три входа `SolutionManager` и приёмка диска — `P-001`;
- оси ссылок до `Load` и носитель `deps.json` — `P-002`, `U-001`;
- частная managed-зависимость в выходе — `P-003`;
- ссылка на DLL publish — `P-004`;
- нет видимого хвоста после исключения `Register` / `Create` — `P-005`;
- чтение коллекции после резолва options — `P-006`;
- уникальность имени на контейнер — `P-007`;
- ошибка типа jsonc → `ParseFailures` — `P-008`;
- замок ALC в эпохе 5 — `P-009`;
- исключение `samples\**` — `P-010`;
- три стадии резолва — `P-011`;
- native вне v1 — `P-012`;
- subprocess smoke — `P-013`;
- stderr сразу, лог после `Build` — `P-014`;
- help из отчёта старта, `Kind`/`Group` = `plugin` — `P-015`.

## Requirements modified

Текст v2 заменяет соответствующие абзацы предыдущего контракта. Файлы предыдущего контракта на диске не менялись.

- Пример анализа и список входов `SolutionManager` → `GetSanitizedPublishedSolutionAsync`, два других входа названы → `P-001`.
- «Та же `Version` / тот же мажор MCP» → две оси, `minHostVersion` остаётся нижней границей продукта → `P-002`.
- «Свой пакет копируется как обычно» → частные managed-зависимости копируются, контракт хоста нет → `P-003`.
- Ссылка на exe → ссылка на `RoslynMcpServer.dll` → `P-004`.
- «Откатывать нечем» и граница «`Register` вернулся» → нет тулов и сервисов этого плагина в контейнере → `P-005`.
- «После `Build` фабрики видны» → чтение после `IOptions<McpServerOptions>.Value` → `P-006`.
- «В этом процессе» → один `IServiceCollection` → `P-007`.
- Ошибка типа `plugins` «как у других ключей» (падение) → `ParseFailures`, хост стартует → `P-008`.
- Тест байтов эпохи 3 → не доказательство ALC; замок перенесён в эпоху 5 → `P-009`.
- Шаблон «в хостовый csproj не входит» → плюс исключение `samples\**`, эпоха 7 его не снимает → `P-010`.
- «Нет пути резолвера — пропуск» → три стадии, framework miss не пропуск, частная DLL не берётся из хоста → `P-011`.
- Копирование `runtimes/` без оговорки → native в v1 не резолвится → `P-012`.
- In-process `PluginStartup` как единственная приёмка процесса → плюс subprocess smoke entry point → `P-013`.
- «Ту же строку пишет лог» без момента → stderr в момент пропуска, файл лога после успешного `Build` → `P-014`.
- Help «берёт `[Description]`» без носителя → отчёт старта, не каталог → `P-015`.

## Requirements superseded/removed

Предыдущий контракт (README, authoring, эпохи 1–7) больше не норма и лежит в `docs/mcp-plugins/_archive/v1/`. Норма — `docs/mcp-plugins/v2/`. Указатель в `docs/README.md` ведёт на v2.

Внутри v2 сняты формулировки, которые противоречили принятым исходам: «откатывать нечем», дедупликация «в этом процессе», пропуск любого промаха `AssemblyDependencyResolver`, обещание что свой пакет (включая native) годен загрузчику, ссылка на apphost.

## Consistency check

Сверены стыки из арбитража, уже на тексте v2:

- `P-005`, `P-006`, `P-007`, `P-015`: в контейнер попадают только принятые тулы; коллекцию читают после резолва options; откат освобождает имя того же контейнера; help хранит только принятые метаданные. Второго списка тулов из отчёта нет.
- `P-011` не обещает native и не подставляет частную DLL из хоста. `P-012` это не отменяет.
- `P-003` не отменён `P-012`: managed-пакет копируется, native не обещается.
- `P-009` не подменяет `P-003`: замок entry обязателен; location частной DLL обязателен, когда фикстура эпохи 4 загружается.
- `P-010` не конфликтует с запретом эпохи 7 менять csproj: запрет не снимает уже требуемое исключение `samples\**`.
- `P-008` не подменяет `P-005`: ошибка типа jsonc — `ParseFailures` и живой хост; исключение `Register` / `Create` — пропуск одного плагина.
- `P-013` и `P-014` делят эпоху 6 без второго discovery. Smoke проверяет протокол процесса. Файл лога проверяет in-process приёмка.
- `P-001` не добавляет метод `SolutionManager` и не обходит загрузчик.
- `P-002` включает `U-001`. Exact minor Roslyn не требуется. Одна `Version` продукта не закрывает оси MCP и Roslyn.
- `A-001` не добавлен в оси `P-002`.

Противоречий, которые ревизия не смогла снять механически, нет.

## Complete decision ledger

- `P-001` → ACCEPTED → APPLIED → `v2/README.md`, `v2/authoring.md`, `v2/epoch-5-assembly-load.md`
- `P-002` → ACCEPTED → APPLIED → `v2/README.md`, `v2/authoring.md`, `v2/_archive/epoch-2-manifest-and-paths.md`
- `P-003` → ACCEPTED → APPLIED → `v2/authoring.md`, `v2/_archive/epoch-4-sample-plugin.md`, location в `v2/epoch-5-assembly-load.md`
- `P-004` → ACCEPTED → APPLIED → `v2/authoring.md`
- `P-005` → ACCEPTED → APPLIED → `v2/_archive/epoch-1-registration.md`, `v2/epoch-5-assembly-load.md`
- `P-006` → ACCEPTED → APPLIED → `v2/_archive/epoch-1-registration.md`, `v2/epoch-5-assembly-load.md`, `v2/epoch-6-host-startup.md`
- `P-007` → ACCEPTED → APPLIED → `v2/_archive/epoch-1-registration.md`
- `P-008` → ACCEPTED → APPLIED → `v2/_archive/epoch-2-manifest-and-paths.md`
- `P-009` → ACCEPTED → APPLIED → `v2/_archive/epoch-3-shadow-copy.md`, `v2/epoch-5-assembly-load.md`
- `P-010` → ACCEPTED → APPLIED → `v2/README.md`, `v2/_archive/epoch-4-sample-plugin.md`, `v2/epoch-7-documentation.md`
- `P-011` → ACCEPTED → APPLIED → `v2/epoch-5-assembly-load.md`
- `P-012` → ACCEPTED → APPLIED → `v2/authoring.md`, `v2/_archive/epoch-3-shadow-copy.md`, `v2/epoch-5-assembly-load.md`, граница в `v2/README.md`
- `P-013` → ACCEPTED → APPLIED → `v2/epoch-6-host-startup.md`
- `P-014` → ACCEPTED → APPLIED → `v2/epoch-6-host-startup.md`
- `P-015` → ACCEPTED → APPLIED → `v2/epoch-6-host-startup.md`
- `U-001` → RESOLVED BY HUMAN (2026-09-27) → входит в `P-002` → оси `Microsoft.CodeAnalysis` в README, authoring, эпохе 2
- `A-001` → HUMAN EXCLUDED (2026-09-27), не `PENDING INDEPENDENT REVIEW` → NOT APPLIED → остаток записан в `v2/README.md`, гейт не добавлен
