# Навигационная поверхность (пункт 8): name-based / FQN / positional

Статус: **proposal, не реализовано**. Это разбор слоёв. Ship: [implementation/08-navigation.md](implementation/08-navigation.md).

Base truth — локальный `main`
`46d796d258c9af5212caa4fb673cc896ab1ac53e` (v1.3.32); публичный
`origin/main` — `bc3ec0946def13ee575a81873854ad6bb1001758`, форк —
`VladD2/RoslynMcpServer@main` `58dc361248fb51c06f1ab5c131849bbb4a1a9883`.

Каталог отличий — [README.md](README.md). Багфиксы 10, 1–4, 6, 7 — [план переноса](port-candidates-1-2-3-4-7.md); п. 5 — [ненулевой exit](port-candidate-5-nonzero-exit.md).

Это единственный пункт сравнения, который является **фичей, а не багфиксом**.
Объём: git `--stat` по `Tools/NavigationTools.cs` — **1702 / 786** (сумма 2488
changed); итоговый файл форка ~**1517** строк (у нас ~688), плюс
`SourcePositionHelper` (~84) и `SearchOverflowHelper` (~71). Серия пересекается
с `docs/mcp-tool-surface-evolution/` Stage 2 (**`symbolId`**, не путать с S2
ниже — FQN-по-имени). Решение о реализации — вместе с той серией, но **S1
(line/column) ей не блокируется**: Stage 2 сам допускает addressing
`filePath+line+column` наряду с id.

## 1. Что у нас сейчас

`Tools/NavigationTools.cs`, константы (строки 17–22):

| Константа | Значение |
|---|---|
| `MaxReferences` | 20 |
| `MaxDefinitionLocations` | 200 |
| `MaxFindUsagesReferences` | 30 |
| `MaxFindUsagesSourceLineChars` | 400 |
| `MaxAmbiguousCandidatesListed` | 10 |
| `MaxImplementations` | 50 |

`find_usages` (строка 268) — единственный name-based вход:

- `SymbolFinder.FindDeclarationsAsync(project, name, ignoreCase: true,
  SymbolFilter.Type | SymbolFilter.Member)` по каждому проекту (строки 291–308);
- далее `PickPrimarySymbol` (строки 319, 619–635) — **слепой выбор одного**
  символа по приоритету `INamedType 400 > IMethod 300 > IProperty 250 > IEvent 200 > IField 150`;
- остальные совпадения печатаются как «candidates», ссылки считаются только по
  «primary»;
- результат обрезается `Take(30)` (строки 335–336) с сообщением об усечении
  (строки 373, 399).

`find_symbol_definition` / `find_symbol_references` требуют `filePath` и
разрешают символ по имени, объявленному в этом файле; `find_implementations`
принимает только простое имя.

## 2. Что даёт форк

Два режима у обоих `find_symbol_definition` / `find_symbol_references`
(описания тулов — `fork/Tools/NavigationTools.cs:41–48`, `292–300`):

**Без `filePath` — solution-wide по имени:**

- регистронезависимый поиск по всем объявлениям; **имя с `.` = точный FQN**
  (`ResolveDeclarationsAsync`, `GetSymbolFqn`, `NormalizeFqn` — строки 1176–1247);
- при коллизии имён — **все** объявления, summary-таблица с группировкой ссылок
  по FQN; без «слепого первого совпадения»;
- если FQN не найден — ошибка со списком кандидатов, **без** fallback на простое
  имя.

Ограничение: member FQN форка — `Type.Member` без сигнатуры, поэтому overloads
одного метода имеют одинаковый FQN и выбираются все вместе. Это не стабильный
symbol identity; для точного overload нужен position или будущий `symbolId`.

**С `filePath` — позиционный SymbolFinder:**

- `line` (1-based) на объявлении **или** на использовании; `column` необязателен
  и вычисляется по первому вхождению `symbolName` на строке
  (`ComputeColumnOnLine`, строка 1411);
