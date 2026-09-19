# Traceability v2

## 1. Original requirements

| Requirement | Original source | Review findings | Arbitration | Resulting v2 sections |
|---|---|---|---|---|
| CI test gate, packaging, счётчики | короткий текст зрелости; M-1…M-4 | E0-01 | E0-01 AWM | README ограничения; Stage 0 |
| `lite` = skeleton+body+rename | R-§1, §3, §7; FACT-1 | нет findings | не затронута | Stage 1 |
| Additive `symbolId` в 1.x без ломки имён | README 1.x; R-§5, §6, §7 | E2-01…E2-04 | все AWM | Stage 2 identity + call forms |
| Docs-only breaking v2 | README; R-§21 | нет findings | граница сохранена | Stage 3 |
| Не сжимать `full`, не менять process default | README; compact-tools | нет findings | сохранено | README ограничения |

`AWM` = ACCEPT WITH MODIFICATION.

## 2. Accepted and modified findings

| Finding | Verdict | Arbitration requirement | Resulting v2 section |
|---|---|---|---|
| E0-01 | AWM | SDK `10.0.x` в workflow; не `global.json`; приёмка на чистом runner | Stage 0 работы п.3 / приёмка; U-ARB-01 |
| E2-01 | AWM | Opaque session ID на публичных API 5.9.0; store session/project/decl; не SymbolKey | Stage 2 «Идентичность»; review-mapping R-§6 |
| E2-02 | AWM | ID-only для пяти старых tools; XOR режимы; `newName` required | Stage 2 «Публичные формы вызовов» |
| E2-03 | AWM | Project context в ID; location candidates; legacy first-hit оговорён | Stage 2 location; U-ARB-03 |
| E2-04 | AWM | Любая правка документа объявления → `stale-id`; другой файл не инвалидирует | Stage 2 lifetime / проверка / тесты |
| NEW-ARB-001 | decided | Preflight согласованного текста linked-пути; иначе `shared-path-conflict` | Stage 2 «Запись linked-пути» |

## 3. Rejected findings — closed behavior

Отклонённых исходных findings нет.

| Closed stronger variant | Source | Preserved v2 section |
|---|---|---|
| Обязательный `global.json` в Stage 0 | E0-01 граница | Stage 0 «Не входит»; U-ARB-01 |
| Internal SymbolKey adapter | E2-01 | Stage 2 identity; compile-time public API |
| Location-only на все старые tools | E2-02 | таблица режимов Stage 2 |
| Durable named + checksum / locals-only fail-closed | E2-04 | строгая инвалидация документа объявления |

## 4. Unresolved findings

Блокирующих UNRESOLVED у пяти исходных findings нет. Продуктовые вопросы
за пределами арбитража: [UNRESOLVED-v2.md](UNRESOLVED-v2.md).

| Gate | Origin | Resulting v2 section |
|---|---|---|
| U-ARB-01 SDK pin репозитория | E0-01 | Stage 0 «Не входит» |
| U-ARB-02 portable/durable ID | E2-01, E2-04 | Stage 2 «Не входит»; Stage 3 вопрос 6 |
| U-ARB-03 legacy filePath migration | E2-03 | Stage 2 location / «Не входит» |

## 5. Incoming review mapping (unchanged except R-§6)

Вердикты M-* и R-§* сохранены. Единственное нормативное исправление
mapping: R-§6 больше не предписывает `SymbolKey`. См.
[review-mapping.md](review-mapping.md).
