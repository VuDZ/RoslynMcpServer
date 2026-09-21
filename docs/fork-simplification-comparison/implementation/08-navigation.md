# 8 — Навигация (серия, не один патч)

Pri **P2**, крупно. SemVer: **minor** при новых параметрах тулов.
Зависит желательно от п. 4 (sanitizer на solution-wide поиске).
Разбор слоёв: [../navigation-surface-8.md](../navigation-surface-8.md).

## Цель

S1–S3: позиция `line`/`column`, name/FQN без обязательного `filePath`, коллизии
всеми группами, overflow без молчаливой обрезки. Не удалять `find_usages` в 1.x.

## Порядок слоёв (отдельные commits)

1. **S1** — `SourcePositionHelper` + `line`/`column` на definition/references;
   auto-column по identifier token, не `IndexOf`. Совместимо со Stage 2
   location-режимом `mcp-tool-surface-evolution`.
2. **S2** — optional `filePath`, FQN-резолв, все коллизии вместо
   `PickPrimarySymbol`. Один резолвер; когда появится `symbolId` — не заводить
   второй.
3. **S3** — `maxResults` + preview; overflow = lifecycle artifact, не сырой
   `%Temp%` форка (retention/size). Env `ROSLYN_MCP_MAX_RESULTS` или только arg.
4. S4 fallback enclosing member — по желанию после S1.
5. S5 `directOnly` — последним или не делать (тихий пропуск ссылок).
6. S6 — `find_usages` остаётся именем; в Description можно пометить как alias.

## Не копировать

Удаление `find_usages`; `RoslynMcp.jsonc` `max-results`; overflow helper без
политики; S5 до identity-сервиса Stage 2.

## Acceptance (минимум S1)

`find_symbol_definition(filePath, line, symbolName)` на usage и на declaration;
ошибки позиции человекочитаемые. Текущие вызовы без `line` без регресса.
