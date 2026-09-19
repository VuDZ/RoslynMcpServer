# Stage 3 — spec Roslyn MCP v2 (docs only)

Статус: **план спеки; не канон API и не код.** Пишется после того, как
Stage 2 дал живой `symbolId` в 1.x — иначе JSON будет фантазией.

Mapping: R-§9, §13, §20–§26, deferred части §2, §6, §8, §15, §19, §22.
Цикл репо: proposal → falsify review → response → arbitration. Сжатие
`full` **не реализовывать**, пока нет принятой нормы.

Входной набросок сигнатур — §21–§22
[`roslyn_mcp_tool_review.md`](../roslyn_mcp_tool_review.md). Ниже — что
спека **обязана** решить, не копируя review как истину.

## Зачем отдельная стадия

1.x обещает: не удалять имена, `full` = compat default. v2 имеет смысл
только вместе с **deprecation / major policy**, которой в репо нет
(version-bump знает patch/minor, не removal).

Цель v2 по полному review (R-§24, §26): не «63 → 20», а intent-level tools
вокруг символа, bounded reads, preview-first edits, reusable ids.

## Вопросы, которые спека должна закрыть

1. **Default профиля.** `lite` как протокольный default vs `full` forever +
   docs. §1 хочет lite default; compact-tools — `full`.
2. **Состав агентского default (~20–22).** Разрешить FACT-5: §20 включает
   code-fix writes, `update_method_body`, `implement_interface`, ILSpy;
   §25 держит их optional. Выбрать один список и KB budget.
3. **Имена.** `find_references` vs сохранить `find_usages`;
   `get_type_outline` vs `get_class_skeleton`; `get_diagnostics` vs
   `get_diagnostics_for_file`; decompiled align (`get_decompiled_member`).
   Aliases-как-дубли запрещены compact-tools — нужен явный breaking или
   период двух имён (это и есть policy).
4. **`resolve_symbol` vs совмещённый `get_symbol_info`.**
5. **`add_member` / `remove_symbol` / `update_member_body`.** Слияние AST
   (§13) без generic `dispatch`.
6. **Stable ids сверх символов:** `diagnosticId`, `codeFixId`, `testId`
   (§22). Как соотносятся с нынешними `diagnosticId`+line+column и VSTest
   filter needles.
7. **`get_diagnostics(scope = file | project | solution)`** (§8) — стоимость
   и таймауты на большом .slnx.
8. **Source и decompiled** — общий result shape (§15, §21).
9. **`files`:** только headless/compat профиль (§14, §25) или навсегда в
   `full`.
10. **Scratchpad / logs / `stop_mcp_server` / raw `dotnet`** — удалить,
    вынести, оставить `operations`/`runtime`.
11. **Метрики §26.6** (меньше токенов, меньше ложных edit, меньше tool
    calls) — что измеряем и на каком репо; без цифр не активировать v2
    как «доказанный выигрыш».

## Кандидат surface из review (§20–§21)

Не норма. Чеклист для будущей спеки:

```text
load_workspace / reset_workspace / get_workspace_info
resolve_symbol
get_symbol_info / get_symbol_outline / get_symbol_source
find_references / find_implementations / get_call_graph
get_diagnostics / get_code_fixes / apply_code_fix
rename_symbol / update_member_body / implement_interface
add_member / remove_symbol
build / run_test / run_tests
explore_assembly + decompiled symbol info/source
```

Плюс discovery (`get_mcp_server_info`, groups/help), если v2 всё ещё
групповой.

Три test-тула (§16) не схлопывать только чтобы попасть в «около 20».

## Явный non-goal этой стадии как кода

- Реализация сжатия каталога.
- Generic `dispatch`.
- Runtime disable групп «потому что v2».

Результат стадии: файлы спеки под `docs/mcp-tool-surface-evolution/`
(или выделенный `docs/roslyn-mcp-v2/` после arbitration) и запись в
[`docs/README.md`](../../../README.md). Пока арбитража нет — этот файл остаётся
напоминанием границ, не JSON-контрактом.
