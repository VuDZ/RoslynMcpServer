# Stage 2 — symbol identity (1.x, additive)

Статус: **post-arbitration spec; реализация не начата**. SemVer: **minor**
(новые tools + изменение published JSON Schema addressing-полей). Старые
JSON-имена tools и параметров не удалять и не переименовывать.

Mapping: R-§2, §3, §5, §6, §7 (`symbolId` на rename), частично §21.
Арбитраж: E2-01…E2-04 — ACCEPT WITH MODIFICATION; NEW-ARB-001.
Не входит: diagnosticId/codeFixId/testId (§22), rename тулов (§23),
сжатие каталога.

## Цель

Агент резолвит символ один раз и дальше передаёт opaque id:

```text
find_symbol_definition / get_symbol_info
→ symbolId

get_symbol_info(symbolId)
get_symbol_source(symbolId)
find_usages(symbolId)          # или filePath+line+column
rename_symbol(symbolId, newName, previewOnly=true)
get_call_graph(symbolId)
```

Вместо повторного `filePath + className + methodName` (overloads, nested
types, partial, explicit interface impl).

## Идентичность

`symbolId` — непрозрачная строка, созданная сервером на **публичных** API
Roslyn **5.9.0**. Клиент формат не разбирает. Прямые вызовы internal Roslyn
API, reflection и internal adapter в производственном коде **не** входят в
Stage 2. `Microsoft.CodeAnalysis.SymbolKey` **не** использовать.

Сервис идентичности (DI, не `new` в tool hosts) для каждого выданного ID
хранит:

- load-session generation;
- `ProjectId`;
- `DocumentId`;
- якорь декларации;
- для символа с несколькими объявлениями — все относящиеся документы;
- fingerprint **полного текста** каждого документа объявления.

Создавать ID от `ISymbol` на **published** solution
(`GetPublishedSolutionAfterDiskSyncAsync`). Один resolver используют
`get_symbol_info`, `get_symbol_source`, navigation и rename. Разные проверки
в разных adapters запрещены.

### Lifetime

ID действует только в текущей load-сессии текущего процесса. Не обещать
переносимость между процессами и сессиями.

Прежний ID **невалиден** при:

- новом процессе;
- `reset_workspace`;
- фактической новой загрузке проекта/solution;
- смене TFM;
- исчезновении project/document.

Повторный cached load **той же** session ID не инвалидирует.

ID **переживает** disk sync, если не изменился полный текст всех документов
объявления выбранного символа и сохранились session и project context.
Изменение **другого** документа само по себе не даёт `stale-id`.

ID **не** переживает произвольную правку документа объявления. Checksum
одной декларации **не** доказывает сохранность идентичности.

### Проверка перед ID-based semantic read и edit

1. Получить текущий published snapshot после disk sync.
2. Проверить session / project / document.
3. Сопоставить fingerprints полных текстов документов объявления.
4. Восстановить symbol из сохранённого якоря.
5. Проверить kind и project context.

Если любая проверка не проходит — явный **`stale-id`**. Не выбирать
ближайший, первый или одноимённый символ. Candidates допустимы при
неопределённом имени или project membership, **никогда** после провала
проверки исторической идентичности ID.

Для edits: identity validation **до** построения кандидата на запись и до
preview, и повторно до apply. Существующий write freshness gate проверяет
write base **после** identity validation и её не заменяет.

`get_symbol_source` поддерживает local functions только в неизменённом
опубликованном snapshot.

Generated source: честно задокументировать лимит
`SymbolFinder.FindDeclarationsAsync` (уже в ARCHITECTURE). Metadata vs
source — origin в `get_symbol_info`, если дёшево; иначе Stage 3.

Отдельный тул `resolve_symbol` (§6, §21) **не обязателен**, если
`get_symbol_info` принимает query / location / уже известный `symbolId`.
Чистое имя — кандидат Stage 3.

## Публичные формы вызовов

Старые JSON-имена tools и параметров сохранить. Прежние валидные вызовы
без ID сохранить с теми же defaults и результатами в пределах существующего
legacy-контракта.

Для `find_usages`, `find_symbol_references`, `get_method_body`,
`get_call_graph`, `rename_symbol` поля прежнего адресования сделать
необязательными в MCP JSON Schema, чтобы был допустим ID-only вызов. При
отсутствии ID потребовать прежний набор полей, кроме явно нового
location-режима `find_usages`.

| Тул | Допустимые полные режимы |
| --- | --- |
| `find_usages` | `symbolId` **или** `filePath+line+column` **или** старый `symbolName` |
| `find_symbol_references` | `symbolId` **или** старые `filePath+symbolName` |
| `get_method_body`, `get_call_graph` | `symbolId` **или** старые `filePath+className+methodName` |
| `rename_symbol` | `symbolId+newName` **или** старые `filePath+symbolName+newName` |
| `get_symbol_info` | `symbolId` **или** полная location **или** query/name |
| `get_symbol_source` | `symbolId` |

Правила селекторов (runtime обязан; MCP schema может не выразить XOR):

- поля более одного режима адресования в одном запросе →
  **`conflicting-selectors`**, даже если они случайно указывают на один
  символ;
- не заполнен ни один полный режим → **`missing-selector`**;
- пустые строки считать отсутствующими.

