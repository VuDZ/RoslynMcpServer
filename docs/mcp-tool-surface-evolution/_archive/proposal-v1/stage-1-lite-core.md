# Stage 1 — lite как semantic core

Статус: **план; реализация не начата**. Новых public имён нет.

Mapping: R-§1, §3, §7, §8 (только docs), §14 (FACT-2), FACT-1, FACT-5.
Исторический бюджет compact-tools: startup-lite ~20 KB UTF-8 `tools/list`.
Сейчас (v1.3.24 README): lite **19 / ~18.3 KB**, full **63 / ~45.9 KB**.

## Цель

Агент в `lite` может закрыть основной цикл без `enable_tool_group`:

```text
load_workspace
→ get_class_skeleton / get_code_skeleton
→ get_method_body
→ rename_symbol (previewOnly=true)
→ run_specific_test / run_dotnet_build
```

Сейчас вторая половина пары skeleton+body недоступна в lite: body в `files`.
Rename — главная причина ставить MCP — сидит в `editing` (17 тулов сразу).

## Каталог

Источник: `Hosting/McpToolCatalog.cs`. Сейчас `InLiteCore` ≡ группа `core`
(`Core<>` vs `Group<>`). Перенос = смена группы, не отдельный флаг.

В `core` (lite):

- `get_method_body` (сейчас `files`);
- `rename_symbol` (сейчас `editing`); `previewOnly=true` остаётся default.

После переноса обновить `McpToolCatalogTests`, README Tool profiles /
Reference (EN+RU), при необходимости одну строку session policy в
`AGENTS.md.sample` (skeleton → body; rename через MCP, не grep).

Замерить minified `tools/list` UTF-8 lite, как в compact-tools epoch 4.
Ориентир: не раздувать сильно выше ~20 KB.

## Сознательно не в этой стадии

| Тул | Почему |
| --- | --- |
| `get_code_fixes` / `apply_code_fix` | §8/§20 хотят workflow в default; `apply_*` — write. Сначала замер KB. Read-only fixes — опционально *после* замера, не в обязательный diff |
| `update_method_body`, `implement_interface` | §10–11 keep, §20 кладут в компактный default. Write + размер. Остаются `editing` до Stage 3 |
| ILSpy | §15/§25 optional; §20 включает в ~22. Не lite |
| Слияние `find_*` | Stage 2 (params), Stage 3 (имена) |
| Смена default профиля `full` → `lite` | ломает compact-tools compat |

`get_changed_files` остаётся в `core` (ревьюер ★★, не ★).

## Docs без схемы (можно в том же patch)

Усилить в README / AGENTS (R-§8, R-§18):

- diagnostics → `get_code_fixes` → `apply_code_fix` (группа `editing`);
- `execute_dotnet_command` — fallback, не первый выбор.

## Приёмка

- Lite регистрирует прежние 19 плюс `get_method_body` и `rename_symbol`.
- `full` по-прежнему все прежние имена (count = 63, группы сдвинуты).
- Записаны новые byte sizes.
- Нет новых/удалённых tool names.

## Версия

Patch (membership / UX, не новая capability).
