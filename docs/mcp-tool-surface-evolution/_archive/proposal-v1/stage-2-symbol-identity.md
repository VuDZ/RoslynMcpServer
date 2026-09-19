# Stage 2 — symbol identity (1.x, additive)

Статус: **план; реализация не начата**. SemVer: **minor** (новые tools +
optional params). Старые имена и обязательные параметры не ломать.

Mapping: R-§2, §3, §5, §6, §7 (`symbolId` на rename), частично §21.
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

Не invent handle. Использовать `Microsoft.CodeAnalysis.SymbolKey` (строка).

- Создавать от `ISymbol` на **published** solution
  (`GetPublishedSolutionAfterDiskSyncAsync`).
- Resolve: `SymbolKey.Resolve` против того же snapshot.
- Ambiguous / missing → candidates или явная ошибка, не silent
  `PickPrimarySymbol`.
- Сервис через DI (не `new` в tool hosts).
- `reset_workspace` без повторного load: id не обязан резолвиться.
- Generated source: честно задокументировать лимит
  `SymbolFinder.FindDeclarationsAsync` (уже в ARCHITECTURE). Metadata vs
  source — origin в `get_symbol_info`, если дёшево; иначе Stage 3.

Отдельный тул `resolve_symbol` (§6, §21) **не обязателен**, если
`get_symbol_info` принимает query / location / уже известный `symbolId`.
Чистое имя — кандидат Stage 3.

## Новые tools

`get_symbol_info`:

- вход: `symbolName` и/или `filePath`+`line`+`column` и/или `symbolId`;
- выход: `kind`, FQN, containing type, return/params, accessibility,
  базовые flags (`isAsync` / `isVirtual` по применимости), source location,
  **`symbolId`**;
- расширенные поля §5 (generic args, XML summary, attributes, nullable,
  implemented/overridden) — не блокер первой поставки.

`get_symbol_source`:

- workspace snapshot (не disk-first, как нынешний `get_method_body`);
- method, constructor, property, accessor, field, event, type
  (class/record/struct/interface/enum), local function;
- overload-aware.

`get_symbol_outline(symbolId)` (§3): желательно, если это тонкая обёртка
над walker'ом `get_class_skeleton` для произвольного типа. Если нет —
оставить `get_class_skeleton` и перенести generic outline в Stage 3.

`get_method_body` не удалять: может позже делегировать в `get_symbol_source`
для методов.

## Optional params на существующих (compatible)

- `find_usages`: `filePath?`, `line?`, `column?`, `symbolId?`. Location или
  id → конкретный `ISymbol`; иначе нынешний name search + primary pick.
  Descriptions: предпочитать этот тул; declaring file больше не обязателен
  для точного режима.
- `find_symbol_references`, `get_method_body`, `get_call_graph`,
  `rename_symbol`: optional `symbolId`.
- Ответы `find_symbol_definition` / `find_usages` печатают `symbolId`.

`find_symbol_references` **остаётся** (R-§2 modification). Удаление/rename
в `find_references` — Stage 3.

## Тесты

- Два overload с одним short name → разные `symbolId`, source не смешивается.
- Nested type / same name в двух проектах.
- Key переживает disk sync документа; не обязан переживать reset без reload.
- Ambiguous name без location → candidates, не «угадали не тот».

## Не входит

- `get_diagnostics(scope)` (§8) — Stage 3.
- `add_member` / `remove_symbol` / `update_member_body` rename (§13, §21).
- Выравнивание decompiled symbol API (§15).
- Стабильные test/diagnostic/fix ids (§22).
- Перенос ILSpy или code-fix writes в `core`.

## Приёмка

- Minor bump (ожидаемо 1.4.0), новые имена в catalog (`full`; lite — решение
  отдельной строкой: как минимум `get_symbol_info` / `get_symbol_source`
  имеют смысл в `core`, если влезают в budget bytes после Stage 1).
- Старые вызовы без `symbolId` ведут себя как сейчас.
- README «Agent tools by version» + Reference.

## Версия

Minor. Новые public capabilities.