`newName` всегда required в схеме и runtime. `scope` и `previewOnly=true`
сохранить. Для C# сигнатуры допустимо переставить `newName` перед optional
полями, сохранив публичные MCP JSON-имена.

В выводах `find_symbol_definition` и `find_usages` прикреплять ID к
конкретному кандидату, а не к неоднозначному набору результатов.

Ошибки `stale-id`, `conflicting-selectors`, `missing-selector` и
`shared-path-conflict` должны быть различимы для клиента.

`find_symbol_references` **остаётся**. Удаление/rename в `find_references` —
Stage 3.

## Location и linked-файлы

Для location-режимов при нескольких project memberships одного file path
возвращать различимые candidates (`project`/`document` и location) и
требовать выбора. Не вызывать `FindDocumentAsync` с first-hit как resolver
точного режима.

Для legacy path-only вызовов сохранить прежнюю совместимость и **явно**
ограничить гарантию точности для linked-файла. Общая миграция всех
filePath-tools на project-aware выбор — не Stage 2
([U-ARB-03](UNRESOLVED-v2.md#u-arb-03--миграция-legacy-filepath-tools)).

## Запись linked-пути (NEW-ARB-001)

До любой ID/location-based записи по пути с несколькими memberships:

1. сгруппировать все затронутые `Document` по нормализованному физическому
   пути;
2. доказать, что предлагаемый итоговый текст одинаков во всех контекстах
   и не оставляет memberships расходящимися.

Иначе **`shared-path-conflict`** до любых файловых изменений. При успехе
писать физический путь **один раз**, затем согласовать все опубликованные
memberships этого пути.

`rename_symbol(scope="project")` для общего файла **не** обещает изоляцию
физической записи: проектный scope Roslyn не делает файл приватным.

## Новые tools

`get_symbol_info`:

- вход: режимы выше; при query/name и неоднозначности — явная выдача
  candidates, не silent `PickPrimarySymbol`;
- выход: `kind`, FQN, containing type, return/params, accessibility,
  базовые flags (`isAsync` / `isVirtual` по применимости), source location,
  **`symbolId`** с указанным lifetime;
- расширенные поля §5 (generic args, XML summary, attributes, nullable,
  implemented/overridden) — не блокер первой поставки.

`get_symbol_source`:

- вход: `symbolId`;
- workspace snapshot (не disk-first, как нынешний `get_method_body`);
- method, constructor, property, accessor, field, event, type
  (class/record/struct/interface/enum), local function;
- overload-aware.

`get_symbol_outline(symbolId)` (§3): желательно, если это тонкая обёртка
над walker'ом `get_class_skeleton` для произвольного типа. Если нет —
оставить `get_class_skeleton` и перенести generic outline в Stage 3.

`get_method_body` не удалять: может позже делегировать в `get_symbol_source`
для методов.

## Тесты

Compile-time проверка сервиса идентичности против публичных API Roslyn
5.9.0; без reflection/internal adapter в производственном коде.

MCP schema и вызовы:

- ID-only для всех пяти старых tools;
- location-only для `find_usages` / `get_symbol_info`;
- каждый legacy-вызов;
- `newName` required;
- отсутствие и смешение selectors;
- defaults rename (`scope`, `previewOnly=true`).

Сценарии точности:

- два overload с одним short name → разные `symbolId`, source не
  смешивается;
- nested type / same name в двух проектах;
- два проекта с одинаковым `AssemblyName` и одноимёнными символами: ID
  выбирает нужное тело и проект rename;
- linked source: location возвращает project candidates; edit проходит
  только при согласованном общем тексте, иначе `shared-path-conflict` без
  записи; preview и apply;
- повторный вызов после sync **другого** файла сохраняет ID;
- изменение полного текста файла объявления — включая вставку / удаление /
  перестановку одноимённых local functions и замену декларации тем же
  текстом при иных изменениях файла — выдаёт `stale-id`;
- reset / new load / new process инвалидируют ID;
- проверить и read, и preview/apply rename;
- ambiguous name без location → candidates, не «угадали не тот».

Изменение published JSON Schema — minor 1.x, но это всё равно изменение
схемы: schema/call tests провести **до** minor release.

## Не входит

- Переносимые между процессами/загрузками ID и сохранение ID после
  изменения документа объявления
  ([U-ARB-02](UNRESOLVED-v2.md#u-arb-02--переносимый-или-durable-symbolid)).
- Общая миграция legacy filePath-tools на project-aware выбор (U-ARB-03).
- `get_diagnostics(scope)` (§8) — Stage 3.
- `add_member` / `remove_symbol` / `update_member_body` rename (§13, §21).
- Выравнивание decompiled symbol API (§15).
- Стабильные test/diagnostic/fix ids (§22).
- Перенос ILSpy или code-fix writes в `core`.

## Приёмка

- Minor bump (ожидаемо 1.4.0), новые имена в catalog (`full`; lite —
  решение отдельной строкой: как минимум `get_symbol_info` /
  `get_symbol_source` имеют смысл в `core`, если влезают в budget bytes
  после Stage 1).
- Старые вызовы без `symbolId` ведут себя как сейчас.
- ID-only вызовы пяти старых tools валидны.
- README «Agent tools by version» + Reference: lifetime ID, `stale-id` и
  отсутствие точности legacy path-only для linked-файла.

## Версия

Minor. Новые public capabilities и изменение JSON Schema addressing-полей.