- fallback: если имя не найдено на точной строке — поиск в теле охватывающего
  члена (`FindSymbolInEnclosingMember`, строка 1469; `GetMemberBody`/`GetPropertyBody`,
  строки 1516/1530), включая brace-less однострочные конструкции;
- `SourcePositionHelper` (`Services/SourcePositionHelper.cs`) — LSP-style
  преобразование line/column → offset и разрешение символа в позиции.

Ограничение auto-column: exact-line path использует текстовый `IndexOf`, поэтому
первое совпадение в строке/comment/string или повторное имя может выбрать не тот
token. Syntax-token поиск добавлен только для enclosing-member fallback. При
переносе auto-column должен искать identifier tokens и возвращать ambiguity,
либо пользователь обязан передавать `column` при нескольких совпадениях.

**Фильтрация virtual dispatch:**

- `ClassifyVirtualReferencesAsync` (строка 964) + `IsDirectVirtualReference`
  (1010) + `GetCallSiteReceiverType` (1038);
- `directOnly: true` (только с `filePath`) оставляет ссылки, у которых статический
  тип получателя — объявляющий тип или производный; по умолчанию все ссылки плюс
  счётчик dispatch-сайтов;
- `IsSameOrDerivedFrom` / `IsSameType` / `TypeIdentityKey` (1088–1121) — при
  сравнении source-типа с его metadata-двойником в другой компиляции
  `SymbolEqualityComparer.Default` не матчит, поэтому используется canonical
  identity по FQN. Это отдельный регресс-кейс `fork@32c64a6`, который без такой
  подстраховки теряет cross-project call sites. Но один FQN без assembly identity
  может склеить одноимённые типы из разных assemblies; fork сознательно ошибается
  в сторону recall (оставить лишнюю ссылку), это не полноценная identity-модель.

**Ограничение результата без потери данных:**

- `maxResults`: аргумент > config `max-results` (`Config/WorkspaceConfig.cs`,
  `DefaultMaxResults = 50`) > default 50;
- при превышении — тот же markdown пишется в
  `%Temp%\roslyn-mcp\<yyyyMMdd-HHmmss-shortguid>\result.md`, агенту возвращается
  count + путь + summary (`Services/SearchOverflowHelper.cs`); молчаливой обрезки нет;
- `preview` (arg/config, default false) — включать текст исходной строки;
  по умолчанию только `path:line:col`.

Operational caveat: temp-файлы не очищаются, не имеют общего size/retention cap,
а путь на filesystem MCP-host может быть недоступен удалённому клиенту. Для нас
S3 требует lifecycle/security policy или cursor/chunking; helper форка не
переносится дословно.

**Побочные изменения поверхности:**

- `find_usages` в форке **удалён** — покрыт `find_symbol_references` без `filePath`;
- `find_implementations` получил FQN-режим, группировку по базовому типу
  (`| FQN | Kind | Types |`, строки 609–646), `maxResults`, `preview`;
- `get_call_graph` получил `line`/`column` (метод по позиции) и `maxNodes` на
  **сумму** callers+callees с overflow в temp-файл.

## 3. Разрыв по сценариям

| Сценарий | Сейчас | Форк |
|---|---|---|
| «Где объявлен `Foo.Bar.Baz`?» | нужно знать файл | FQN-имя без `filePath` |
| Общее имя (`Add`, `ToString`) в 30 типах | один «primary» + список кандидатов | все объявления, ссылки по FQN |
| «Что вызывает метод в строке 120?» | нужен `filePath` + имя | `line` (+ `column` авто) на usage |
| Одноимённые члены в одном файле | ошибка/первый матч | кандидаты FQN + line:col |
| Virtual/override: сайты в чужой иерархии | не фильтруются | `directOnly` + счётчик dispatch |
| >30 ссылок | обрезка + просьба сузить | полный результат в temp-файле |

## 4. Предлагаемый объём (если решаем делать)

Разбито на слои, чтобы можно было останавливаться:

| Слой | Содержимое | Ценность | Оценка |
|---|---|---|---|
| S1 | `SourcePositionHelper` + `line`/`column` в `find_symbol_definition`/`find_symbol_references`; auto-column по syntax tokens, не `IndexOf` | высокая, дешёвая | малая/средняя |
| S2 | `filePath` optional + name-based/FQN резолв + выдача всех коллизий; overload без signature остаётся группой до `symbolId` | высокая | средняя |
| S3 | `maxResults` + `preview`; overflow через lifecycle-managed artifact/cursor, не бесконечный temp | средняя | средняя |
| S4 | fallback «символ на строке не найден → тело охватывающего члена» | средняя | средняя |
| S5 | `directOnly` + classification виртуальных ссылок + canonical type identity | средняя | высокая (Roslyn-семантика, много краевых случаев) |
| S6 | Решение по `find_usages` (оставить, пометить deprecated или слить) | продуктовая | малая |

S1–S3 дают основной прирост UX при умеренном риске. S5 — самый дорогой и
единственный, где ошибка тихо меняет результат для агента (пропущенные ссылки),
поэтому его стоит делать либо последним, либо не делать.

## 5. Acceptance

- S1: `find_symbol_definition(filePath, line, symbolName)` работает как на
  объявлении, так и на использовании; `column` вычисляется по identifier token,
  когда совпадение единственно; ambiguity просит явную column; ошибки позиции —
  человекочитаемые (line/column вне диапазона, символ не найден).
- S2: `find_symbol_references(symbolName: "Ns.Type.Method")` без `filePath`
  находит все ссылки; при 3 одноимённых объявлениях возвращаются все три группы,
  а не одна произвольная; FQN-промах возвращает кандидатов и не падает на
  простое имя.
- S3: при превышении `maxResults` результат не теряется — доступный клиенту
  artifact/cursor + summary; есть retention и общий size cap; ни один результат
  не обрезается молча.
- Регресс: текущие `find_symbol_references` / `find_symbol_definition` c
  `filePath` без позиции сохраняют поведение; `MaxReferences` /
  `MaxDefinitionLocations` не конфликтуют с `maxResults`.

## 6. Открытые вопросы

1. **`find_usages`.** У нас он документирован в `AGENTS.md.sample` и в правилах
   как основной name-based вход. Слияние с `find_symbol_references` (как в форке)
   уменьшает поверхность, но ломает инструкции агентам и таблицу tools.
   Варианты: (а) оставить как алиас с пометкой в описании, (б) пометить
   deprecated и обновить AGENTS/README, (в) удалить. В 1.x удаление имени
   запрещено `mcp-tool-surface-evolution` («не удалять public tool names»).
2. **Взаимодействие с `mcp-tool-surface-evolution` Stage 2 (`symbolId`).**
   - Навигационный **S1** (позиция) = location-режим Stage 2; можно делать
     раньше id.
   - Навигационный **S2** (FQN без `filePath`) = query-режим, которым Stage 2
     всё равно должен выдавать первый id (`get_symbol_info`). Не заводить второй
     FQN-резолвер, когда появится identity-сервис — один `ResolveDeclarationsAsync`.
   - Навигационный **S5** (`directOnly` / canonical type identity) не заменяет
     `symbolId` и не должен обходить SymbolEqualityComparer своими правилами
     там, где Stage 2 уже задал identity.
3. **`max-results` из config.** У нас нет конфиг-файла (`RoslynMcp.jsonc`); брать
   только аргумент + дефолт 50 или вводить env-переменную (`ROSLYN_MCP_MAX_RESULTS`)?
4. **`search_code` и ripgrep** (`Services/RipgrepRunner.cs`,
   `SearchScopeResolver.cs`) — в этот объём не входят (каталог гэп 11); если S3
   делается, overflow abstraction стоит проектировать нейтральной и с
   retention/size/access policy, а не копировать `SearchOverflowHelper`.
5. **`directOnly` по умолчанию выключен** — принимаем ли мы, что агент может не
   знать про флаг и получить «шумные» ссылки, или включаем эвристику с пометкой?
