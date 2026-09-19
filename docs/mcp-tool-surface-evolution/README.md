# MCP tool surface evolution — proposal v2

Дата: 2026-09-19. Статус: **post-arbitration specification, proposed, не
реализовано**. Канон этой темы. Не начинать код, пока нет явного запроса на
реализацию конкретной стадии.

Исторический v1, review, ответы и арбитраж — в
[_archive/](_archive/README.md). Эта ревизия включает
[_archive/arbitration/normative-change-set.md](_archive/arbitration/normative-change-set.md).
Stage 2 можно передавать в реализацию только по этому канону, не по v1.

Поводы:

- зрелость (packaging, documentation drift, отсутствие CI test gate);
- внешняя критика tool surface:
  [`roslyn_mcp_tool_review.md`](../../roslyn_mcp_tool_review.md)
  (полная версия; incoming, **не** норма).

Состояние репозитория на момент записи: **v1.3.24**, full **63** / lite **19**.
Источник истины имён и групп: [`Hosting/McpToolCatalog.cs`](../../Hosting/McpToolCatalog.cs).

Разбор полной критики по секциям: [review-mapping.md](review-mapping.md).
Трассировка арбитража: [TRACEABILITY-v2.md](TRACEABILITY-v2.md).
Открытые продуктовые вопросы (не блокируют v2): [UNRESOLVED-v2.md](UNRESOLVED-v2.md).

## Зачем эта серия

Сильная сторона сервера — не filesystem и не generic `dotnet`, а semantic
слой: символы, diagnostics/CodeActions, rename, skeleton/member reads,
targeted build/test, ILSpy. `lite` уже близко к правильному default **для
агента**. Следующий выигрыш — не «ещё меньше tools в `full`», а:

1. починить зрелость (CI, packaging, счётчики);
2. довести `lite` до пары skeleton + body и semantic rename;
3. сделать символ first-class (`symbolId`) **аддитивно** в 1.x;
4. отдельно специфицировать breaking v2, не удаляя имена до принятой нормы.

## Фиксированные ограничения 1.x

Совместимы с [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) и shipped
[compact-tools](../archive/compact-tools/README.md):

- `full` остаётся backward-compatible default процесса
  (`ROSLYN_MCP_TOOL_PROFILE` unset = `full`). Рекомендовать `lite` клиентам
  — да; менять default — нет (пока нет major/deprecation policy).
- Не удалять и не переименовывать public tool names в 1.x.
- Не вводить generic `dispatch(name, arguments)`.
- Runtime disable групп не добавлять.
- Новые tools / optional params — **minor**. Membership lite / docs / CI —
  **patch**, если схема существующих tools не ломается.
- `PackageVersion` (`0.1.0-beta`) не bumpать, пока нет намерения публиковать
  NuGet.
- Совместимость 1.x = ранее валидные вызовы остаются валидными. Ослабление
  `required` у addressing-полей ради ID-only — допустимо; удаление JSON-имён
  — нет.

## Стадии

Каждый файл — отдельный результат. Позднюю стадию не реализовывать раньше.
Код стадии не начинает следующую «заодно».

| Стадия | Единственный результат | SemVer | Зависимость |
| --- | --- | --- | --- |
| [0 — гигиена и CI](stage-0-hygiene.md) | Packaging, счётчики, test gate. Schema тулов не меняется | patch¹ | нет |
| [1 — lite semantic core](stage-1-lite-core.md) | `get_method_body` и `rename_symbol` в `core`; замер lite bytes | patch | 0 желателен |
| [2 — symbol identity 1.x](stage-2-symbol-identity.md) | session-scoped `symbolId` + `get_symbol_info` / `get_symbol_source`; ID-only и legacy на старых именах | minor 1.4 | 1 желателен |
| [3 — spec v2](stage-3-v2-spec.md) | Канон ~20 тулов, deprecation policy, mapping имён. **Без кода сжатия `full`** | docs | 2 даёт факты |

¹ Чистый `.github/workflows` без csproj — без bump версии сервера.

Stage 1 и docs-only граница Stage 3 не изменены арбитражем, кроме запрета
наследовать непубличный `SymbolKey` и считать Stage 2 переносимым ID.

```mermaid
flowchart LR
  s0[Stage0_hygiene_CI]
  s1[Stage1_lite_core]
  s2[Stage2_symbolId_1x]
  s3[Stage3_v2_spec]
  s0 --> s1 --> s2 --> s3
```

## Что серия явно не делает до конца Stage 3

- Сжимать `full` 63 → 20 ножом по каталогу.
- Удалять `find_symbol_references`, `manage_agent_scratchpad`, `files`, AST
  micro-edits.
- Делать `lite` единственным протокольным default.
- Считать filesystem/Git/dotnet «не наше» для headless-хостов.
- Вводить переносимый между процессами `symbolId` или сохранять ID после
  правки документа объявления.

## Связанные документы

| Роль | Документ |
| --- | --- |
| Runtime / инварианты | [ARCHITECTURE.md](../ARCHITECTURE.md) |
| Исторический compact catalog | [archive/compact-tools](../archive/compact-tools/README.md) |
| Incoming критика (полная) | [roslyn_mcp_tool_review.md](../../roslyn_mcp_tool_review.md) |
| Mapping секций → вердикт | [review-mapping.md](review-mapping.md) |
| Изменения v1 → v2 | [CHANGELOG-v2.md](CHANGELOG-v2.md) |
| Product catalog | корневой README, `McpToolCatalog` |
