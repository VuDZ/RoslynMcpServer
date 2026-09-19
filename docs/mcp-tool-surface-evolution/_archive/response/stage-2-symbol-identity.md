# Stage 2 — ответы

ID: E2-01
Verdict: ACCEPT

Критика верна. Предписанный `Microsoft.CodeAnalysis.SymbolKey` в Roslyn 5.9.0 internal; прямой вызов из сервера не компилируется, а спека одновременно запрещает собственный handle.

Требования:
Incoming [`roslyn_mcp_tool_review.md`](../roslyn_mcp_tool_review.md) §6: «opaque server-side symbol handle is sufficient»; «external API does not have to expose raw Roslyn internals». Чат: symbol-oriented 1.x, не ломка имён.
Shipped: [`RoslynMcpServer.csproj`](../../../../RoslynMcpServer.csproj) PackageReference Workspaces **5.9.0**. В дереве нет обращения к `SymbolKey`. `InternalsVisibleTo` только на `RoslynMcpServer.Tests` / `LifecycleTestHost`, не на Roslyn. [ARCHITECTURE.md](../../../ARCHITECTURE.md): `InProcessAnalyzerAssemblyLoader` — **public-API-only**. Probe ревью (`SymbolKey.IsVisible=False`) согласуется с `internal partial struct SymbolKey`.

Что менять:
[stage-2-symbol-identity.md](../proposal-v1/stage-2-symbol-identity.md) «Идентичность»: снять «Не invent handle» и MUST на `SymbolKey.Create`/`Resolve`.
MUST: MCP `symbolId` — непрозрачная строка сервера, не сериализация internal `SymbolKey`.
MUST: реализация компилируется против публичного API Roslyn 5.9.0 (не `IgnoresAccessChecksTo` как норма Stage 2).
MUST: в identitity входит контекст проекта/компиляции опубликованной сессии (см. E2-03).
Рекомендуемый 1.x механизм: session-scoped handle в DI-сервисе (`ProjectId` + `DocumentId` + declaring span/kind + checksum синтаксиса объявления); жизнь = load-сессия; `reset_workspace` инвалидирует (уже в спеке). Internal `SymbolKey` не является контрактом протокола.

Последствия:
API: формат `symbolId` и его lifetime (процесс/сессия, не portable across PID). Модель: серверный store, не «голая» строка Roslyn. Тесты: compile-time spike на 5.9.0 до кода tools. Совместимость: 1.x additive. Persistence на диск — нет.

