# Сводка арбитража

| ID | Позиция Grok | Позиция Astra | Финальный вердикт | Обязательный результат |
|---|---|---|---|---|
| R-01 | Host/persistence не выбраны | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Выбрать host, capability/write matrix и failure contract |
| R-02 | Нет источника negative dependencies | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Versioned dependency evidence и reject unknown до hit |
| R-03 | Baseline 1.3.5 устарел | ACCEPT | ACCEPT | Перебазировать на commit и принятый lifecycle |
| R-04 | Capture/overlay identity неполны | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Разделить base, requested/effective overlay и readiness |
| R-05 | Epoch 2/3 не дают полезный workload | NEEDS CLARIFICATION | ACCEPT WITH MODIFICATION | E2 только checkpoint; O4/O5 обязательны для полного результата |
| R-06 | Одно-TFM go не доказывает цель | NEEDS CLARIFICATION | ACCEPT WITH MODIFICATION | Назвать workload; обязательные target TFM становятся gate |
| C-01 | Contract negative dependencies невыполним | ACCEPT | ACCEPT | Сохранить доказанное existence/absence/regions evidence |
| C-02 | Непокрытое событие ненаблюдаемо | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Discovery→coverage→capture→validation; late/lazy inputs |
| C-03 | RAM/disk identity расходятся | REJECT | REJECT | Ничего; V15 проверяет фактическую null/explicit semantics |
| C-04 | Completeness predicate отсутствует | ACCEPT | ACCEPT | Expected roots/instances/edges/inputs и empty-project policy |
| C-05 | Overlay не входит в hit contract | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Prepare/gate и bounded readiness metadata |
| C-06 | `obj` уничтожит hit-rate | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Не ослаблять hashes; измерить категории build invalidation |
| C-07 | «Конус» может быть всей монорепой | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Явные regions, budget и bounded fallback |
| C-08 | Lease protocol не задан | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Выбрать ownership/lifetime/cleanup protocol |
| C-09 | Encoding недоопределён | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Effective decoding и character/round-trip oracle |
| E0-01 | Host — первый feasibility gate | ACCEPT | ACCEPT | Host и write feasibility до production go |
| E0-02 | Unsupported нельзя только перечислить | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Detector-before-hit; positive/negative/untested раздельно |
| E0-03 | SG oracle не наблюдает output | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Расширить in-process full generated-output oracle |
| E0-04 | Perf gate можно обойти | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Разделить experiment и production; полный e2e budget |
| E0-05 | Metadata исследование смешано с cache | NEEDS CLARIFICATION | ACCEPT WITH MODIFICATION | Оставить отдельным comparator без влияния на cache go |
| E1-01 | Adhoc не сохраняет project changes | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Operation/capability/writer/result matrix |
| E1-02 | Старую сессию уже dispose-ят | ACCEPT | ACCEPT | Полная lifecycle transition/ownership table |
| E1-03 | Multi-membership конфликтует с tools | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Разделить sync memberships и query context |
| E1-04 | Overlay wording меняет default | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Session-sticky admission; on/off/reset/restart tests |
| E1-05 | Scanner-инвариант не в той эпохе | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Сквозной инвариант плюс baseline dirty-path test |
| E1-06 | Core refactor — production change | ACCEPT | ACCEPT | Regression matrix обычного load и hydrate |
| E1-07 | Documents не доказывают completeness | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Две evidence tables: hydration и admission |
| E2-01 | Unknown input даёт ложный hit | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Hit только через C-01 protocol; сохранить V12 |
| E2-02 | Zero-DTB подменяет e2e | REJECT | REJECT | Ничего: conjunctive acceptance уже задана |
| E2-03 | Включённый cache может не быть записан | ACCEPT | ACCEPT | Policy/source/capture/write status раздельно |
| E2-04 | Miss после edit противоречит цели | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | E2 — unchanged checkpoint; O5 закрыть до completion |
| E2-05 | Capture нужен idle/background | NEEDS CLARIFICATION | UNRESOLVED | Выбрать foreground/background и lifecycle после evidence |
| E2-06 | Store/API выпустят до admission | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Store correctness отдельно от public activation |
| E3-01 | Refresh под lock deadlock/lost update | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Under-lock API/queue/publication diagram и tests |
| E3-02 | Strict на каждый query убивает hot path | NEEDS CLARIFICATION | UNRESOLVED | Нужны freshness promise, cadence и budget |
| E3-03 | Любой Created вызывает DTB | ACCEPT | ACCEPT | Role/coverage event classifier |
| E3-04 | Refresh failure ломает availability | NEEDS CLARIFICATION | UNRESOLVED | Нужна policy stale/unknown/error по операциям |
| E3-05 | Content-only classifier недоказан | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Только proven document role; graph role приоритетна |
| E3-06 | Durable generation на каждую правку | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Разделить RAM freshness, validity и capture cadence |
| E3-07 | Watcher не покрывает ancestor/`obj` | ACCEPT | ACCEPT | Watch/probe coverage map и resource limits |
| E3-08 | Time suppress теряет external write | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Own-write bytes/hash/revision и pending state |
| E4-01 | 4C запрещён как interpreter | REJECT | REJECT | Оставить closed subset с unknown→load |
| E4-02 | Weak mode неотличим от strict | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Effective validation mode и evidence compatibility |
| E4-03 | Partial mode даёт ложные ответы | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Per-tool partial coverage или отказ |
| E4-04 | Test discovery и CLI расходятся | ACCEPT | ACCEPT | Явный discovery coverage и CLI target semantics |
| V-01 | Oracle должен иметь тот же host | REJECT | REJECT | Сохранить fresh MSBuild как независимый oracle |
| V-02 | V11 не изолирует detector | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Разделить reasons и non-member control |
| V-03 | Unsupported SG делает gate пустым | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Supported SG — full oracle; rejected — admission only |
| V-04 | Fixture-only позволяет public API | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Пройденный workload budget/hit-rate до activation |
| H-01 | `go` разрешает обход MUST | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Раздельные decision authorities и точные next actions |

Итого: **10 ACCEPT**, **33 ACCEPT WITH MODIFICATION**, **4 REJECT**,
**3 UNRESOLVED**.
