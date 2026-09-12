# Приёмка v3 S6 — согласование документации

Дата: 2026-09-12. Вердикт: **принимается (docs-only, v1.3.19).**
Актуальные нормативы больше не разрешают confirmed raw publication после
отказа opt-in и не оставляют stale candidate без проверки свежести.
Inaccessible открыт и не входит в подтверждённую поддержку; исходная серия
не объявлена завершённой. V3-R4 закрыт как документационное противоречие.
Самоотчёт [implementer](2c860d65-088a-4319-a08a-786579596c6c) не заменяет сверку.
Норматив: [s6-contract-alignment.md](s6-contract-alignment.md).

Независимая сверка (без runtime-матрицы — это S7): прочитаны целевые файлы
спеки и поиск живых формулировок getter-fallback / «A4-09 не блокер» /
«original после ошибки» / «inaccessible до rollout».

| Норматив | Статус |
| --- | --- |
| `docs/ARCHITECTURE.md` | актуальный: published getter, S2 freshness, S3–S5 publication, inaccessible open |
| README EN+RU `load_workspace` | fail-closed ≠ raw persist; capture withhold; inaccessible unsupported |
| v2 `README` «Текущее поведение v1.3.19» | S2–S5; fallback getter только в секции **историческое v1.3.5** |
| `LIFECYCLE-v2`, epoch-1 inventory | getter = `_solution`; readers = published accessors |
| epoch-2 | датированная поправка: raw original ≠ publish/execute |
| epoch-4 spec + acceptance | A4-09/A4-12 закрыты в продукте S2; исторический leftover 2026-09-11 сохранён |
| epoch-5 provenance | принятая матрица available/missing/foreign; inaccessible не gate уже принятого rollout |
| `u-arb-04-atomic-load-prepare` | таблица S3–S5; raw originals не разрешение исполнять |
| `UNRESOLVED-v2`, `FOLLOWUPS`, `TRACEABILITY-v2` | inaccessible open; 36/36 и 9/9 не доказательство отсутствия R1–R3 |
| v3 README | S1–S5 приняты; S7 и серия целиком не приняты |

csproj **1.3.19**, schema/catalog не менялись. Исторические v2 verdicts не
переписаны задним числом. Rewrite/skip для inaccessible не назначались.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| V3-R4 | Docs | противоречивые live-нормы | **закрыт** |
| S6-1 | Low | `docs/analyzer-shadow-copy-improvements/README.md` и `docs/analyzer-shadow-copy/README.md` всё ещё описывают getter fallback как текущее наблюдение | открыт — вне списка targets S6 |
| S6-2 | Low | `epoch-6-results.md` / `epoch-4-results.md` / хвосты «не чинить A4-09» в старых acceptance | открыт — исторический snapshot, не live norm |
| S6-3 | Low | `WorkspaceTools` `[Description]` / `McpToolHelpCatalog` короче README Reference (S3/S4 withhold) | открыт — schema не трогали, bump не нужен |

Следующий шаг — S7 (итоговый runtime).
