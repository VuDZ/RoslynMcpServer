# Epoch 4 — Measured optional directions

Статус: **optional направления**, каждое с отдельным design, gates и handoff.
O5 при этом остаётся обязательным для `series complete`; его реализация может
использовать 4C или иной отдельно доказанный механизм.

## 4A. Partial graph и metadata references

- Scope/metadata mode имеют canonical roots и отдельные RAM/disk keys.
- Partial mode не выполняет скрытую auto-expand; expand — новый explicit scope.
- Для каждого semantic/refactoring tool до activation задаётся:
  `partial result + coverage` либо `refuse/full-scope required`.
- Refactoring, требующий полноты, не выполняется по одному hint.
- `get_test_list` сообщает syntax coverage, requested roots и incomplete status.
- CLI build/test следует явно выбранной disk target, не partial semantic graph.
- Project/binary ambiguity не разрешается первым совпадением.
- Metadata comparator не выдаётся за full-source equivalence; stale/corrupt DLL
  обрабатывается заявленным fallback.

Trace: E4-03 — ACCEPT WITH MODIFICATION; E4-04 — ACCEPT; O7.

## 4B. Validation modes

Для `strict | stat | watcher` описываются effective guarantees и compatibility
`producer evidence -> reader policy`. Strict reader слабого payload выполняет
полную проверку либо miss. Semantic admission не смешивается с freshness mode.
Schema/namespace меняется только при несовместимой форме evidence.

Нельзя тихо заменить strict результат stat/watcher. Выбор live policy остаётся
U-ARB-02.

Trace: E4-02 — ACCEPT WITH MODIFICATION.

## 4C. Content/membership reuse без полного DTB

- Разрешён только доказанный closed subset Include/Exclude/Remove/conditions.
- `unknown -> ordinary load` до partial publication.
- Общий интерпретатор MSBuild запрещён.
- Negative dependencies, imports, linked memberships и target inputs
  перепроверяются.
- Project-instance mapping и транзитивная инвалидация обязательны для
  per-project reuse.
- Изменение content и membership имеют разные rules/reasons.
- Вся equivalence/admission/performance matrix повторяется для нового profile.

Это допустимое направление, а не доказательство feasibility.

Trace: E4-01 — REJECT (рекомендация удалить 4C закрыта); R-05, E2-04.

## 4D. Symbol index

- Index — только hint; Roslyn проверяет candidate в текущей solution.
- Identity включает document membership, project/TFM, parse options/defines,
  content hash и schema.
- Delete/project removal удаляет entries; invalid/miss использует ordinary search.
- Generated symbols не добавляются без отдельного contract/oracle.
- Реализация допускается только после измеренного bottleneck.

## Общая приёмка

Каждое направление имеет независимые experiment/implementation/activation
verdicts, target workload и заранее утверждённый budget. Результат одного
направления не является evidence другого и не ослабляет O8.