Шире finding:
[NEW-D-01](summary.md#new-d-01) — «stable» id vs session-scoped handle.

---

ID: E2-02
Verdict: ACCEPT

Критика верна. Добавление optional `symbolId` при сохранении C# `string` без default не даёт вызовов из примеров стадии (`find_usages(symbolId)`, `rename_symbol(symbolId, newName)`).

Требования:
Серия 1.x ([README.md](../proposal-v1/README.md) «Фиксированные ограничения»): не удалять/переименовывать tool names; старые вызовы остаются валидны. Compact-tools: не менять *имена*; не dispatcher.
Stage 2 цель (строки 18–22) уже описывает ID-only вызовы. Строка 4 («обязательные параметры не ломать») противоречит этой цели: это не MUST заказчика, а слишком узкая формулировка совместимости.
Shipped: `FindUsages(string symbolName)`, `RenameSymbol(string filePath, string symbolName, string newName)` без default; пустые строки отклоняются ([NavigationTools.cs](../../../../Tools/NavigationTools.cs), [UtilityTools.cs](../../../../Tools/UtilityTools.cs)). [`McpToolCatalogTests.Public_tool_schemas_preserve_required_default_and_type`](../../../../RoslynMcpServer.Tests/McpToolCatalogTests.cs) зеркалит `required` из сигнатуры.

Что менять:
[stage-2-symbol-identity.md](../proposal-v1/stage-2-symbol-identity.md) шапка и «Optional params»: совместимость = **ранее валидные вызовы остаются валидными**, не «все addressing-поля остаются required».
Для каждого затронутого tool — режимы и required:

- ID: `symbolId` (+ `newName` для rename);
- location: `filePath`+`line`+`column` (и имя, если без id);
- legacy: нынешние обязательные поля без `symbolId`.

Конфликт одновременно валидных селекторов, указывающих на разные символы → ошибка, не silent prefer. `newName` остаётся required у rename. Schema tests: ID-only, location-only, legacy.

Последствия:
API/JSON Schema: часть свойств уходит из `required` (C# `string? = null`). Старые клиенты, которые поля шлют, не ломаются. Каталожный тест required/default обновить под новые optional. Валидация в адаптерах: ветвление режимов.

Шире finding:
нет.

---

ID: E2-03
Verdict: ACCEPT

Критика верна. Строка `SymbolKey` (и любая identity без проекта) не отличает одноимённые символы в двух компиляциях с одной assembly identity; location через `FindDocumentAsync` тоже берёт первый Document.

Требования:
Incoming §6: точная identity при overloads, nested, нескольких проектах. Stage 2 «Тесты»: «same name в двух проектах» — обещание, которое голый ключ assembly-identity не держит.
Shipped: `SymbolKey.Resolve(Compilation, …)` (не Solution); XML 5.9.0 сравнивает assembly identity. Probe ревью: `equalKeys=True`, `AkeyInB=B.cs`. [`SolutionManager.FindDocumentAsync`](../../../../Services/SolutionManager.cs) 434–437: `FirstOrDefault` по пути.

Что менять:
[stage-2-symbol-identity.md](../proposal-v1/stage-2-symbol-identity.md) «Идентичность» / «Новые tools» / «Тесты»: `symbolId` MUST включать идентификатор проекта (или эквивалент compilation) **текущей published-сессии**. Reload / смена TFM / `reset_workspace` → id не резолвится (уже близко к спеке, сделать явным).
Location-режим: несколько memberships одного пути → candidates, не первый hit.
Приёмка: два проекта с одним `AssemblyName` и разными телами; linked document; доказательство read/rename именно выбранного проекта.

Последствия:
API: envelope/handle, не одна строка ключа. Resolve: выбор `Project.GetCompilationAsync` по сохранённому `ProjectId`. `FindDocumentAsync` для Stage 2 location нельзя переиспользовать as-is. Тесты как в «Что менять». Write boundary: rename должен идти от выбранного project context.

Шире finding:
[NEW-D-02](summary.md#new-d-02) — `FindDocumentAsync` FirstOrDefault уже в production filePath-tools, не только Stage 2.

---

ID: E2-04
Verdict: ACCEPT

Критика верна. Ключ локальной функции по индексу среди same name+kind после вставки соседней `L()` резолвится в чужую функцию без ambiguity; disk sync перед следующим tool как раз подставляет свежий snapshot.

Требования:
Stage 2 цель: повторно использовать id после disk sync; `get_symbol_source` включает local function. Incoming §3 перечисляет local functions как kind source, не как durable identity после правки соседей.
Shipped: [`GetPublishedSolutionAfterDiskSyncAsync`](../../../../Services/SolutionManager.cs) flush dirty → текущий `_solution`. Write freshness gate не доказывает тождество символа. Probe: old key → `newlyInserted` local. XML SymbolKey: interior symbols by index.

Что менять:
[stage-2-symbol-identity.md](../proposal-v1/stage-2-symbol-identity.md) «Идентичность», `get_symbol_source`, «Тесты»:
- Durable reuse `symbolId` после disk sync — только для **именованных деклараций** (type / method / property / event / field / constructor) при совпадении checksum (или эквивалента) declaring syntax; mismatch → явный stale-id, не «успешный» чужой символ.
- Local function / lambda / anonymous: source в **том же** published snapshot по location допустим; reuse id после sync, который меняет индекс/соседей, MUST fail-closed. Не лечить first-match: в probe результат уже единственный и неверный.
Приёмка: вставка, удаление, перестановка одноимённых local functions → stale или прежняя цель, не silent retarget.

Последствия:
API: контракт lifetime/stale. Модель handle: checksum/span, не индекс среди тёзок. Тесты E2-04. Observability: stale-id в ответе tool. Rename local после sync без проверки — запрещён этим контрактом.

Шире finding:
нет — тот же класс, что E2-01 (нельзя опираться на семантику internal SymbolKey как на «стабильный id»).
